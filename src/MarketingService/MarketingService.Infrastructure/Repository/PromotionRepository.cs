using System.Collections.Concurrent;
using Collaboration.Domain.Context;
using Collaboration.Domain.Infrastructure;
using Collaboration.Domain.Repository;
using FreeSql;
using MarketingService.Domain.Entities;
using MarketingService.Domain.IRepository;
using MarketingService.Domain.Services;

namespace MarketingService.Infrastructure.Repository;

/// <summary>营销活动仓储实现。</summary>
/// <remarks>
/// <para><b>刻意不继承 <see cref="CrudRepository{T}"/></b>，两条理由：</para>
/// <list type="number">
/// <item>本仓储的读写都要<b>显式列出要改的字段</b>。通用基类用 <c>SetDto</c> 把实体所有属性
/// 写进 SET，包括 <c>platform_id</c> / <c>merchant_id</c> 这些不该被编辑接口改的列；
/// 而 <c>Db.Update&lt;T&gt;(entity)</c> 那条路在这个项目里又已经踩过坑（空 SET、返回 0、不报错）。</item>
/// <item>继承后再定义同名 <c>InsertAsync</c> / <c>UpdateAsync</c> 会触发 CS0108
/// 「隐藏继承的成员」——编译器提醒你：这里的行为和基类不一样，必须显式写 <c>new</c>。
/// 那就变成在向读代码的人暗示「它其实是个重写」，其实是两套实现。不如直接不继承。</item>
/// </list>
/// </remarks>
public sealed class PromotionRepository : IPromotionRepository
{
    /// <summary>快照缓存条目：某平台的启用活动 + 优惠优先级。</summary>
    /// <param name="LoadedAt">载入时刻（UTC），用于算 TTL。</param>
    /// <param name="Activities">该平台启用中的活动（不含时间窗过滤）。</param>
    /// <param name="Priority">该平台的优惠优先级。</param>
    private sealed record SnapshotCacheEntry(
        DateTime LoadedAt, List<PromotionActivity> Activities, int Priority);

    /// <summary>按平台的营销快照缓存。</summary>
    /// <remarks>
    /// <para>仓储注册为 Scoped（每个请求一个实例），实例字段存不住东西，所以用静态表；
    /// 多实例部署时每个进程各有一份，最多差一个 TTL —— 对营销配置可接受。</para>
    /// <para><b>只缓存「启用中的活动行」，不缓存时间窗</b>：到点生效 / 到点失效由
    /// <see cref="ListActiveAsync"/> 在内存里按 <c>nowUtc</c> 过滤，活动不会被缓存拖后腿。</para>
    /// <para>写操作（新建 / 编辑 / 启停 / 删除 / 改优先级）清空整张缓存：
    /// 后台写操作是低频的，清整表比按平台精确失效更不容易出错。</para>
    /// </remarks>
    private static readonly ConcurrentDictionary<long, SnapshotCacheEntry> SnapshotCache = new();

    /// <summary>快照 TTL：与网关 RBAC 缓存同口径（30 秒）。</summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly IFreeSql _db;

    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public PromotionRepository(IFreeSql freeSql) => _db = freeSql;

    /// <inheritdoc />
    public async Task<List<PromotionActivity>> ListActiveAsync(
        long platformId, long sessionId, DateTime nowUtc, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(platformId, ct).ConfigureAwait(false);

        // 时间窗在内存里按 nowUtc 过滤（快照里不缓存时间窗），其余条件与原来一致
        return snapshot.Activities
            .Where(a => a.StartTime <= nowUtc
                        && a.EndTime >= nowUtc
                        && (sessionId <= 0 || a.SessionId == sessionId))
            .OrderBy(a => a.SortOrder).OrderBy(a => a.Id)
            .ToList();
    }

