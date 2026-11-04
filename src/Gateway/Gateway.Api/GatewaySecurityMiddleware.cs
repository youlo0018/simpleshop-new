using System.Globalization;
using System.Security.Claims;
using Collaboration.Web;
using Microsoft.Extensions.Options;

namespace Gateway.Api;

/// <summary>
/// 网关安全中间件：剥注入站的租户头 → 验签 → RBAC → 注入可信租户头。
/// </summary>
/// <remarks>
/// 顺序是刻意的，一步都不能换：
/// <list type="number">
/// <item><b>先剥</b>：把入站的 X-Claim-* / X-Internal-Token 全部删掉。
/// 不剥的话任何人发一个 X-Claim-PlatformId: 0 就是平台超管——这是整套租户隔离最容易被捅破的一层。</item>
/// <item><b>再验</b>：按 alg 分派验签（后台 RS256 / 客户 HS256）。</item>
/// <item><b>再鉴权</b>：路径绑定了权限点就必须持有它。</item>
/// <item><b>最后注</b>：用验签得到的主体重新写入 X-Claim-* 与 X-Internal-Token。
/// 写进去的值只来自令牌，一个字都不采信客户端。</item>
/// </list>
/// </remarks>
public sealed class GatewaySecurityMiddleware
{
    /// <summary>入站必须被剥离的头（大小写不敏感）。</summary>
    private static readonly string[] StrippedHeaders =
    [
        TenancyOptions.Headers.InternalToken,
        TenancyOptions.Headers.UserId,
        TenancyOptions.Headers.UserName,
        TenancyOptions.Headers.TenantType,
        TenancyOptions.Headers.PlatformId,
        TenancyOptions.Headers.MerchantId,
        TenancyOptions.Headers.Permissions,
        TenancyOptions.Headers.Roles
    ];

    private readonly RequestDelegate _next;
    private readonly DualTokenValidator _validator;
    private readonly AdminSessionRevocationChecker _revocations;
    private readonly CustomerSessionChecker _sessions;
    private readonly RoutePermissionCache _permissions;
    private readonly GatewayOptions _options;
    private readonly ILogger<GatewaySecurityMiddleware> _logger;

