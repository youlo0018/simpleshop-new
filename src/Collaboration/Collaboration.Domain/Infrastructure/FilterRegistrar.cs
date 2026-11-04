using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Collaboration.Domain.Context;
using Collaboration.Domain.Entities;
using FreeSql.Internal;

namespace Collaboration.Domain.Infrastructure;

/// <summary>查询过滤器注册器：用 FreeSql GlobalFilter 统一注入软删 / 租户 / 客户 / 公开可见性。</summary>
/// <remarks>
/// 为什么不用 Aop.ParseExpression：它的 Result 是**替换**整个 WHERE，不是追加。
/// 实测踩坑：不存在的账号 not_exist_user 能登录成功并返回库里第一条记录，
/// 业务条件 customer_name 被注入的 is_deleted=false 整个顶掉。
/// 那样部署等于租户隔离全线失效、可越权（REVIEW 里的 P0 级风险）。
/// GlobalFilter.ApplyIf 才是 FreeSql 为这类场景设计的原语，它是 AND 进查询的。
/// 依据：DATA_SPEC.md 3.2、3.2.1；BUSINESS.md 1.3、1.4。
/// </remarks>
public static class FilterRegistrar
{
    /// <summary>程序集 → 实体过滤元数据的缓存。</summary>
    /// <remarks>
    /// IFreeSql 是 Scoped（见 FreeSqlServiceCollectionExtensions 里为什么不能改成 Singleton），
    /// 每个请求都会重新注册过滤器。程序集扫描、接口查找、属性判定这些结果与请求无关，
    /// 缓存起来避免每个请求把全部实体类型反射扫一遍；只有带租户常量的表达式必须按请求重建。
    /// </remarks>
    private static readonly ConcurrentDictionary<Assembly, EntityCatalog> CatalogCache = new();

    /// <summary>实体类型 → 强类型 ApplyOnly 委托的缓存。</summary>
    private static readonly ConcurrentDictionary<Type, ApplyOnlyInvoker> InvokerCache = new();

