using System.Text;
using Collaboration.Domain.Infrastructure;
using Collaboration.Domain.Repository;
using FreeSql;
using MerchantPlatformService.Domain.Entities;
using MerchantPlatformService.Domain.Exceptions;
using MerchantPlatformService.Domain.IRepository;

namespace MerchantPlatformService.Infrastructure.Repository;

/// <summary>商户仓储实现。</summary>
public sealed class MerchantRepository : CrudRepository<Merchant>, IMerchantRepository
{
    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public MerchantRepository(IFreeSql freeSql) : base(freeSql) { }

    /// <inheritdoc />
    public new async Task<long> InsertAsync(Merchant merchant, CancellationToken ct = default)
    {
        try
        {
            return await base.InsertAsync(merchant, ct);
        }
        catch (Exception ex) when (PostgresErrors.IsUniqueViolationOn(ex, "uk_merchant_platform_name"))
        {
            throw new MerchantNameTakenException();
        }
    }

    /// <inheritdoc />
    public new async Task<Merchant?> GetByIdAsync(long merchantId, CancellationToken ct = default)
        => await Db.Select<Merchant>().Where(a => a.Id == merchantId).FirstAsync(ct);

    /// <inheritdoc />
    public new async Task<bool> UpdateAsync(Merchant merchant, CancellationToken ct = default)
        => await base.UpdateAsync(merchant, ct) > 0;

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(long merchantId, CancellationToken ct = default)
    {
        var affected = await Db.Update<Merchant>()
            .Where(a => a.Id == merchantId)
            .Set(a => new Merchant
            {
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<long> CountByPlatformAsync(long platformId, CancellationToken ct = default)
        => await Db.Select<Merchant>()
            .Where(a => a.PlatformId == platformId)
            .CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, long>> CountGroupByPlatformAsync(
        IReadOnlyCollection<long> platformIds, CancellationToken ct = default)
    {
        var result = new Dictionary<long, long>();
        if (platformIds.Count == 0) return result;

        var rows = await Db.Select<Merchant>()
            .Where(a => platformIds.Contains(a.PlatformId))
            .ToListAsync(a => a.PlatformId, ct);

        foreach (var group in rows.GroupBy(a => a))
        {
            result[group.Key] = group.Count();
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<Merchant> ListEnabledByPlatform(long platformId, CancellationToken ct = default)
    {
        var query = Db.Select<Merchant>()
            .Where(a => a.Status == PlatformStatuses.Enabled);

        // platformId = 0 表示「全部平台」，不是「平台 Id 等于 0」。
        // 之前写成无条件 a.PlatformId == platformId，超管查商户下拉永远为空，
        // 商品列表的商户筛选因此没有任何选项。
        if (platformId > 0)
        {
            query = query.Where(a => a.PlatformId == platformId);
        }

        return query.OrderBy(a => a.Id).ToList();
    }

    /// <inheritdoc />
    public async Task<List<(long Id, string Name)>> ListNamesByIdsAsync(
        IReadOnlyList<long> merchantIds, CancellationToken ct = default)
    {
        if (merchantIds.Count == 0) return [];

        // 内部上下文（Access = Internal）不过租户过滤，这里显式去重 + 限流（一次最多 200 个）
        var ids = merchantIds.Where(a => a > 0).Distinct().Take(200).ToArray();
        if (ids.Length == 0) return [];

        var rows = await Db.Select<Merchant>()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(a => new NameRow(a.Id, a.MerchantName), ct)
            .ConfigureAwait(false);

        return rows.Select(a => (a.Id, a.Name)).ToList();
    }

    /// <summary>名称投影行（FreeSql 投影需要具名类型，元组映射不可靠）。</summary>
    /// <param name="Id">Id。</param>
    /// <param name="Name">展示名。</param>
    private sealed record NameRow(long Id, string Name);

    /// <inheritdoc />
    public async Task<PagedMerchants> PageAsync(MerchantFilter filter, CancellationToken ct = default)
    {
        var query = Db.Select<Merchant>();

        if (filter.PlatformId > 0) query = query.Where(a => a.PlatformId == filter.PlatformId);
        if (filter.AuditStatus > 0) query = query.Where(a => a.AuditStatus == filter.AuditStatus);
        if (filter.Status > 0) query = query.Where(a => a.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var text = filter.Keyword.Trim();
            query = query.Where(a => a.MerchantName.Contains(text) || a.MerchantNo.Contains(text));
        }

        var total = await query.CountAsync(ct);
        if (total == 0) return new PagedMerchants([], 0, filter.Page, filter.PageSize);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .OrderByDescending(a => a.Id)
            .Page(filter.Page, filter.PageSize)
            .ToListAsync(ct);

        return new PagedMerchants(items, total, filter.Page, filter.PageSize);
    }

    /// <inheritdoc />
    public async Task<int> TryUpdateAuditAsync(long merchantId, int expectedAuditStatus,
        int newAuditStatus, string auditRemark, long auditorId, string auditorName,
        CancellationToken ct = default, int? newStatus = null)
    {
        var update = Db.Update<Merchant>()
            // 带上「当前审核状态」作为条件：两个管理员同时点审核时，
            // 只有一个能把状态从 10 改成 20，另一个拿到 0 就该直接返回，
            // 而不是把审核人 / 审核时间覆盖掉
            .Where(a => a.Id == merchantId && a.AuditStatus == expectedAuditStatus)
            .Set(a => new Merchant
            {
                AuditStatus = newAuditStatus,
                AuditRemark = auditRemark,
                AuditedAt = DateTime.UtcNow,
                AuditorId = auditorId,
                AuditorName = auditorName,
                UpdatedAt = DateTime.UtcNow
            });

        // 审核通过时顺带启用商户。FreeSql 会把多次 Set **合并进同一条 UPDATE**，
        // 正好满足「状态与启停必须原子写入」的要求（详见接口注释）。
        //
        // ⚠️ 第二个 Set 里**绝不能再带 UpdatedAt**：第一次 Set 已经写过这一列，
        // 再写一次 PostgreSQL 直接报 42601「multiple assignments to same column」，
        // 表现为审核接口 500 —— 而错误信息完全指不到「你多写了一列」。
        if (newStatus.HasValue)
        {
            var status = newStatus.Value;
            update = update.Set(a => new Merchant { Status = status });
        }

        return await update.ExecuteAffrowsAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> TryUpdateStatusAsync(long merchantId, int expectedStatus, int newStatus,
        CancellationToken ct = default)
        => await Db.Update<Merchant>()
            // 带上「当前启停状态」作为条件：两个管理员同时点「停用」时只有一个生效，
            // 另一个拿到 0 后回「状态已变化」，而不是覆盖回去
            .Where(a => a.Id == merchantId && a.Status == expectedStatus)
            .Set(a => new Merchant
            {
                Status = newStatus,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

    /// <inheritdoc />
    public async Task<int> SyncRatingsAsync(IReadOnlyDictionary<long, decimal> ratings,
        CancellationToken ct = default)
    {
        if (ratings.Count == 0) return 0;

        // 🔴 单条 UPDATE ... FROM (VALUES ...) 批量更新，不再逐个商户发 UPDATE。
        //
        // 评分同步一次要覆盖全平台商户，逐个 UPDATE 意味着 N 次往返；
        // 商户多起来后这条链路会从「毫秒级」退化成「秒级」，而调用方是内部同步任务，
        // 超时后还会重试，重试又跑一遍全部 UPDATE。
        //
        // 所有行共用同一个 updated_at：同一批同步本来就是一个时刻的结果，
        // 逐行取 now() 会让同批数据的更新时间不一致，排查时看不出它们是同一次同步。
        var updatedAt = DateTime.UtcNow;
        var sql = new StringBuilder("UPDATE merchant AS m SET rating = v.rating, updated_at = v.updated_at ")
            .Append("FROM (VALUES ");
        var parameters = new Dictionary<string, object>(ratings.Count * 2 + 1)
        {
            // 全批共用一个时间戳：同一批同步本来就是一个时刻的结果
            ["updated_at"] = updatedAt
        };

        var index = 0;
        foreach (var (merchantId, rating) in ratings)
        {
            if (index > 0) sql.Append(", ");

            // 参数名带行号，占位符与 Dictionary 的 key 一一对应（不含 @）
            // 🔴 参数名必须全小写：FreeSql 解析占位符时会做大小写规范化，
            // 写成 @updatedAt 会被当成「@updated」后面跟着列名 At，报 column "updatedat" does not exist。
            sql.Append($"(@id{index}, @rating{index}, @updated_at)");
            parameters[$"id{index}"] = merchantId;
            parameters[$"rating{index}"] = rating;
            index++;
        }

        // 软删条件必须显式补上：原生 SQL 不经过 FreeSql 的全局过滤器，
        // 漏掉会把已删除商户的评分也一起改掉（与逐行版本的行为不一致）。
        sql.Append(") AS v(id, rating, updated_at) WHERE m.id = v.id AND m.is_deleted = false");

        return await Db.Ado
            .ExecuteNonQueryAsync(sql.ToString(), parameters, ct)
            .ConfigureAwait(false);
    }
}