    /// <summary>构造中间件。</summary>
    /// <param name="next">下一段管道。</param>
    /// <param name="validator">双令牌验签器。</param>
    /// <param name="revocations">后台令牌吊销检查器。</param>
    /// <param name="sessions">客户会话检查器（登出 / 过期后立刻失效）。</param>
    /// <param name="permissions">RBAC 映射缓存。</param>
    /// <param name="options">网关配置。</param>
    /// <param name="logger">日志器。</param>
    public GatewaySecurityMiddleware(
        RequestDelegate next,
        DualTokenValidator validator,
        AdminSessionRevocationChecker revocations,
        CustomerSessionChecker sessions,
        RoutePermissionCache permissions,
        IOptions<GatewayOptions> options,
        ILogger<GatewaySecurityMiddleware> logger)
    {
        _next = next;
        _validator = validator;
        _revocations = revocations;
        _sessions = sessions;
        _permissions = permissions;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>执行安全处理。</summary>
    /// <param name="context">当前请求。</param>
    /// <returns>管道结果。</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // ---- 第 1 步：无条件剥离入站的租户声明头 ----
        foreach (var header in StrippedHeaders)
        {
            context.Request.Headers.Remove(header);
        }

        var path = context.Request.Path.Value ?? "/";

        // 探活端点不进鉴权：容器编排与 start-services.ps1 都要靠它判断服务是否就绪，
        // 加了鉴权会让「服务其实活着」被误判成「起不来」，进而反复重启。
        // 它不代理到任何下游、不返回业务数据，放开没有泄露面。
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/ready", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 匿名白名单：只登记登录 / 注册 / 换令牌 / 图片回源 / 游客浏览。
        //
        // 但**带了令牌的请求仍要验签并注入声明**：C 端商品卡要按登录客户自己的券算到手价，
        // 而「匿名路径直接放行」会让下游拿不到任何客户上下文 —— 那样它只能去信
        // 客户端传进来的 customerId（实测：传任意客户 Id 就能读到别人的券码）。
        // 没带令牌照旧按游客放行，所以「没登录也能逛」不受影响。
        if (IsAnonymous(path))
        {
            var anonymousToken = DualTokenValidator.ExtractBearer(context.Request.Headers.Authorization.ToString());
            if (anonymousToken is not null)
            {
                var anonymousOutcome = await _validator.ValidateAsync(anonymousToken);
                if (!anonymousOutcome.Succeeded)
                {
                    _logger.LogWarning(
                        "匿名路径上的令牌验签失败：{Path} {Error}", path, anonymousOutcome.Error);
                    await RejectAsync(context, StatusCodes.Status401Unauthorized,
                        new { error = "invalid_token", error_description = "The access token is not valid." });
                    return;
                }

                context.User = anonymousOutcome.Principal!;

                // 匿名路径上带了客户令牌，同样要过服务端会话：登出后即使令牌还没到期，
                // 也不该再用它读「按客户算的到手价」之类的个性化数据。
                if (!await CheckCustomerSessionAsync(context, anonymousOutcome, path))
                {
                    return;
                }

                InjectClaims(context, context.User);
            }

            await _next(context);
            return;
        }

        // ---- 第 2 步：验签 ----
        var token = DualTokenValidator.ExtractBearer(context.Request.Headers.Authorization.ToString());
        if (token is null)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized,
                new { error = "invalid_token", error_description = "Missing bearer token." });
            return;
        }

        var outcome = await _validator.ValidateAsync(token);
        if (!outcome.Succeeded)
        {
            _logger.LogWarning("令牌验签失败：{Path} {Error}", path, outcome.Error);
            await RejectAsync(context, StatusCodes.Status401Unauthorized,
                new { error = "invalid_token", error_description = "The access token is not valid." });
            return;
        }

        context.User = outcome.Principal!;

        // ---- 第 2.5 步：会话吊销 ----
        // 验签只能证明「令牌是我们签的、还没过期」，证明不了「签发之后账号被重置过密码」。
        // 只对后台令牌生效：客户令牌有自己的吊销语义（CustomerService 管），不在这里越界。
        if (outcome.Kind == DualTokenValidator.KindAdmin)
        {
            var userId = long.TryParse(First(context.User, "sub"), out var uid) ? uid : 0;
            var issuedAt = long.TryParse(First(context.User, "iat"), out var iat) ? iat : (long?)null;

            var revocation = await _revocations.CheckAsync(userId, issuedAt);

            if (revocation.StoreUnavailable)
            {
                await RejectAsync(context, StatusCodes.Status503ServiceUnavailable,
                    new { error = "authorization_unavailable", error_description = "The session store is unavailable." });
                return;
            }

            if (revocation.Revoked)
            {
                _logger.LogWarning("账号 {UserId} 的令牌已被吊销（重置密码），拒绝 {Path}", userId, path);
                await RejectAsync(context, StatusCodes.Status401Unauthorized,
                    new { error = "invalid_token", error_description = "The access token has been revoked." });
                return;
            }
        }

        // ---- 第 2.6 步：客户会话 ----
        // 客户令牌的吊销语义由 CustomerService 的会话键承载：登出 / 刷新会删旧键。
        // 验签只证明「是我们签的、还没过期」，证明不了「这个会话已经失效」。
        if (!await CheckCustomerSessionAsync(context, outcome, path))
        {
            return;
        }

        // ---- 第 3 步：RBAC ----
        var lookup = await _permissions.ResolveAsync(path);
        var requiredCode = lookup.RequiredCode;
        if (!lookup.Available)
        {
            // 权限中心不可用
            if (!_options.Rbac.AllowAllWhenUnavailable)
            {
                await RejectAsync(context, StatusCodes.Status503ServiceUnavailable,
                    new { error = "authorization_unavailable", error_description = "Permission service is unavailable." });
                return;
            }

            _logger.LogError("权限中心不可用且配置为放行，{Path} 被放行——这是不安全的降级", path);
        }
        else if (requiredCode is not null
                 && outcome.Kind == DualTokenValidator.KindCustomer
                 && IsCustomerAllowed(path))
        {
            // 客户令牌复用后台能力（当前只有统一文件上传）：仍然要求令牌有效，
            // 但客户没有后台权限点，不能按 requiredCode 去查。
        }
        else if (requiredCode is not null && !HasPermission(context.User, requiredCode))
        {
            _logger.LogWarning("权限不足：{Path} 需要 {Code}", path, requiredCode);
            await RejectAsync(context, StatusCodes.Status403Forbidden,
                new { error = "insufficient_permissions", error_description = $"Requires '{requiredCode}'." });
            return;
        }
        else if (requiredCode is null
                 && outcome.Kind == DualTokenValidator.KindCustomer
                 && !IsCustomerPath(path))
        {
            // 🔴 客户令牌走到未映射路径，且**不在 C 端显式清单里** → 拒绝（fail-closed）。
            //
            // 上面只堵住了后台令牌。客户令牌这条支路原来是「未映射 = C 端接口 = 放行」，
            // 于是任何新加的后台接口只要漏配 api_path（而 api_path 漏配是静态的、
            // 没有日志、超管测试完全测不出来），任何顾客都能直接调它 ——
            // /gateway/payments/Simulate 就是这么被顾客拿去白拿商品的。
            //
            // 现在 C 端接口要在 GatewayOptions.CustomerPaths 显式登记：
            // 漏登记的症状是 403（马上会被回归测试和联调发现），
            // 而不是「接口对全世界敞开」这种静默的资损。
            _logger.LogError(
                "拒绝客户令牌访问未登记的路径 {Path}：它不在 C 端接口清单里。"
                + "如果是新增的 C 端接口，请登记到 GatewayOptions.CustomerPaths；"
                + "如果是后台接口，请补权限种子的 api_path", path);

            await RejectAsync(context, StatusCodes.Status403Forbidden,
                new
                {
                    error = "route_not_mapped",
                    error_description = "This route is not registered for customer access.",
                });
            return;
        }
        else if (requiredCode is null && outcome.Kind != DualTokenValidator.KindCustomer)
        {
            // 🔴 没有权限映射的后台令牌一律拒绝（fail-closed）。
            //
            // RBAC 的判定是「查不到映射 → requiredCode 为 null → 放行」，
            // 所以**漏配一条 api_path 就等于那个接口完全不鉴权**。
            // 这个缺陷是静默的：没有日志、没有报错，接口照常工作，
            // 只是任何人都能调 —— 曾经 /gateway/payments/Simulate 就是这样：
            // 顾客拿自己的客户令牌就能把自己的订单标成已支付，白拿商品。
            //
            // 这里按「未映射 = 只可能是 C 端接口」处理：C 端接口本来就刻意不绑
            // 后台权限点（绑了会把小程序自己挡掉），所以客户令牌照常放行；
            // 而后台令牌走到未映射路径，说明**权限种子漏了这条**，
            // 必须响亮地拒绝并留下日志，让人去补种子，而不是默默放行。
            _logger.LogError(
                "拒绝后台令牌访问未映射路径 {Path}：权限种子里缺少这条 api_path，"
                + "补上之前该接口对所有后台账号都是敞开的", path);

            await RejectAsync(context, StatusCodes.Status403Forbidden,
                new
                {
                    error = "route_not_mapped",
                    error_description = "This admin route has no permission mapping and is denied by default.",
                });
            return;
        }

        // ---- 第 4 步：注入可信租户头 ----
        InjectClaims(context, context.User);

        await _next(context);
    }

    /// <summary>判断路径是否命中匿名白名单。</summary>
    private bool IsAnonymous(string path)
    {
        if (_options.AnonymousPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return true;
        return _options.AnonymousPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>判断客户令牌是否被显式允许访问该路径。</summary>
    /// <param name="path">网关路径。</param>
    /// <returns>命中客户复用白名单返回 true。</returns>
    private bool IsCustomerAllowed(string path)
        => _options.CustomerAllowedPaths.Any(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>判断路径是否在 C 端接口显式清单里（精确 + 前缀）。</summary>
    /// <param name="path">网关路径。</param>
    /// <returns>命中返回 true。</returns>
    private bool IsCustomerPath(string path)
    {
        if (_options.CustomerPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return _options.CustomerPathPrefixes.Any(p =>
            path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>校验客户令牌对应的服务端会话，失败时直接写出 401/503。</summary>
    /// <param name="context">当前请求。</param>
    /// <param name="outcome">验签结果（只有客户令牌会真正查会话）。</param>
    /// <param name="path">请求路径，仅用于日志。</param>
    /// <returns>可以继续管道返回 true；已拒绝并写出响应返回 false。</returns>
    /// <remarks>
    /// <b>Redis 不可用时 503 而不是 401</b>：存储故障与「会话被登出」是两回事，
    /// 都回 401 会让客户端误以为令牌失效而清掉本地会话，故障恢复后还得重新登录。
    /// </remarks>
    private async Task<bool> CheckCustomerSessionAsync(
        HttpContext context, TokenValidationOutcome outcome, string path)
    {
        if (outcome.Kind != DualTokenValidator.KindCustomer)
        {
            return true;
        }

        var principal = outcome.Principal!;
        var customerId = long.TryParse(First(principal, "sub"), out var id) ? id : 0;
        var sessionId = First(principal, "jti");

        var check = await _sessions.CheckAsync(
            customerId, sessionId, TimeSpan.FromHours(_options.CustomerToken.SessionHours));

        if (check.StoreUnavailable)
        {
            _logger.LogError("客户会话存储不可用，拒绝 {Path}（fail-closed）", path);
            await RejectAsync(context, StatusCodes.Status503ServiceUnavailable,
                new { error = "authorization_unavailable", error_description = "The session store is unavailable." });
            return false;
        }

        if (!check.Valid)
        {
            _logger.LogWarning("客户 {CustomerId} 的会话已失效，拒绝 {Path}", customerId, path);
            await RejectAsync(context, StatusCodes.Status401Unauthorized,
                new { error = "invalid_token", error_description = "The session has expired or been signed out." });
            return false;
        }

        return true;
    }

    /// <summary>判断主体是否持有指定权限点。</summary>
    /// <remarks>
    /// 只认令牌里的 permission 声明，且<b>只对后台令牌</b>有意义：
    /// 客户令牌没有权限点（fail-closed，无绑定即无权限，BUSINESS 5.3）。
    /// </remarks>
    private static bool HasPermission(ClaimsPrincipal principal, string code)
        => principal.FindAll("permission").Any(c => string.Equals(c.Value, code, StringComparison.Ordinal));

    /// <summary>把令牌里的声明写成下游可信头。</summary>
    private void InjectClaims(HttpContext context, ClaimsPrincipal principal)
    {
        var headers = context.Request.Headers;

        headers[TenancyOptions.Headers.InternalToken] = _options.InternalToken;

        headers[TenancyOptions.Headers.UserId] = First(principal, "sub");
        headers[TenancyOptions.Headers.UserName] = First(principal, "user_name");
        headers[TenancyOptions.Headers.TenantType] = ResolveTenantType(principal);
        headers[TenancyOptions.Headers.PlatformId] = First(principal, "platform_id");
        headers[TenancyOptions.Headers.MerchantId] = First(principal, "merchant_id");
        headers[TenancyOptions.Headers.CustomerNo] = First(principal, "customer_no");
        // jti 就是会话 Id：登出 / 刷新要按它定位当前设备那一把会话键
        headers[TenancyOptions.Headers.SessionId] = First(principal, "jti");

        var permissions = principal.FindAll("permission").Select(c => c.Value)
            .Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).ToArray();

        if (permissions.Length > 0)
        {
            headers[TenancyOptions.Headers.Permissions] = string.Join(',', permissions);
        }
    }

    /// <summary>解析租户类型：后台令牌用 tenant_type（1/2），客户令牌恒为 3。</summary>
    private static string ResolveTenantType(ClaimsPrincipal principal)
    {
        var raw = First(principal, "tenant_type");
        return raw is "1" or "2" ? raw : "3";
    }

    /// <summary>取令牌里第一个指定名称的声明值；没有返回空串。</summary>
    /// <param name="principal">验签后的主体。</param>
    /// <param name="type">声明名。</param>
    /// <returns>声明值或空串。</returns>
    private static string First(ClaimsPrincipal principal, string type)
        => principal.FindFirst(type)?.Value ?? string.Empty;

    /// <summary>以 JSON 返回 OAuth2 风格错误。</summary>
    private static async Task RejectAsync(HttpContext context, int status, object payload)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(payload);
    }
}