    /// <summary>强类型 ApplyOnly 调用体，供反射构造委托。</summary>
    private static readonly MethodInfo ApplyTypedMethod = typeof(FilterRegistrar)
        .GetMethod(nameof(ApplyTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>「未软删」条件，与请求上下文无关，可以跨请求复用。</summary>
    private static readonly LambdaExpression SoftDeleteExpression = BuildSoftDelete(typeof(EntityBase));

    /// <summary>按实体类型调用 <c>GlobalFilter.ApplyOnly&lt;T&gt;</c> 的委托。</summary>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="name">过滤器名字。</param>
    /// <param name="where">过滤表达式。</param>
    private delegate void ApplyOnlyInvoker(GlobalFilter filter, string name, LambdaExpression where);

    /// <summary>为指定程序集里的全部实体注册全局过滤。</summary>
    /// <param name="freeSql">已构建的 FreeSql 实例。</param>
    /// <param name="entityAssemblies">实体所在程序集，通常是本服务的 Xxx.Domain。</param>
    public static void Register(IFreeSql freeSql, params Assembly[] entityAssemblies)
    {
        var ctx = TenantContextHolder.Current;

        // 软删是所有实体共有的规则。用 EntityBase 做 ApplyOnly，
        // FreeSql 会按「查询实体的类型是否可从 EntityBase 赋值」自动匹配，
        // 因此不需要为每个实体注册一遍，也不会给 platform 等根表叠加租户条件。
        ApplyOnly(freeSql.GlobalFilter, typeof(EntityBase), "soft_delete", SoftDeleteExpression);

        foreach (var assembly in entityAssemblies)
        {
            var catalog = CatalogCache.GetOrAdd(assembly, BuildCatalog);

            // 公开可见性与实体自身类型绑定，名字带类型名，避免不同接口实现互相覆盖。
            if (ctx.ShouldFilterPublicVisibility)
            {
                RegisterPublicVisibility(freeSql.GlobalFilter, catalog.PublicVisibility);
            }

            RegisterTenantFilters(freeSql.GlobalFilter, catalog.TenantRules, ctx);
        }

        RegisterCustomerFilter(freeSql.GlobalFilter, ctx);
    }

    /// <summary>注册后台租户过滤。</summary>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="rules">缓存下来的租户过滤规则（实体类型 + 是否有 merchant_id 列）。</param>
    /// <param name="ctx">当前租户上下文。</param>
    /// <remarks>
    /// <para>FreeSql 的 <c>ApplyIf</c> 不是按实体隔离的：它会被加入所有查询，
    /// 再由表达式翻译去碰运气。多个实体注册同名或不同名的过滤器时，
    /// 每个查询都会拿到全部条件，最后在 SQL 里出现重复字段或串表条件。</para>
    ///
    /// <para><c>ApplyOnly&lt;TEntity&gt;</c> 才是按类型生效的原语：
    /// FreeSql 在拼接 SQL 前会检查 <c>Only</c>，只把表达式参数类型可赋值的实体套进去。
    /// 这里为每个非租户根的后台实体单独注册，既避免重复叠加，
    /// 又让 <c>platform</c> 这种租户根表完全不进入租户条件。
    /// 平台根表的可见性由 <c>PlatformRepository</c> 显式限定。</para>
    /// </remarks>
    private static void RegisterTenantFilters(
        GlobalFilter filter, IReadOnlyList<TenantRule> rules, TenantContext ctx)
    {
        // Internal 也要跳过：内部调用没有平台/商户声明，套上「PlatformId == 0」
        // 会把所有行都过滤掉，内部接口直接查不到任何数据。
        if (ctx.IsSuperAdmin || ctx.IsCustomer || ctx.IsAnonymous || ctx.IsInternal) return;

        var merchantId = ctx.IsMerchant ? ctx.MerchantId : (long?)null;
        foreach (var rule in rules)
        {
            ApplyOnly(filter, rule.EntityType, "tenant:" + rule.EntityType.Name,
                BuildTenant(rule.EntityType, ctx.PlatformId, rule.HasMerchantColumn ? merchantId : null));
        }
    }

    /// <summary>注册客户私有数据过滤。</summary>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="ctx">当前租户上下文。</param>
    /// <remarks>客户令牌只能看自己的数据；该条件绑定到 CustomerEntityBase，不会串到后台实体。</remarks>
    private static void RegisterCustomerFilter(GlobalFilter filter, TenantContext ctx)
    {
        if (!ctx.IsCustomer) return;

        ApplyOnly(
            filter,
            typeof(CustomerEntityBase),
            "customer",
            BuildCustomer(typeof(CustomerEntityBase), ctx.UserId));
    }

    /// <summary>给实现了 <see cref="IPublicVisible{T}"/> 的实体挂「公开可见性」过滤器。</summary>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="rules">缓存下来的可见性规则（实体类型 + 接口方法）。</param>
    /// <remarks>
    /// 未实现该接口的实体直接跳过：可见性过滤只对「前台能看到的表」生效，
    /// 后台表不该被这条过滤影响。条件由实体自己实现（<c>BuildPublicCondition</c>），
    /// 而不是在这里写死「审核通过 + 已上架」—— 各表的公开口径本来就不一样。
    /// </remarks>
    private static void RegisterPublicVisibility(
        GlobalFilter filter, IReadOnlyList<PublicVisibilityRule> rules)
    {
        foreach (var rule in rules)
        {
            // 实体实例每次现建：BuildPublicCondition 当前都是无状态实现，
            // 但缓存实例会把「实现必须无状态」变成一条隐式约束，将来有人加个字段就会串请求。
            // 这里只缓存反射结果（类型 / 接口方法），实例创建的开销可以忽略。
            var instance = Activator.CreateInstance(rule.EntityType);
            if (instance is null) continue;

            if (rule.Method.Invoke(instance, new object[] { DateTime.UtcNow }) is LambdaExpression lambda)
            {
                ApplyOnly(filter, rule.EntityType, "public_visibility:" + rule.EntityType.Name, lambda);
            }
        }
    }

    /// <summary>把过滤器应用到单个实体类型（FreeSql 的 ApplyOnly 是泛型方法，这里走缓存委托）。</summary>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="type">实体类型。</param>
    /// <param name="name">过滤器名字，出现在 SQL 注释里便于排查。</param>
    /// <param name="where">过滤条件。</param>
    private static void ApplyOnly(GlobalFilter filter, Type type, string name, LambdaExpression where)
        => InvokerCache.GetOrAdd(type, BuildInvoker)(filter, name, where);

    /// <summary>构造「按实体类型调用 ApplyOnly」的强类型委托。</summary>
    /// <param name="type">实体类型。</param>
    /// <returns>委托实例；同一类型只构造一次。</returns>
    private static ApplyOnlyInvoker BuildInvoker(Type type)
        => ApplyTypedMethod.MakeGenericMethod(type).CreateDelegate<ApplyOnlyInvoker>();

    /// <summary>强类型调用体：把 LambdaExpression 转成 ApplyOnly 需要的泛型表达式。</summary>
    /// <typeparam name="T">实体类型。</typeparam>
    /// <param name="filter">全局过滤器。</param>
    /// <param name="name">过滤器名字。</param>
    /// <param name="where">过滤条件。</param>
    private static void ApplyTyped<T>(GlobalFilter filter, string name, LambdaExpression where)
        => filter.ApplyOnly<T>(name, (Expression<Func<T, bool>>)where, true);

    /// <summary>扫描程序集，缓存与请求无关的实体过滤元数据。</summary>
    /// <param name="assembly">实体所在程序集。</param>
    /// <returns>该程序集的实体过滤元数据。</returns>
    private static EntityCatalog BuildCatalog(Assembly assembly)
    {
        var publicVisibility = new List<PublicVisibilityRule>();
        var tenantRules = new List<TenantRule>();

        foreach (var type in SafeGetTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract) continue;
            if (!typeof(EntityBase).IsAssignableFrom(type)) continue;

            var iface = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPublicVisible<>));
            var method = iface?.GetMethod("BuildPublicCondition");
            if (method is not null)
            {
                publicVisibility.Add(new PublicVisibilityRule(type, method));
            }

            // 判定依据是「有没有 PlatformId / MerchantId 这两列」，**不是**继承自哪个基类。
            //
            // 🔴 只认 AdminEntityBase 会漏掉 Order：订单表没有审计列（创建人 / 操作人），
            // 所以 Order 继承 EntityBase，但它照样有 platform_id / merchant_id，
            // 照样必须被租户裁剪。漏掉它的后果实测过 ——
            // 商户 B 的后台账号能按 id 读到**商户 A 的订单详情**（列表是收窄过的，
            // 详情没有），订单号与实付金额一览无余。
            //
            // 用属性名反射而不是基类判断，以后再有实体走 EntityBase + 租户列也能自动覆盖，
            // 不需要有人记得回来改这里。
            // 只要有 platform_id 就够：商户维度是**可选**的。
            // BuildTenant 本来就是这么设计的（merchantId 为 null 时只比平台），
            // 而 marketing_config / user_role 这类表只有 platform_id，
            // 一刀切要求两列都在会把它们漏掉。
            if (typeof(ITenantRoot).IsAssignableFrom(type)) continue;
            if (type.GetProperty(nameof(AdminEntityBase.PlatformId)) is null) continue;

            // 没有 MerchantId 列的类型不能把商户条件加进去 ——
            // Expression.Property 找不到属性会直接抛。
            var hasMerchantColumn =
                type.GetProperty(nameof(AdminEntityBase.MerchantId)) is not null;

            tenantRules.Add(new TenantRule(type, hasMerchantColumn));
        }

