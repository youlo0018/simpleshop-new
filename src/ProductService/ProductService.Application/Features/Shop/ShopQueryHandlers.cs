using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using System.Text.Json;
using ProductService.Application.Services;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

// `Features/Product` 这个命名空间会遮蔽同名的 Product 实体（CS0118），
// 必须在本地起别名。全项目统一这么处理，别的地方也一样。
using ProductEntity = ProductService.Domain.Entities.Product;

namespace ProductService.Application.Features.Shop;

/// <summary>前台商品分页处理器。</summary>
/// <remarks>
/// <para><b>审核通过 + 已上架由服务端强制过滤</b>，不靠调用方传参数。
/// 前台看到「已下架」或「未审核」的商品，轻则是运营事故、重则是能把没审过的商品买出去——
/// 所以过滤条件写死在服务端，命令里压根没有这两个字段。</para>
///
/// <para><b>到手价一次批量算</b>：先把当页商品的启用 SKU 一次查回来，
/// 再合成一次营销试算（BUSINESS.md 11.5 的「整页商品合并为一次批量试算」）。
/// 逐个商品调营销服务就是 N+1，一屏 20 个商品就是 20 次跨服务调用。</para>
/// </remarks>
public sealed class QueryShopProductsHandler
    : MediatR.IRequestHandler<QueryShopProductsCommand, ApiResponse<ShopProductPage>>
{
    private readonly IProductRepository _products;
    private readonly ShopItemAssembler _assembler;
    private readonly IMarketingTargetClient _targets;

    /// <summary>构造处理器。</summary>
    /// <param name="products">商品仓储。</param>
    /// <param name="assembler">列表项组装器（含到手价计算，与搜索查询共用同一份实现）。</param>
    /// <param name="targets">营销商品范围客户端。</param>
    public QueryShopProductsHandler(
        IProductRepository products,
        ShopItemAssembler assembler,
        IMarketingTargetClient targets)
    {
        _products = products;
        _assembler = assembler;
        _targets = targets;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>商品分页结果，含到手价与优惠来源。</returns>
    public async Task<ApiResponse<ShopProductPage>> Handle(
        QueryShopProductsCommand request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var target = await ShopTargetResolver.ResolveAsync(
            _products, _targets, request.ActivityId, request.CouponId, ct)
            .ConfigureAwait(false);

        var filter = new ProductQuery
        {
            Keyword = request.Keyword ?? string.Empty,
            PlatformId = request.PlatformId,
            CategoryId = request.CategoryId,
            CategoryIds = request.CategoryIds ?? [],
            BrandId = request.BrandId,
            BrandIds = request.BrandIds ?? [],
            MinPrice = request.MinPrice,
            MaxPrice = request.MaxPrice,
            MerchantId = request.MerchantId,
            TargetFilterSpecified = target.Specified,
            ProductIds = target.ProductIds,

            // 前台只看「审核通过 + 已上架」，写死在服务端
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

        var (rows, total) = await _products.QueryPagedAsync(page, pageSize, filter, ct);

        // 公开只读接口：有令牌用令牌客户（不一致直接 403），没有客户上下文一律按游客 ——
        // 请求体里的 customerId 在匿名路径上不可信（否则能读到别人的券码）。
        var items = await _assembler.BuildAsync(
            CustomerScope.ResolvePublic(request.CustomerId), rows, ct);

        return ApiResults.Ok(new ShopProductPage(items, total, page, pageSize));
    }
}

/// <summary>前台商品详情处理器。</summary>
public sealed class QueryShopProductDetailHandler
    : MediatR.IRequestHandler<QueryShopProductDetailCommand, ApiResponse<ShopProductDetailDto>>
{
    private readonly IProductRepository _products;
    private readonly IShopPriceClient _prices;

    /// <summary>构造处理器。</summary>
    /// <param name="products">商品仓储。</param>
    /// <param name="prices">营销服务客户端（到手价）。</param>
    public QueryShopProductDetailHandler(IProductRepository products, IShopPriceClient prices)
    {
        _products = products;
        _prices = prices;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>商品详情，含每个 SKU 的到手价。</returns>
    /// <remarks>
    /// 未上架 / 未审核通过的商品一律回 404。前台能看到「存在但没上架」的商品，
    /// 等于告诉竞品「我们正在筹备 XX」，也等于让用户收藏一个买不到的东西。
    /// </remarks>
    public async Task<ApiResponse<ShopProductDetailDto>> Handle(
        QueryShopProductDetailCommand request, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(request.ProductId, ct);
        if (product is null
            // 平台维度：别的平台的商品与「已下架」回同一个 404，不泄露它存在
            || (request.PlatformId > 0 && product.PlatformId != request.PlatformId)
            || product.AuditStatus != AuditStatuses.Approved
            || product.Status != ListingStatuses.OnShelf)
        {
            return ApiResults.Fail<ShopProductDetailDto>(BaseApiResponseCode.NotFound, "商品不存在或已下架");
        }

        var specs = await _products.GetSpecsAsync(product.Id, ct);
        var values = await _products.GetSpecValuesAsync(product.Id, ct);
        var skus = await _products.GetSkusAsync(product.Id, ct);
        var links = await _products.GetSkuSpecLinksAsync(product.Id, ct);

        var enabled = skus.Where(a => a.Status == SkuStatuses.Enabled).ToList();

        var prices = enabled.Count == 0
            ? new Dictionary<long, ShopSkuPrice>()
            // 每个 SKU 单独定价：详情页展示的每个规格的到手价，
            // 必须就是用户选中那个规格下单时的到手价。
            : await _prices.CalculateAsync(
                CustomerScope.ResolvePublic(request.CustomerId),
                enabled.Select(a => new ShopPriceLine(product.Id, a.Id, a.Price)).ToList(),
                ct);

        var linksBySku = links.GroupBy(a => a.SkuId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.SpecValueId).OrderBy(a => a).ToArray());

        var valuesBySpec = values.GroupBy(a => a.SpecId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToList());

        var specDtos = specs
            .OrderBy(a => a.SortOrder)
            .Select(spec => new ShopSpecDto(
                spec.Id,
                spec.SpecName,
                valuesBySpec.TryGetValue(spec.Id, out var vs)
                    ? vs.Select(v => new ShopSpecValueDto(v.Id, v.ValueName)).ToList()
                    : new List<ShopSpecValueDto>()))
            .ToList();

        var skuDtos = enabled.Select(sku =>
        {
            var price = prices.TryGetValue(sku.Id, out var p)
                ? p
                : new ShopSkuPrice(sku.Price, sku.Price, 0m, "none", string.Empty);

            return new ShopSkuDto(
                sku.Id.ToString(),
                sku.SkuName,
                sku.SkuSpecText,
                sku.Image,
                sku.Price,
                price.PayableAmount,
                price.Source,
                price.SourceName,
                linksBySku.TryGetValue(sku.Id, out var ids) ? ids : Array.Empty<long>());
        }).ToList();

        var dto = new ShopProductDetailDto(
            product.Id.ToString(),
            product.MerchantId.ToString(),
            product.SpuName,
            product.SubTitle,
            product.MainImage,
            ParseImageArray(product.Images),
            ParseImageArray(product.DetailImages),
            product.BrandName,
            product.CategoryName,
            product.DeliveryType,
            product.EvaluationScore,
            product.EvaluationCount,
            specDtos,
            skuDtos);

        return ApiResults.Ok(dto);
    }

    /// <summary>把商品图集的 JSON 文本解析成 URL 数组。</summary>
    /// <param name="json">商品实体里的图集 JSON。</param>
    /// <returns>URL 数组；空或非法 JSON 返回空数组。</returns>
    /// <remarks>
    /// 历史脏数据不应让整个商品详情 500：解析失败时图集降级为空，其它字段照常展示。
    /// </remarks>
    private static IReadOnlyList<string> ParseImageArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
