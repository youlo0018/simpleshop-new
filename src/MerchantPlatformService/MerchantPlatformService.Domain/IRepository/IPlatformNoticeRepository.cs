using MerchantPlatformService.Domain.Entities;

namespace MerchantPlatformService.Domain.IRepository;

/// <summary>平台公告仓储。</summary>
public interface IPlatformNoticeRepository
{
    /// <summary>后台分页查公告（平台账号由 AOP 自动按平台过滤）。</summary>
    /// <param name="status">状态，0 表示不限。</param>
    /// <param name="page">页码，从 1 起。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>当页公告与总条数。只读、幂等。</returns>
    Task<(List<PlatformNotice> Items, long Total)> PageAsync(
        int status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>按 Id 取公告。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>公告或 null。只读、幂等。</returns>
    Task<PlatformNotice?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>插入公告。</summary>
    /// <param name="notice">公告实体。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新公告 Id。非幂等。</returns>
    Task<long> InsertAsync(PlatformNotice notice, CancellationToken ct = default);

    /// <summary>更新公告。</summary>
    /// <param name="notice">公告实体。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>受影响行数。幂等。</returns>
    Task<int> UpdateAsync(PlatformNotice notice, CancellationToken ct = default);

    /// <summary>软删公告。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否删除成功。幂等。</returns>
    Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default);

    /// <summary>启停公告。</summary>
    /// <param name="id">公告 Id。</param>
    /// <param name="status">目标状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否更新成功。幂等。</returns>
    Task<bool> SetStatusAsync(long id, int status, CancellationToken ct = default);

    /// <summary>取某平台启用中的公告（小程序用，按排序值升序）。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="limit">最多返回条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>公告列表。只读、幂等。</returns>
    Task<List<PlatformNotice>> ListEnabledAsync(
        long platformId, int limit, CancellationToken ct = default);
}
