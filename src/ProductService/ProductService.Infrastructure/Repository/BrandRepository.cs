using Collaboration.Domain.Repository;
using FreeSql;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

namespace ProductService.Infrastructure.Repository;

/// <summary>品牌仓储实现。写入方法继承 CrudRepository（雪花 Id 与审计时间戳由基类填）。</summary>
public sealed class BrandRepository : CrudRepository<Brand>, IBrandRepository
{
    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public BrandRepository(IFreeSql freeSql) : base(freeSql)
    {
    }

    /// <inheritdoc />
    public async Task<(List<Brand> Items, long Total)> QueryPagedAsync(
        int page, int pageSize, string keyword, bool includeDisabled,
        CancellationToken ct = default, long platformId = 0)
    {
        var select = Db.Select<Brand>();

        // 平台维度：品牌表里 platform_id = 0 表示「全平台公共」，必须一并放行
        // （只查本平台会把公共品牌全部藏掉）
        if (platformId > 0)
        {
            select = select.Where(a => a.PlatformId == 0 || a.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            select = select.Where(a => a.BrandName.Contains(kw) || a.BrandCode.Contains(kw));
        }

        if (!includeDisabled) select = select.Where(a => a.Status == 1);

        // 分页参数在这里夹取（与其它仓储同一口径）：
        // 不夹的话 pageSize=100000 就是一次全表拉取，而文档口径是 1-100。
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);

        var total = await select.CountAsync(ct);
        var items = await select.OrderBy(a => a.SortOrder).OrderByDescending(a => a.Id)
            .Page(safePage, safeSize).ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByNameAsync(string brandName, long excludeId = 0, CancellationToken ct = default)
        => await Db.Select<Brand>()
            .Where(a => a.BrandName == brandName && a.Id != excludeId)
            .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistsByCodeAsync(string brandCode, long excludeId = 0, CancellationToken ct = default)
        => !string.IsNullOrWhiteSpace(brandCode) &&
           await Db.Select<Brand>()
               .Where(a => a.BrandCode == brandCode && a.Id != excludeId)
               .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<long> CountProductsAsync(long brandId, CancellationToken ct = default)
        => await Db.Select<Product>().Where(a => a.BrandId == brandId).CountAsync(ct);
}