    /// <summary>取某平台的营销快照（命中缓存直接返回，过期则重新载入）。</summary>
    /// <param name="platformId">平台 Id，0 表示不限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>快照。</returns>
    /// <remarks>
    /// 结算页 / 商品列表的到手价试算是全系统调用频次最高的读路径，
    /// 每次都直接打库既浪费又让活动配置页的写操作与它争连接。
    /// </remarks>
    private async Task<SnapshotCacheEntry> GetSnapshotAsync(long platformId, CancellationToken ct)
    {
        if (SnapshotCache.TryGetValue(platformId, out var cached)
            && DateTime.UtcNow - cached.LoadedAt < SnapshotTtl)
        {
            return cached;
        }

        var activities = await _db.Select<PromotionActivity>()
            .Where(a => a.Status == 1 && (platformId <= 0 || a.PlatformId == platformId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var config = await _db.Select<MarketingConfig>()
            .Where(a => a.PlatformId == platformId)
            .FirstAsync(ct)
            .ConfigureAwait(false);

        var entry = new SnapshotCacheEntry(
            DateTime.UtcNow, activities, config?.Priority ?? MarketingPriorities.CouponFirst);

        SnapshotCache[platformId] = entry;
        return entry;
    }

    /// <summary>清空快照缓存（任何写操作后调用）。</summary>
    private static void InvalidateSnapshot() => SnapshotCache.Clear();

    /// <inheritdoc />
    public async Task<PromotionActivity?> GetAsync(long activityId, CancellationToken ct = default)
        => await _db.Select<PromotionActivity>().Where(a => a.Id == activityId).FirstAsync(ct);

    /// <inheritdoc />
    public async Task<(List<PromotionActivity> Items, long Total)> ListAsync(
        int activityType, string keyword, int status, int page, int pageSize, CancellationToken ct = default)
    {
        var like = (keyword ?? string.Empty).Trim();

        var items = await _db.Select<PromotionActivity>()
            .Where(a => (activityType <= 0 || a.ActivityType == activityType)
                        && (status <= 0 || a.Status == status)
                        && (string.IsNullOrEmpty(like) || a.ActivityName.Contains(like)))
            .OrderByDescending(a => a.CreatedAt).OrderByDescending(a => a.Id)
            .Limit(pageSize).Offset((page - 1) * pageSize)
            .ToListAsync(ct);

        var total = await _db.Select<PromotionActivity>()
            .Where(a => (activityType <= 0 || a.ActivityType == activityType)
                        && (status <= 0 || a.Status == status)
                        && (string.IsNullOrEmpty(like) || a.ActivityName.Contains(like)))
            .CountAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<long> InsertAsync(PromotionActivity activity, CancellationToken ct = default)
    {
        // 雪花 Id 与时间戳在这里显式填。实测 FreeSql 3.5 的 Aop.CurdBefore 在本项目调用链上
        // 没有触发，依赖它会让 Id 写成 0、CreatedAt 写成默认值 0001-01-01（CRUD 基类里也记了这个坑）。
        // 审计字段在这里显式填。实测 FreeSql 3.5 的 Aop.CurdBefore 在本项目调用链上没有触发，
        // 依赖它会让 Id 写成 0、CreatedAt 写成 0001-01-01（CRUD 基类里也记了这个坑）。
        var ctx = TenantContextHolder.Current;
        activity.Id = SnowflakeId.NewId();
        activity.CreatedAt = DateTime.UtcNow;
        activity.CreatedById = ctx.UserId;
        activity.CreatedByName = ctx.UserName;
        activity.OperationId = ctx.UserId;
        activity.OperationName = ctx.UserName;
        await _db.Insert(activity).ExecuteAffrowsAsync(ct);
        // 写操作后清快照缓存：下一次读会重新载入（文档「写操作必须 Invalidate」）
        InvalidateSnapshot();
        return activity.Id;
    }

    /// <inheritdoc />
    public async Task<int> UpdateAsync(PromotionActivity activity, CancellationToken ct = default)
    {
        // 显式 Where + Set：FreeSql 3.5 下 Db.Update<T>(entity) 在雪花主键上会生成空 SET，
        // 一条 SQL 都不发、返回 0 且不报错——接口回「成功」而数据纹丝不动。
        //
        // 操作人字段**只在这里**更新，创建人字段永远不进 SET——
        // 「这个活动是谁建的」一旦被编辑动作覆盖，就再也查不出配置错误是谁引进来的了。
        var ctx = TenantContextHolder.Current;
        var affected = await _db.Update<PromotionActivity>()
            .Where(a => a.Id == activity.Id)
            .Set(a => new PromotionActivity
            {
                ActivityName = activity.ActivityName,
                ActivityType = activity.ActivityType,
                ThresholdAmount = activity.ThresholdAmount,
                DiscountAmount = activity.DiscountAmount,
                DiscountRate = activity.DiscountRate,
                GiftTemplateId = activity.GiftTemplateId,
                SessionId = activity.SessionId,
                TargetType = activity.TargetType,
                Targets = activity.Targets,
                StartTime = activity.StartTime,
                EndTime = activity.EndTime,
                PerOrderLimit = activity.PerOrderLimit,
                TotalQuantity = activity.TotalQuantity,
                SortOrder = activity.SortOrder,
                Status = activity.Status,
                OperationId = ctx.UserId,
                OperationName = ctx.UserName,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

        // 编辑会改范围 / 时间 / 状态，快照必须失效（文档「写操作必须 Invalidate」）
        if (affected > 0) InvalidateSnapshot();
        return affected;
    }

    /// <inheritdoc />
    public async Task<int> SetStatusAsync(long activityId, int status, CancellationToken ct = default)
    {
        var ctx = TenantContextHolder.Current;
        var affected = await _db.Update<PromotionActivity>()
            .Where(a => a.Id == activityId)
            .Set(a => new PromotionActivity
            {
                Status = status,
                OperationId = ctx.UserId,
                OperationName = ctx.UserName,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

        if (affected > 0) InvalidateSnapshot();
        return affected;
    }

    /// <inheritdoc />
    public async Task<int> GetPriorityAsync(long platformId, CancellationToken ct = default)
        // 优先级也在同一份快照里：试算每次都要读它，单独查一次库不值当
        => (await GetSnapshotAsync(platformId, ct).ConfigureAwait(false)).Priority;

    /// <inheritdoc />
    public async Task<MarketingConfig?> GetConfigAsync(long platformId, CancellationToken ct = default)
        => await _db.Select<MarketingConfig>()
            .Where(a => a.PlatformId == platformId)
            .FirstAsync(ct);

    /// <inheritdoc />
    public async Task SavePriorityAsync(long platformId, int priority, CancellationToken ct = default)
    {
        var row = await _db.Select<MarketingConfig>()
            .Where(a => a.PlatformId == platformId)
            .FirstAsync(ct);

        if (row is null)
        {
            await _db.Insert(new MarketingConfig
            {
                Id = SnowflakeId.NewId(),
                CreatedAt = DateTime.UtcNow,
                PlatformId = platformId,
                Priority = priority
            }).ExecuteAffrowsAsync(ct).ConfigureAwait(false);
            InvalidateSnapshot();
            return;
        }

        // 用 Where + Set 而不是 Update<T>(entity)：后者在本项目已确认会生成空 SET 子句、
        // 一条 SQL 都不发就返回 0（见 CrudRepository 的 P0 修复说明）
        await _db.Update<MarketingConfig>()
            .Where(a => a.Id == row.Id)
            .Set(a => new MarketingConfig { Priority = priority, UpdatedAt = DateTime.UtcNow })
            .ExecuteAffrowsAsync(ct).ConfigureAwait(false);

        // 优先级是试算的输入，改完必须让快照失效
        InvalidateSnapshot();
    }

    /// <inheritdoc />
    public async Task<int> SoftDeleteAsync(long activityId, CancellationToken ct = default)
    {
        var affected = await _db.Update<PromotionActivity>()
            .Where(a => a.Id == activityId)
            .Set(a => new PromotionActivity
            {
                IsDeleted = true, DeletedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

        if (affected > 0) InvalidateSnapshot();
        return affected;
    }

    /// <inheritdoc />
    public async Task<bool> RecordParticipationAsync(
        MarketingActivityRecord record, CancellationToken ct = default)
    {
        var exists = await _db.Select<MarketingActivityRecord>()
            .Where(a => a.OrderNo == record.OrderNo && a.ActivityId == record.ActivityId)
            .AnyAsync(ct)
            .ConfigureAwait(false);
        if (exists) return false;

        record.Id = SnowflakeId.NewId();
        record.CreatedAt = DateTime.UtcNow;

        await _db.Insert(record).ExecuteAffrowsAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<ActivityParticipationResult> AggregateParticipationAsync(
        DateTime from, DateTime to, long merchantId, long platformId, int limit,
        CancellationToken ct = default)
    {
        var rows = await _db.Select<MarketingActivityRecord>()
            .Where(a => a.CreatedAt >= from && a.CreatedAt < to)
            .Where(a => merchantId <= 0 || a.MerchantId == merchantId)
            .Where(a => platformId <= 0 || a.PlatformId == platformId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 内存分组：一个区间的参与记录量级是「订单数」，与积分 / 券报表同一取舍
        // （数据量真的上来之后改 SQL 聚合或加汇总表，见 REVIEW P2）。
        var groups = rows
            .GroupBy(a => a.ActivityId)
            .Select(g => new ActivityParticipationAggregate(
                g.Key,
                // 名字取快照：同一次分组里的名字理论上一样（活动改名不改历史记录）
                g.OrderByDescending(a => a.CreatedAt).First().ActivityName,
                g.LongCount(),
                Math.Round(g.Sum(a => a.DiscountAmount), 2, MidpointRounding.AwayFromZero),
                g.Select(a => a.OrderNo).ToList()))
            .OrderByDescending(a => a.OrderCount)
            .ToList();

        // 排序必须是**确定**的：参与订单数相同时按活动 Id 倒序（新的在前）。
        // 不加这个次序，同数量的活动顺序随分组顺序漂移，
        // 分页 / 截断边界上会出现「同一条数据这次在、下次不在」。
        var ordered = groups
            .OrderByDescending(a => a.OrderCount)
            .ThenByDescending(a => a.ActivityId)
            .ToList();

        return new ActivityParticipationResult(ordered.Take(limit).ToList(), ordered.Count);
    }

    /// <inheritdoc />
    public async Task<(List<MarketingActivityRecord> Items, long Total)> PageParticipationAsync(
        long activityId, DateTime from, DateTime to, int page, int pageSize,
        CancellationToken ct = default)
    {
        var select = _db.Select<MarketingActivityRecord>()
            .Where(a => activityId <= 0 || a.ActivityId == activityId)
            .Where(a => a.CreatedAt >= from && a.CreatedAt < to);

        var total = await select.CountAsync(ct).ConfigureAwait(false);

        // 排序带 Id 兜底：同一秒内落库的两条记录，只按时间排会翻页重复或漏行
        var items = await select
            .OrderByDescending(a => a.CreatedAt)
            .OrderByDescending(a => a.Id)
            .Page(page, pageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<List<MarketingActivityRecord>> ListParticipationOrphansAsync(
        DateTime createdBefore, int limit, CancellationToken ct = default)
        => await _db.Select<MarketingActivityRecord>()
            .Where(a => !a.IsDeleted && a.CreatedAt < createdBefore)
            // 升序：先清最老的记录，避免长期积压的孤儿永远排在队尾
            .OrderBy(a => a.CreatedAt)
            .OrderBy(a => a.Id)
            .Limit(Math.Clamp(limit, 1, 1000))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int> SoftDeleteParticipationByOrderNosAsync(
        IReadOnlyCollection<string> orderNos, CancellationToken ct = default)
    {
        var list = orderNos
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (list.Length == 0) return 0;

        var now = DateTime.UtcNow;
        return await _db.Update<MarketingActivityRecord>()
            .Where(a => list.Contains(a.OrderNo) && !a.IsDeleted)
            .Set(a => new MarketingActivityRecord
            {
                IsDeleted = true,
                DeletedAt = now,
                UpdatedAt = now
            })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
    }
}
