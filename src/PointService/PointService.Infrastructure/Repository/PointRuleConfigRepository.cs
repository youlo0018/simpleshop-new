using Collaboration.Domain.Infrastructure;
using FreeSql;
using PointService.Domain.Entities;
using PointService.Domain.IRepository;

namespace PointService.Infrastructure.Repository;

/// <summary>积分规则配置仓储实现。</summary>
public sealed class PointRuleConfigRepository : IPointRuleConfigRepository
{
    private readonly IFreeSql _db;

    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public PointRuleConfigRepository(IFreeSql freeSql) => _db = freeSql;

    /// <inheritdoc />
    public async Task<bool> HasCustomizedAsync(CancellationToken ct = default)
        => await _db.Select<PointRuleConfig>()
            .Where(a => a.RuleValue != null && a.RuleValue != string.Empty)
            .CountAsync(ct)
            .ConfigureAwait(false) > 0;

    /// <inheritdoc />
    public async Task<PointRuleConfig?> GetByKeyAsync(string ruleKey, CancellationToken ct = default)
        => await _db.Select<PointRuleConfig>()
            .Where(a => a.RuleKey == ruleKey)
            .FirstAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task UpsertAsync(
        string ruleKey, string ruleValue, string description,
        long operatorId, string operatorName, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var existing = await GetByKeyAsync(ruleKey, ct).ConfigureAwait(false);

        if (existing is null)
        {
            await _db.Insert(new PointRuleConfig
            {
                Id = SnowflakeId.NewId(),
                CreatedAt = now,
                RuleKey = ruleKey,
                RuleValue = ruleValue,
                Description = description,
                UpdatedById = operatorId,
                UpdatedByName = operatorName
            }).ExecuteAffrowsAsync(ct).ConfigureAwait(false);
            return;
        }

        // 用 Where + Set 而不是 Update(entity)：
        // 后者在雪花主键（IsIdentity=false）下会生成空 SET 子句、一条 SQL 都不发
        // 就返回 0，接口回「成功」而数据纹丝不动（见 CrudRepository 的 P0 修复说明）。
        await _db.Update<PointRuleConfig>()
            .Where(a => a.Id == existing.Id)
            .Set(a => new PointRuleConfig
            {
                RuleValue = ruleValue,
                Description = description,
                UpdatedAt = now,
                UpdatedById = operatorId,
                UpdatedByName = operatorName
            })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
    }
}
