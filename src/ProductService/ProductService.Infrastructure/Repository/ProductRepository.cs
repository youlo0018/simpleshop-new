using Collaboration.Domain.Infrastructure;
using Collaboration.Domain.Repository;
using FreeSql;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

namespace ProductService.Infrastructure.Repository;

/// <summary>
/// 商品仓储实现。
/// </summary>
/// <remarks>
/// 商品写入一律走<b>事务</b>：SPU、规格、规格值、SKU、SKU↔规格值关联是一组，
/// 中途失败留下半套数据比整体失败更难收拾——比如规格建好了 SKU 没建，
/// 前端的规格选择器会渲染出一个点不出商品的组合。
/// 商品本身的 Insert / Update 继承 CrudRepository（雪花 Id 与审计时间戳由基类填）。
/// </remarks>
public sealed class ProductRepository : CrudRepository<Product>, IProductRepository
{
    private readonly IFreeSql _db;

    /// <summary>构造仓储。</summary>
    /// <param name="freeSql">已注册全局过滤的 FreeSql 单例。</param>
    public ProductRepository(IFreeSql freeSql) : base(freeSql)
    {
        _db = freeSql;
    }

    /// <inheritdoc />
    public async Task<(List<Product> Items, long Total)> QueryPagedAsync(
        int page, int pageSize, ProductQuery query, CancellationToken ct = default)
    {
        var select = _db.Select<Product>();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            select = select.Where(a => a.SpuName.Contains(kw));
        }

        // 平台维度：C 端必须按当前平台过滤；后台 / 内部传 0 表示不限
        // （后台的可见范围由租户 AOP 决定，这里不重复限制）
        if (query.PlatformId > 0) select = select.Where(a => a.PlatformId == query.PlatformId);
        // 多选优先于单选（单选保留给旧调用点）
        var categoryIds = query.CategoryIds.ToArray();
        if (categoryIds.Length > 0) select = select.Where(a => categoryIds.Contains(a.CategoryId));
        else if (query.CategoryId > 0) select = select.Where(a => a.CategoryId == query.CategoryId);

        var brandIds = query.BrandIds.ToArray();
        if (brandIds.Length > 0) select = select.Where(a => brandIds.Contains(a.BrandId));
        else if (query.BrandId > 0) select = select.Where(a => a.BrandId == query.BrandId);

        if (query.MerchantId > 0) select = select.Where(a => a.MerchantId == query.MerchantId);
        if (query.TargetFilterSpecified)
        {
            var productIds = query.ProductIds.Distinct().ToArray();
            select = productIds.Length == 0
                ? select.Where(a => false)
                : select.Where(a => productIds.Contains(a.Id));
        }
        // 价格区间按商品的最低 SKU 售价过滤（与排序口径一致）
        if (query.MinPrice > 0) select = select.Where(a => a.MinPrice >= query.MinPrice);
        if (query.MaxPrice > 0) select = select.Where(a => a.MinPrice <= query.MaxPrice);
        if (query.Status > 0) select = select.Where(a => a.Status == query.Status);
        if (query.AuditStatus > 0) select = select.Where(a => a.AuditStatus == query.AuditStatus);

        var total = await select.CountAsync(ct);

        select = query.Order switch
        {
            ProductSorts.SalesDesc => select
                .OrderByDescending(a => a.Sales)
                .OrderByDescending(a => a.Id),
            ProductSorts.Newest => select
                .OrderByDescending(a => a.CreatedAt)
                .OrderByDescending(a => a.Id),
            ProductSorts.PriceAsc => select
                .OrderBy(a => a.MinPrice)
                .OrderBy(a => a.Id),
            ProductSorts.PriceDesc => select
                .OrderByDescending(a => a.MinPrice)
                .OrderByDescending(a => a.Id),
            _ => select
                .OrderBy(a => a.AuditStatus)
                .OrderBy(a => a.SortOrder)
                .OrderByDescending(a => a.Sales)
                // Id 兜底排序：同值行的顺序必须稳定，否则翻页会重复或漏行
                .OrderByDescending(a => a.Id)
        };

