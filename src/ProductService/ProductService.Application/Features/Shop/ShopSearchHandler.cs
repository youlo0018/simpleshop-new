using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using Microsoft.Extensions.Logging;
using ProductService.Application.Services;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;
using ProductEntity = ProductService.Domain.Entities.Product;

namespace ProductService.Application.Features.Shop;

/// <summary>前台商品搜索处理器（Elasticsearch 召回 + 回库取权威数据）。</summary>
/// <remarks>
/// <para><b>ES 只负责召回 Id，价格与状态一律回 PostgreSQL 取。</b>
/// 商品价格与上下架是会变的，索引里的副本必然有滞后窗口；
/// 直接读副本就会出现「搜索结果有货、点进去已下架」「列表 99、结算 129」。
/// 用户对价格的不信任就是这么来的，而且极难排查——两个库对不上，却都不知道该信哪个。</para>
///
/// <para>回库之后<b>再过滤一次</b>「审核通过 + 已上架」。ES 侧已经过滤过一次，
/// 这里再过一次是因为索引可能滞后：运营刚下架、索引同步失败，那这一单还会被 ES 放出来。
/// 宁可少一条，也不要「搜到了却买不了」。</para>
/// </remarks>
public sealed class QueryShopSearchHandler
    : MediatR.IRequestHandler<QueryShopSearchCommand, ApiResponse<ShopSearchResult>>
{
    private readonly IProductRepository _products;
    private readonly IProductSearchIndex _search;
    private readonly ISearchSynonymRepository _synonyms;
    private readonly ShopItemAssembler _assembler;
    private readonly IMarketingTargetClient _targets;
    private readonly ILogger<QueryShopSearchHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="products">商品仓储。</param>
    /// <param name="search">搜索索引（只用于召回）。</param>
    /// <param name="synonyms">同义词规则仓储。</param>
    /// <param name="assembler">列表项组装器（含到手价计算，与列表查询共用）。</param>
    /// <param name="targets">营销商品范围客户端。</param>
    /// <param name="logger">日志器。</param>
    public QueryShopSearchHandler(
        IProductRepository products,
        IProductSearchIndex search,
        ISearchSynonymRepository synonyms,
        ShopItemAssembler assembler,
        IMarketingTargetClient targets,
        ILogger<QueryShopSearchHandler> logger)
    {
        _products = products;
        _search = search;
        _synonyms = synonyms;
        _assembler = assembler;
        _targets = targets;
        _logger = logger;
    }

    /// <summary>执行搜索。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>搜索结果。</returns>
    /// <remarks>
    /// ES 不可用（未配置 / 抖动 / 索引还没建）时**不报错**，退化成「只按类目浏览」。
    /// 搜索是增强功能，它挂了不该让商品列表页整个打不开。
    /// </remarks>
    public async Task<ApiResponse<ShopSearchResult>> Handle(
        QueryShopSearchCommand request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var keyword = (request.Keyword ?? string.Empty).Trim();

        // 无关键词时，复杂筛选直接走数据库浏览。
        // 有关键词时不能退回数据库 LIKE：那样会绕过 IK 与同义词，
        // “眼睫毛”又会搜不到“睫毛膏”。正确顺序是 ES 召回 Id，再由数据库
        // 做价格、销量、活动等过滤与排序。
        var useDatabaseBrowse = keyword.Length == 0 && HasDatabaseCriteria(request);
        if (useDatabaseBrowse)
        {
            return await FallbackAsync(request, keyword, page, pageSize, ct).ConfigureAwait(false);
        }

        var keywords = keyword.Length == 0
            ? Array.Empty<SearchKeywordVariant>()
            : await ExpandKeywordsAsync(request, keyword, ct).ConfigureAwait(false);

        if (keyword.Length > 0 && HasDatabaseCriteria(request))
        {
            return await SearchWithDatabaseCriteriaAsync(
                    request, keyword, keywords, page, pageSize, ct)
                .ConfigureAwait(false);
        }

        var ids = await _search.SearchIdsAsync(
            keywords, request.PlatformId, request.CategoryId, request.BrandId,
            (page - 1) * pageSize, pageSize, ct);

        // 搜不到就退化成纯类目浏览：宁可给用户一个能点的列表，也不要一个空白页
        if (ids.Count == 0)
        {
            return await FallbackAsync(request, keyword, page, pageSize, ct).ConfigureAwait(false);
        }

        var rows = await _products.GetByIdsAsync(ids, ct);

        // 兜底过滤：索引可能滞后于库（刚下架 / 同步失败），这里以库为准
        var visible = rows
            .Where(a => a.AuditStatus == AuditStatuses.Approved && a.Status == ListingStatuses.OnShelf)
            .ToList();

        // 按 ES 给的相关度顺序重排。库里查出来是不保证顺序的，
        // 不重排的话「最相关的排第一」这个诉求就丢了
        visible = ReorderBySearchRelevance(visible, ids);

        var items = await _assembler.BuildAsync(
            CustomerScope.ResolvePublic(request.CustomerId), visible, ct);

        return ApiResults.Ok(new ShopSearchResult(
            items, items.Count, page, pageSize, keyword,
            keywords.Count > 1 ? "elasticsearch+ik+synonyms" : "elasticsearch+ik"));
    }

    /// <summary>读取适用于当前搜索上下文的同义词规则并展开关键词。</summary>
    private async Task<IReadOnlyList<SearchKeywordVariant>> ExpandKeywordsAsync(
        QueryShopSearchCommand request, string keyword, CancellationToken ct)
    {
        var rules = await _synonyms.ListAsync(ct).ConfigureAwait(false);
        if (rules.Count == 0)
        {
            rules = ProductSearchSynonymDefaults.All
                .Select((terms, index) => new SearchSynonym
                {
                    Terms = terms,
                    RuleType = SearchSynonymRuleTypes.Equivalent,
                    ScopeType = SearchSynonymScopeTypes.Global,
                    ScopeId = 0,
                    Priority = index,
                    Status = SearchSynonymStatuses.Enabled
                })
                .ToList();
        }

        var categoryIds = (request.CategoryIds ?? [])
            .Append(request.CategoryId)
            .Where(a => a > 0)
            .Distinct()
            .ToArray();
        var brandIds = (request.BrandIds ?? [])
            .Append(request.BrandId)
            .Where(a => a > 0)
            .Distinct()
            .ToArray();

        var applicable = rules
            .Where(a => SearchSynonymExpander.IsApplicable(
                a, request.PlatformId, categoryIds, brandIds))
            .ToList();

        return SearchSynonymExpander.Expand(keyword, applicable);
    }

    /// <summary>当前请求是否需要数据库参与筛选或排序。</summary>
    private static bool HasDatabaseCriteria(QueryShopSearchCommand request)
        => request.SortBy != ShopSorts.Default
           || request.MinPrice > 0
           || request.MaxPrice > 0
           || request.ActivityId > 0
           || request.CouponId > 0
           || (request.CategoryIds?.Count ?? 0) > 0
           || (request.BrandIds?.Count ?? 0) > 0;

    /// <summary>
    /// 关键词先由 ES 召回，再在数据库中完成排序、价格与活动范围过滤。
    /// </summary>
    private async Task<ApiResponse<ShopSearchResult>> SearchWithDatabaseCriteriaAsync(
        QueryShopSearchCommand request,
        string keyword,
        IReadOnlyList<SearchKeywordVariant> keywords,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        const int maxSearchCandidates = 10_000;
        var ids = await _search.SearchIdsAsync(
            keywords, request.PlatformId, request.CategoryId, request.BrandId,
            0, maxSearchCandidates, ct).ConfigureAwait(false);

        if (ids.Count == 0)
        {
            return await FallbackAsync(request, keyword, page, pageSize, ct).ConfigureAwait(false);
        }

        var target = await ShopTargetResolver.ResolveAsync(
            _products, _targets, request.ActivityId, request.CouponId, ct).ConfigureAwait(false);

        var allowedIds = target.Specified
            ? ids.Intersect(target.ProductIds).ToArray()
            : ids.ToArray();

        var filter = new ProductQuery
        {
            // 关键词已经由 ES 处理，这里不能再让数据库用 LIKE 二次裁掉同义词结果。
            Keyword = string.Empty,
            PlatformId = request.PlatformId,
            CategoryId = request.CategoryId,
            CategoryIds = request.CategoryIds ?? [],
            BrandId = request.BrandId,
            BrandIds = request.BrandIds ?? [],
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            ProductIds = allowedIds,
            TargetFilterSpecified = true,
            AuditStatus = AuditStatuses.Approved,
            Status = ListingStatuses.OnShelf,
            Order = request.SortBy switch
            {
                ShopSorts.SalesDesc => ProductSorts.SalesDesc,
                ShopSorts.Newest => ProductSorts.Newest,
                ShopSorts.PriceAsc => ProductSorts.PriceAsc,
                ShopSorts.PriceDesc => ProductSorts.PriceDesc,
                _ => ProductSorts.Default
            }
        };

        var (rows, total) = await _products.QueryPagedAsync(page, pageSize, filter, ct)
            .ConfigureAwait(false);

        if (request.SortBy == ShopSorts.Default)
        {
            rows = ReorderBySearchRelevance(rows, ids);
        }

        var items = await _assembler.BuildAsync(
            CustomerScope.ResolvePublic(request.CustomerId), rows, ct);

        return ApiResults.Ok(new ShopSearchResult(
            items, total, page, pageSize, keyword,
            keywords.Count > 1
                ? "elasticsearch+ik+synonyms+database"
                : "elasticsearch+ik+database"));
    }

    /// <summary>ES 搜不到时的降级：按类目 / 品牌直接查库。</summary>
    /// <param name="request">原命令。</param>
    /// <param name="keyword">关键词。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>降级结果。</returns>
    private async Task<ApiResponse<ShopSearchResult>> FallbackAsync(
        QueryShopSearchCommand request, string keyword, int page, int pageSize, CancellationToken ct)
    {
        var target = await ShopTargetResolver.ResolveAsync(
            _products, _targets, request.ActivityId, request.CouponId, ct)
            .ConfigureAwait(false);
        var filter = new ProductQuery
        {
            // 降级时关键词交给数据库做 LIKE 匹配：搜不准，但至少能返回东西
            Keyword = keyword,
            PlatformId = request.PlatformId,
            CategoryId = request.CategoryId,
            CategoryIds = request.CategoryIds ?? [],
            BrandId = request.BrandId,
            BrandIds = request.BrandIds ?? [],
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            TargetFilterSpecified = target.Specified,
            ProductIds = target.ProductIds,
            AuditStatus = AuditStatuses.Approved,
            Status = ListingStatuses.OnShelf,
            Order = request.SortBy switch
            {
                ShopSorts.SalesDesc => ProductSorts.SalesDesc,
                ShopSorts.Newest => ProductSorts.Newest,
                ShopSorts.PriceAsc => ProductSorts.PriceAsc,
                ShopSorts.PriceDesc => ProductSorts.PriceDesc,
                _ => ProductSorts.Default
            }
        };

        // 记一条 Info：搜索降级要能从日志里看出来，否则「搜不准」会被误判成分词器的问题
        _logger.LogInformation("商品搜索无结果，降级为数据库浏览（关键词：{Keyword}）", keyword);

        var (rows, total) = await _products.QueryPagedAsync(page, pageSize, filter, ct);
        var items = await _assembler.BuildAsync(
            CustomerScope.ResolvePublic(request.CustomerId), rows, ct);

        return ApiResults.Ok(new ShopSearchResult(items, total, page, pageSize, keyword, "database"));
    }

    /// <summary>按 ES 召回顺序重排。</summary>
    /// <param name="rows">回库取到的商品。</param>
    /// <param name="ids">ES 返回的 Id 顺序（已按相关度排好）。</param>
    /// <returns>重排后的列表。</returns>
    private static List<ProductEntity> ReorderBySearchRelevance(
        List<ProductEntity> rows, IReadOnlyList<long> ids)
    {
        var rank = new Dictionary<long, int>();
        for (var i = 0; i < ids.Count; i++) rank[ids[i]] = i;

        return rows
            .OrderBy(a => rank.TryGetValue(a.Id, out var r) ? r : int.MaxValue)
            .ToList();
    }
}
