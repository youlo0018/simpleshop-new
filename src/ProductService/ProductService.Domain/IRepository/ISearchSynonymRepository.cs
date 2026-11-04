using ProductService.Domain.Entities;

namespace ProductService.Domain.IRepository;

/// <summary>商品搜索同义词仓储。</summary>
public interface ISearchSynonymRepository
{
    /// <summary>按优先级读取全部同义词规则。</summary>
    Task<List<SearchSynonym>> ListAsync(CancellationToken ct = default);

    /// <summary>按 Id 读取规则。</summary>
    Task<SearchSynonym?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>检查同一作用域、类型下是否已有相同词条。</summary>
    Task<bool> ExistsAsync(
        int ruleType, int scopeType, long scopeId, string terms,
        long excludeId = 0, CancellationToken ct = default);

    /// <summary>新增规则。</summary>
    Task<long> InsertAsync(SearchSynonym rule, CancellationToken ct = default);

    /// <summary>更新规则。</summary>
    Task<bool> UpdateAsync(SearchSynonym rule, CancellationToken ct = default);

    /// <summary>删除规则。</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>修改启停状态。</summary>
    Task<bool> SetStatusAsync(long id, int status, CancellationToken ct = default);

    /// <summary>兼容旧版配置页：原子替换为一批全局等价词。</summary>
    Task ReplaceAllAsync(IReadOnlyList<string> terms, CancellationToken ct = default);
}
