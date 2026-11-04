using FreeSql;
using Collaboration.Domain.Infrastructure;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

namespace ProductService.Infrastructure.Repository;

/// <summary>商品搜索同义词仓储实现。</summary>
public sealed class SearchSynonymRepository : ISearchSynonymRepository
{
    private readonly IFreeSql _db;

    /// <summary>构造仓储。</summary>
    /// <param name="db">FreeSql 实例。</param>
    public SearchSynonymRepository(IFreeSql db) => _db = db;

    /// <inheritdoc />
    public Task<List<SearchSynonym>> ListAsync(CancellationToken ct = default)
        => _db.Select<SearchSynonym>()
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.Priority)
            .OrderBy(a => a.ScopeType)
            .OrderBy(a => a.ScopeId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<SearchSynonym?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Select<SearchSynonym>()
            .Where(a => !a.IsDeleted && a.Id == id)
            .FirstAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<bool> ExistsAsync(
        int ruleType, int scopeType, long scopeId, string terms,
        long excludeId = 0, CancellationToken ct = default)
        => _db.Select<SearchSynonym>()
            .Where(a => !a.IsDeleted
                        && a.RuleType == ruleType
                        && a.ScopeType == scopeType
                        && a.ScopeId == scopeId
                        && a.Terms == terms
                        && (excludeId <= 0 || a.Id != excludeId))
            .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<long> InsertAsync(SearchSynonym rule, CancellationToken ct = default)
    {
        rule.Id = SnowflakeId.NewId();
        rule.CreatedAt = DateTime.UtcNow;
        await _db.Insert(rule).ExecuteAffrowsAsync(ct).ConfigureAwait(false);
        return rule.Id;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SearchSynonym rule, CancellationToken ct = default)
    {
        var rows = await _db.Update<SearchSynonym>()
            .Where(a => !a.IsDeleted && a.Id == rule.Id)
            .Set(a => a.Terms, rule.Terms)
            .Set(a => a.RuleType, rule.RuleType)
            .Set(a => a.ScopeType, rule.ScopeType)
            .Set(a => a.ScopeId, rule.ScopeId)
            .Set(a => a.Priority, rule.Priority)
            .Set(a => a.Status, rule.Status)
            .Set(a => a.Remark, rule.Remark)
            .Set(a => a.UpdatedAt, DateTime.UtcNow)
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var rows = await _db.Delete<SearchSynonym>()
            .Where(a => !a.IsDeleted && a.Id == id)
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetStatusAsync(long id, int status, CancellationToken ct = default)
    {
        var rows = await _db.Update<SearchSynonym>()
            .Where(a => !a.IsDeleted && a.Id == id)
            .Set(a => a.Status, status)
            .Set(a => a.UpdatedAt, DateTime.UtcNow)
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task ReplaceAllAsync(IReadOnlyList<string> terms, CancellationToken ct = default)
    {
        // FreeSql 3.5 没有 TransactionAsync，沿用项目统一的同步事务 + Task.Run 写法。
        await Task.Run(() => _db.Transaction(() =>
        {
            _db.Delete<SearchSynonym>()
                .Where(a => true)
                .ExecuteAffrows();

            for (var i = 0; i < terms.Count; i++)
            {
                _db.Insert(new SearchSynonym
                {
                    Id = SnowflakeId.NewId(),
                    CreatedAt = DateTime.UtcNow,
                    Terms = terms[i],
                    RuleType = SearchSynonymRuleTypes.Equivalent,
                    ScopeType = SearchSynonymScopeTypes.Global,
                    ScopeId = 0,
                    Priority = i,
                    Status = SearchSynonymStatuses.Enabled,
                    SortOrder = i
                }).ExecuteAffrows();
            }
        }), ct).ConfigureAwait(false);
    }
}
