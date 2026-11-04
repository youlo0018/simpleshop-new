using ProductService.Domain.Entities;

namespace ProductService.Domain.IRepository;

/// <summary>搜索热搜词仓储。</summary>
public interface ISearchHotKeywordRepository
{
    /// <summary>后台分页查热搜词（平台账号由 AOP 自动过滤）。</summary>
    /// <param name="status">状态，0 表示不限。</param>
    /// <param name="keyword">按关键词模糊匹配，空表示不限。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>当页数据与总数。只读、幂等。</returns>
    Task<(List<SearchHotKeyword> Items, long Total)> PageAsync(
        int status, string keyword, int page, int pageSize, CancellationToken ct = default);

    /// <summary>按 Id 取热搜词。</summary>
    /// <param name="id">热搜词 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>热搜词或 null。</returns>
    Task<SearchHotKeyword?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>插入热搜词。</summary>
    /// <param name="keyword">热搜词实体。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新 Id。</returns>
    Task<long> InsertAsync(SearchHotKeyword keyword, CancellationToken ct = default);

    /// <summary>更新热搜词。</summary>
    /// <param name="keyword">热搜词实体。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>受影响行数。</returns>
    Task<int> UpdateAsync(SearchHotKeyword keyword, CancellationToken ct = default);

    /// <summary>软删热搜词。</summary>
    /// <param name="id">热搜词 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功。</returns>
    Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default);

    /// <summary>启停热搜词。</summary>
    /// <param name="id">热搜词 Id。</param>
    /// <param name="status">目标状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否成功。</returns>
    Task<bool> SetStatusAsync(long id, int status, CancellationToken ct = default);

    /// <summary>取某平台启用中的热搜词（小程序用，按排序值升序）。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="limit">最多返回条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>热搜词列表。</returns>
    Task<List<SearchHotKeyword>> ListEnabledAsync(
        long platformId, int limit, CancellationToken ct = default);

    /// <summary>取某平台启用中、包含指定词的热搜词（联想用）。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="keyword">用户已输入的关键词。</param>
    /// <param name="limit">最多返回条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>匹配的热搜词列表。</returns>
    Task<List<SearchHotKeyword>> ListMatchingAsync(
        long platformId, string keyword, int limit, CancellationToken ct = default);
}