        var items = await select
            .Page(page, pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<List<string>> SuggestNamesAsync(
        long platformId, string prefix, int limit, CancellationToken ct = default)
    {
        var text = (prefix ?? string.Empty).Trim();
        if (text.Length == 0 || platformId <= 0) return [];

        return await _db.Select<Product>()
            .Where(a => a.PlatformId == platformId
                        && a.AuditStatus == AuditStatuses.Approved
                        && a.Status == ListingStatuses.OnShelf
                        && a.SpuName.StartsWith(text))
            .OrderByDescending(a => a.Sales)
            .OrderBy(a => a.Id)
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(a => a.SpuName, ct);
    }

    /// <inheritdoc />
    public async Task<List<Product>> GetByIdsAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return new List<Product>();

        // Distinct 很重要：搜索召回里可能有重复 Id（多关键词命中同一条），
        // 不去重会让同一件商品在一页里出现两次
        var list = ids.Distinct().ToArray();

        return await _db.Select<Product>()
            .Where(a => list.Contains(a.Id))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<List<long>> ListAllVisibleIdsAsync(CancellationToken ct = default)
        => await _db.Select<Product>()
            .Where(a => a.AuditStatus == AuditStatuses.Approved
                        && a.Status == ListingStatuses.OnShelf)
            .ToListAsync(a => a.Id, ct);

    /// <inheritdoc />
    public async Task<List<long>> ListVisibleCategoryIdsAsync(
        long merchantId, long platformId, CancellationToken ct = default)
    {
        var query = _db.Select<Product>()
            .Where(a => a.MerchantId == merchantId
                        && a.AuditStatus == AuditStatuses.Approved
                        && a.Status == ListingStatuses.OnShelf);
        if (platformId > 0)
        {
            query = query.Where(a => a.PlatformId == platformId);
        }

        var ids = await query.ToListAsync(a => a.CategoryId, ct).ConfigureAwait(false);
        return ids.Distinct().ToList();
    }

    /// <inheritdoc />
    public async Task<List<Sku>> GetSkusAsync(long productId, CancellationToken ct = default)
        => await _db.Select<Sku>()
            .Where(a => a.ProductId == productId)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<List<Sku>> GetSkusByIdsAsync(
        IReadOnlyCollection<long> ids, bool onlyEnabled, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        var list = ids.Where(a => a > 0).Distinct().Take(200).ToArray();
        if (list.Length == 0) return [];

        return await _db.Select<Sku>()
            .Where(a => list.Contains(a.Id))
            .Where(a => !onlyEnabled || a.Status == 1)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<List<Product>> ListOnShelfByMerchantAsync(
        long merchantId, CancellationToken ct = default)
        => await _db.Select<Product>()
            .Where(a => a.MerchantId == merchantId
                        && a.Status == ListingStatuses.OnShelf
                        && a.IsDeleted == false)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<int> OffShelfByMerchantAsync(long merchantId, CancellationToken ct = default)
        => await _db.Update<Product>()
            // 条件更新带上「当前仍是已上架」，避免与运营的上下架操作互相覆盖
            .Where(a => a.MerchantId == merchantId
                        && a.Status == ListingStatuses.OnShelf
                        && a.IsDeleted == false)
            .Set(a => new Product
            {
                Status = ListingStatuses.OffShelf,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct);

    /// <inheritdoc />
    public async Task<bool> UpdateRatingsAsync(
        long productId, decimal score, int count, CancellationToken ct = default)
        => await _db.Update<Product>()
            .Where(a => a.Id == productId)
            .Set(a => new Product
            {
                EvaluationScore = score,
                EvaluationCount = count,
                UpdatedAt = DateTime.UtcNow
            })
            .ExecuteAffrowsAsync(ct) > 0;

    /// <inheritdoc />
    public async Task<List<SkuPriceRow>> GetSkuPriceRowsAsync(
        IReadOnlyCollection<long> productIds, CancellationToken ct = default)
    {
        if (productIds.Count == 0) return new List<SkuPriceRow>();

        var ids = productIds.Distinct().ToArray();

        // 只取**启用中**的 SKU：停用 SKU 不可下单，把它算进到手价会让商品卡显示一个买不到的价格。
        var rows = await _db.Select<Sku>()
            .Where(a => ids.Contains(a.ProductId) && a.Status == SkuStatuses.Enabled)
            .ToListAsync(ct);

        return rows.Select(a => new SkuPriceRow(a.Id, a.ProductId, a.Price)).ToList();
    }

    /// <inheritdoc />
    public async Task<List<ProductSpec>> GetSpecsAsync(long productId, CancellationToken ct = default)
        => await _db.Select<ProductSpec>()
            .Where(a => a.ProductId == productId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<List<ProductSpecValue>> GetSpecValuesAsync(long productId, CancellationToken ct = default)
        => await _db.Select<ProductSpecValue>()
            .Where(a => a.ProductId == productId)
            .OrderBy(a => a.SpecId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<List<SkuSpecValue>> GetSkuSpecLinksAsync(long productId, CancellationToken ct = default)
    {
        var skuIds = (await GetSkusAsync(productId, ct)).Select(a => a.Id).ToArray();
        if (skuIds.Length == 0) return new List<SkuSpecValue>();

        return await _db.Select<SkuSpecValue>()
            .Where(a => skuIds.Contains(a.SkuId))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Sku?> GetSkuByCodeAsync(string skuCode, CancellationToken ct = default)
        => await _db.Select<Sku>().Where(a => a.SkuCode == skuCode).FirstAsync(ct);

    /// <inheritdoc />
    public async Task<bool> ExistsSkuCodeAsync(string skuCode, long excludeId = 0, CancellationToken ct = default)
        => await _db.Select<Sku>()
            .Where(a => a.SkuCode == skuCode && a.Id != excludeId)
            .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<long> CountProductsAsync(long categoryId, CancellationToken ct = default)
        => await _db.Select<Product>().Where(a => a.CategoryId == categoryId).CountAsync(ct);

    /// <inheritdoc />
    public Task<int> DeleteProductAsync(long id, CancellationToken ct = default)
        => Db.Delete<Product>().Where(a => a.Id == id).ExecuteAffrowsAsync(ct);

    /// <inheritdoc />
    public Task<int> InsertSpecsAsync(
        IReadOnlyCollection<ProductSpec> specs,
        IReadOnlyCollection<ProductSpecValue> values,
        CancellationToken ct = default)
    {
        if (specs.Count == 0 && values.Count == 0) return Task.FromResult(0);

        // 规格项要先有 Id，规格值才知道挂到哪个 SpecId 下，所以必须分两步且在同一事务里。
        return Task.Run(() =>
        {
            // FreeSql 的 Db.Transaction 收的是 void 委托，计数只能在外面接
            var affected = 0;
            _db.Transaction(() =>
            {
                if (specs.Count > 0) affected += _db.Insert(specs.ToList()).ExecuteAffrows();
                if (values.Count > 0) affected += _db.Insert(values.ToList()).ExecuteAffrows();
            });

            return affected;
        }, ct);
    }

    /// <inheritdoc />
    public Task<int> SoftDeleteSpecsAsync(long productId, CancellationToken ct = default)
    {
        // FreeSql 的 Db.Transaction 收 void 委托，计数在外面接
        var result = 0;
        _db.Transaction(() =>
        {
            var now = DateTime.UtcNow;

            result += _db.Update<ProductSpecValue>()
                .Where(a => a.ProductId == productId && !a.IsDeleted)
                .Set(a => new ProductSpecValue { IsDeleted = true, DeletedAt = now })
                .ExecuteAffrows();

            result += _db.Update<ProductSpec>()
                .Where(a => a.ProductId == productId && !a.IsDeleted)
                .Set(a => new ProductSpec { IsDeleted = true, DeletedAt = now })
                .ExecuteAffrows();
        });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<int> UpsertSkusAsync(
        IReadOnlyCollection<Sku> skus,
        IReadOnlyDictionary<string, IReadOnlyList<SkuSpecLink>> linksBySkuCode,
        CancellationToken ct = default)
    {
        if (skus.Count == 0) return Task.FromResult(0);

        var result = 0;
        _db.Transaction(() =>
        {
            foreach (var sku in skus)
            {
                var affected = _db.Update<Sku>()
                    .Where(a => a.Id == sku.Id)
                    .Set(a => new Sku
                    {
                        SkuCode = sku.SkuCode,
                        SkuName = sku.SkuName,
                        SkuSpecText = sku.SkuSpecText,
                        Price = sku.Price,
                        OriginalPrice = sku.OriginalPrice,
                        Image = sku.Image,
                        Status = sku.Status,
                        UpdatedAt = DateTime.UtcNow
                    })
                    .ExecuteAffrows();

                // affected == 0 说明这一行还不存在（新建），补雪花 Id 后插入。
                // 走 Set + Where 而不是 Update<T>(entity)：后者在本项目已确认会生成空 SET。
                if (affected == 0)
                {
                    sku.Id = SnowflakeId.NewId();
                    sku.CreatedAt = DateTime.UtcNow;
                    _db.Insert(sku).ExecuteAffrows();
                }

                result++;
            }

            // 链接必须等所有 SKU 都拿到真实 Id 之后再拼，
            // 否则新建商品的链接全是 (0, 规格值)，多个 SKU 会撞同一个复合主键。
            var links = new List<SkuSpecValue>();
            foreach (var (skuCode, templates) in linksBySkuCode)
            {
                var target = skus.FirstOrDefault(a => string.Equals(a.SkuCode, skuCode, StringComparison.Ordinal));
                if (target is null || target.Id <= 0) continue;

                foreach (var template in templates)
                {
                    links.Add(new SkuSpecValue
                    {
                        SkuId = target.Id,
                        SpecValueId = template.SpecValueId,
                        SpecId = template.SpecId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            if (links.Count > 0)
            {
                var skuIds = skus.Select(a => a.Id).ToArray();

                // 关联表没有软删列，重建前先物理删掉本商品下已有的关联
                _db.Delete<SkuSpecValue>()
                    .Where(a => skuIds.Contains(a.SkuId))
                    .ExecuteAffrows();

                _db.Insert(links).ExecuteAffrows();
            }
        });

        return Task.FromResult(result);
    }
    /// <inheritdoc />
    public Task<int> SoftDeleteSkusNotInAsync(
        long productId,
        IReadOnlyCollection<string> keepCodes,
        CancellationToken ct = default)
    {
        var result = 0;
        _db.Transaction(() =>
        {
            var now = DateTime.UtcNow;
            var existing = _db.Select<Sku>().Where(a => a.ProductId == productId).ToList();
            var stale = existing.Where(a => !keepCodes.Contains(a.SkuCode, StringComparer.Ordinal)).ToList();

            if (stale.Count == 0) return;

            var ids = stale.Select(a => a.Id).ToArray();
            _db.Delete<SkuSpecValue>().Where(a => ids.Contains(a.SkuId)).ExecuteAffrows();

            result = _db.Update<Sku>()
                .Where(a => ids.Contains(a.Id))
                .Set(a => new Sku { IsDeleted = true, DeletedAt = now, UpdatedAt = now })
                .ExecuteAffrows();
        });

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<int> RefreshPriceRangeAsync(long productId, CancellationToken ct = default)
    {
        var result = 0;
        _db.Transaction(() =>
        {
            var prices = _db.Select<Sku>()
                .Where(a => a.ProductId == productId && a.Status == 1 && !a.IsDeleted)
                .ToList()
                .Select(a => a.Price)
                .ToList();

            // 一个启用 SKU 都没有时价格区间归 0，而不是留上一次的值——
            // 留着旧价格会让前台显示一个已经买不到的价格。
            var min = prices.Count == 0 ? 0m : prices.Min();
            var max = prices.Count == 0 ? 0m : prices.Max();

            result = _db.Update<Product>()
                .Where(a => a.Id == productId)
                .Set(a => new Product { MinPrice = min, MaxPrice = max, UpdatedAt = DateTime.UtcNow })
                .ExecuteAffrows();
        });

        return Task.FromResult(result);
    }
}
