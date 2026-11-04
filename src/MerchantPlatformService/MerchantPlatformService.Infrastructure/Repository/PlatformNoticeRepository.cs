using Collaboration.Domain.Repository;
using FreeSql;
using MerchantPlatformService.Domain.Entities;
using MerchantPlatformService.Domain.IRepository;

namespace MerchantPlatformService.Infrastructure.Repository;

/// <summary>平台公告仓储实现。租户过滤由全局过滤器注入。</summary>
public sealed class PlatformNoticeRepository
    : CrudRepository<PlatformNotice>, IPlatformNoticeRepository
{
    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 实例。</param>
    public PlatformNoticeRepository(IFreeSql freeSql) : base(freeSql) { }

    /// <inheritdoc />
    public async Task<(List<PlatformNotice> Items, long Total)> PageAsync(
        int status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Db.Select<PlatformNotice>()
            .Where(a => status <= 0 || a.Status == status);

        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderBy(a => a.SortOrder)
            .OrderByDescending(a => a.Id)
            .Page(safePage, safeSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(long id, CancellationToken ct = default)
        => await Db.Update<PlatformNotice>()
            .Where(a => a.Id == id)
            .Set(a => new PlatformNotice
            {
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false) > 0;

    /// <inheritdoc />
    public async Task<bool> SetStatusAsync(long id, int status, CancellationToken ct = default)
        => await Db.Update<PlatformNotice>()
            .Where(a => a.Id == id)
            .Set(a => new PlatformNotice { Status = status, UpdatedAt = DateTime.UtcNow })
            .ExecuteAffrowsAsync(ct)
            .ConfigureAwait(false) > 0;

    /// <inheritdoc />
    public async Task<List<PlatformNotice>> ListEnabledAsync(
        long platformId, int limit, CancellationToken ct = default)
        => await Db.Select<PlatformNotice>()
            .Where(a => a.PlatformId == platformId
                        && a.Status == PlatformNoticeStatuses.Enabled)
            .OrderBy(a => a.SortOrder)
            .OrderBy(a => a.Id)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