        return new EntityCatalog(publicVisibility, tenantRules);
    }

    /// <summary>一个程序集里与过滤相关的实体元数据。</summary>
    private sealed class EntityCatalog
    {
        /// <summary>构造元数据。</summary>
        /// <param name="publicVisibility">公开可见性规则。</param>
        /// <param name="tenantRules">租户过滤规则。</param>
        public EntityCatalog(
            IReadOnlyList<PublicVisibilityRule> publicVisibility,
            IReadOnlyList<TenantRule> tenantRules)
        {
            PublicVisibility = publicVisibility;
            TenantRules = tenantRules;
        }

        /// <summary>公开可见性规则。</summary>
        public IReadOnlyList<PublicVisibilityRule> PublicVisibility { get; }

        /// <summary>租户过滤规则。</summary>
        public IReadOnlyList<TenantRule> TenantRules { get; }
    }

    /// <summary>公开可见性规则：实体类型 + 接口方法。</summary>
    private sealed class PublicVisibilityRule
    {
        /// <summary>构造规则。</summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="method">接口的 BuildPublicCondition 方法。</param>
        public PublicVisibilityRule(Type entityType, MethodInfo method)
        {
            EntityType = entityType;
            Method = method;
        }

        /// <summary>实体类型。</summary>
        public Type EntityType { get; }

        /// <summary>BuildPublicCondition 方法。</summary>
        public MethodInfo Method { get; }
    }

    /// <summary>租户过滤规则：实体类型 + 是否有 merchant_id 列。</summary>
    private sealed class TenantRule
    {
        /// <summary>构造规则。</summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="hasMerchantColumn">是否含 MerchantId 列。</param>
        public TenantRule(Type entityType, bool hasMerchantColumn)
        {
            EntityType = entityType;
            HasMerchantColumn = hasMerchantColumn;
        }

        /// <summary>实体类型。</summary>
        public Type EntityType { get; }

        /// <summary>是否含 MerchantId 列。</summary>
        public bool HasMerchantColumn { get; }
    }

    /// <summary>构造「未软删」条件：<c>x =&gt; !x.IsDeleted</c>。</summary>
    /// <param name="type">实体类型。</param>
    /// <returns>过滤表达式。</returns>
    private static LambdaExpression BuildSoftDelete(Type type)
    {
        var p = Expression.Parameter(type, "x");
        var body = Expression.Not(Expression.Property(p, nameof(EntityBase.IsDeleted)));
        return Expression.Lambda(body, p);
    }

    /// <summary>构造后台租户条件：平台账号按 platform_id，商户账号再叠加自己的 merchant_id。</summary>
    /// <param name="type">实体类型。</param>
    /// <param name="platformId">当前平台 Id。</param>
    /// <param name="merchantId">当前商户 Id；null 表示平台账号（不限商户）。</param>
    /// <returns>过滤表达式。</returns>
    /// <remarks>
    /// 商户根表（<see cref="IMerchantRoot"/>）是例外：它的 merchant_id 列恒为 0，
    /// 身份在 Id 上，所以商户账号要判 <c>Id == 我的商户Id</c>。
    /// </remarks>
    private static LambdaExpression BuildTenant(Type type, long platformId, long? merchantId)
    {
        var p = Expression.Parameter(type, "x");

        // 商户根表（merchant）：它的 merchant_id 列恒为 0，身份在 Id 上。
        // 商户账号看自己的记录要判 `Id == 我的商户Id`，判 merchant_id 会一条都查不到
        // （详见 IMerchantRoot 的说明）。
        // 平台账号那一侧不受影响：merchantId 为 null 时走的仍是 platform_id 条件。
        if (merchantId.HasValue && typeof(IMerchantRoot).IsAssignableFrom(type))
        {
            return Expression.Lambda(
                Expression.Equal(
                    Expression.Property(p, nameof(EntityBase.Id)),
                    Expression.Constant(merchantId.Value, typeof(long))),
                p);
        }

        var body = Expression.Equal(
            Expression.Property(p, nameof(AdminEntityBase.PlatformId)),
            Expression.Constant(platformId, typeof(long)));

        if (merchantId.HasValue)
        {
            body = Expression.AndAlso(body, Expression.Equal(
                Expression.Property(p, nameof(AdminEntityBase.MerchantId)),
                Expression.Constant(merchantId.Value, typeof(long))));
        }

        return Expression.Lambda(body, p);
    }

    /// <summary>构造 C 端客户条件：<c>x =&gt; x.CustomerId == 当前客户</c>。</summary>
    /// <param name="type">实体类型。</param>
    /// <param name="customerId">当前客户 Id。</param>
    /// <returns>过滤表达式。</returns>
    private static LambdaExpression BuildCustomer(Type type, long customerId)
    {
        var p = Expression.Parameter(type, "x");
        var body = Expression.Equal(
            Expression.Property(p, nameof(CustomerEntityBase.CustomerId)),
            Expression.Constant(customerId, typeof(long)));
        return Expression.Lambda(body, p);
    }

    /// <summary>安全地取程序集里的全部类型：个别类型加载失败时不炸整轮注册。</summary>
    /// <param name="assembly">程序集。</param>
    /// <returns>能加载出来的类型。</returns>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}



