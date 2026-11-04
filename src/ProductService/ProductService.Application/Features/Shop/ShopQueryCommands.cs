using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace ProductService.Application.Features.Shop;

/// <summary>前台商品分页（只返回**审核通过 + 已上架**的商品）。</summary>
/// <param name="CustomerId">客户 Id；传 0 表示游客，优惠只算活动不算券。</param>
/// <param name="Keyword">按商品名模糊搜索。</param>
/// <param name="CategoryId">按分类过滤，0 不限。</param>
/// <param name="BrandId">按品牌过滤，0 不限。</param>
/// <param name="MerchantId">
/// 按店铺过滤，0 表示不限，店铺页用它列本店商品。
/// 没有它的话店铺页只能拿到全站商品 —— 小程序「店铺页」退化成「商品列表换个标题」。
/// </param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="SortBy">排序方式，见 <see cref="ShopSorts"/>。</param>
/// <param name="PlatformId">
/// 当前平台 Id。C 端必须传（小程序按 platformCode 锁平台）；0 表示不限，仅供后台 / 内部调试。
/// </param>
/// <param name="CategoryIds">分类多选；非空时优先于 CategoryId。</param>
/// <param name="BrandIds">品牌多选；非空时优先于 BrandId。</param>
/// <param name="MinPrice">最低售价（按 SKU 最低售价），0 表示不限。</param>
/// <param name="MaxPrice">最高售价（按 SKU 最低售价），0 表示不限。</param>
/// <param name="ActivityId">只保留该活动适用的商品，0 表示不限。</param>
/// <param name="CouponId">只保留该用户券适用的商品，0 表示不限。</param>
public record QueryShopProductsCommand(
    long CustomerId = 0,
    string Keyword = "",
    long CategoryId = 0,
    long BrandId = 0,
    long MerchantId = 0,
    int Page = 1,
    int PageSize = 20,
    int SortBy = ShopSorts.Default,
    long PlatformId = 0,
    IReadOnlyList<long>? CategoryIds = null,
    IReadOnlyList<long>? BrandIds = null,
    decimal MinPrice = 0m,
    decimal MaxPrice = 0m,
    long ActivityId = 0,
    long CouponId = 0) : IRequest<ApiResponse<ShopProductPage>>;

/// <summary>前台商品详情（只返回审核通过 + 已上架的商品）。</summary>
/// <param name="CustomerId">客户 Id；0 表示游客。</param>
/// <param name="ProductId">商品 Id。</param>
/// <param name="PlatformId">当前平台 Id；C 端必须传，商品不属于该平台时回 404。</param>
public record QueryShopProductDetailCommand(long CustomerId, long ProductId, long PlatformId = 0)
    : IRequest<ApiResponse<ShopProductDetailDto>>;

/// <summary>前台商品搜索（Elasticsearch + IK 分词，无需登录）。</summary>
/// <param name="CustomerId">客户 Id；0 表示游客，只算活动价不计券。</param>
/// <param name="Keyword">关键词，空表示按类目 / 品牌浏览。</param>
/// <param name="CategoryId">分类过滤，0 表示不限。</param>
/// <param name="BrandId">品牌过滤，0 表示不限。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="PlatformId">当前平台 Id；C 端必须传。</param>
/// <param name="SortBy">排序方式，见 <see cref="ShopSorts"/>。</param>
/// <param name="CategoryIds">分类多选；非空时优先于 CategoryId。</param>
/// <param name="BrandIds">品牌多选；非空时优先于 BrandId。</param>
/// <param name="MinPrice">最低售价，0 表示不限。</param>
/// <param name="MaxPrice">最高售价，0 表示不限。</param>
/// <param name="ActivityId">只保留该活动适用的商品，0 表示不限。</param>
/// <param name="CouponId">只保留该用户券适用的商品，0 表示不限。</param>
public record QueryShopSearchCommand(
    long CustomerId = 0,
    string Keyword = "",
    long CategoryId = 0,
    long BrandId = 0,
    int Page = 1,
    int PageSize = 20,
    long PlatformId = 0,
    int SortBy = ShopSorts.Default,
    IReadOnlyList<long>? CategoryIds = null,
    IReadOnlyList<long>? BrandIds = null,
    decimal MinPrice = 0m,
    decimal MaxPrice = 0m,
    long ActivityId = 0,
    long CouponId = 0) : IRequest<ApiResponse<ShopSearchResult>>;

/// <summary>前台搜索结果。</summary>
/// <param name="Items">商品列表。</param>
/// <param name="Total">命中总数（按 ES 的 hits 总数估算，可能略偏）。</param>
/// <param name="Page">当前页码。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="Keyword">回显的关键词。</param>
/// <param name="Engine">检索引擎说明，便于排查「为什么搜不准」。</param>
public sealed record ShopSearchResult(
    IReadOnlyList<ShopProductItem> Items, long Total, int Page, int PageSize,
    string Keyword, string Engine);

/// <summary>前台排序方式。</summary>
public static class ShopSorts
{
    /// <summary>默认：综合（排序值 + 销量）。</summary>
    public const int Default = 0;

    /// <summary>销量从高到低。</summary>
    public const int SalesDesc = 1;

    /// <summary>价格从低到高（按到手价）。</summary>
    public const int PriceAsc = 2;

    /// <summary>价格从高到低（按到手价）。</summary>
    public const int PriceDesc = 3;

    /// <summary>最新上架。</summary>
    public const int Newest = 4;
}

/// <summary>前台商品列表项。</summary>
/// <param name="ProductId">商品 Id（字符串形式，雪花 Id 避免前端丢精度）。</param>
/// <param name="MerchantId">商户 Id（字符串）；0 表示平台自营。</param>
/// <param name="SpuName">商品名。</param>
/// <param name="SubTitle">副标题。</param>
/// <param name="MainImage">主图。</param>
/// <param name="BrandName">品牌名。</param>
/// <param name="CategoryName">分类名。</param>
/// <param name="DeliveryType">配送方式。</param>
/// <param name="OriginalPrice">划线原价：各启用 SKU 原价的最小值。</param>
/// <param name="FinalPrice">到手价：各启用 SKU 到手价的最小值。</param>
/// <param name="DiscountSource">优惠来源标签，无优惠为 <c>none</c>。</param>
/// <param name="DiscountSourceName">优惠来源名称（活动名或券码）。</param>
/// <param name="HasDiscount">是否有优惠。</param>
/// <param name="Sales">销量。</param>
/// <param name="EvaluationScore">评价均分。<b>0 表示还没有评价</b>，前端展示时要转成 5.0。</param>
/// <param name="EvaluationCount">评价条数（只数首评）。</param>
public sealed record ShopProductItem(
    string ProductId, string MerchantId, string SpuName, string SubTitle, string MainImage,
    string BrandName, string CategoryName, int DeliveryType,
    decimal OriginalPrice, decimal FinalPrice,
    string DiscountSource, string DiscountSourceName, bool HasDiscount, long Sales,
    decimal EvaluationScore, int EvaluationCount);

/// <summary>前台商品分页结果。</summary>
/// <param name="Items">当页商品。</param>
/// <param name="Total">总条数。</param>
/// <param name="Page">当前页码。</param>
/// <param name="PageSize">每页条数。</param>
public sealed record ShopProductPage(
    IReadOnlyList<ShopProductItem> Items, long Total, int Page, int PageSize);

/// <summary>前台商品详情。</summary>
/// <param name="ProductId">商品 Id。</param>
/// <param name="MerchantId">
/// 商品所属商户 Id（字符串下发）。详情页「进店」按钮靠它跳店铺页 ——
/// 之前前端从客户资料里取 merchantId（客户资料里根本没有这个字段），
/// 结果永远跳到 <c>merchantId=</c> 空参数。
/// </param>
/// <param name="SpuName">商品名。</param>
/// <param name="SubTitle">副标题。</param>
/// <param name="MainImage">主图。</param>
/// <param name="Images">图集 URL 数组（由 JSON 解析后下发，前端不再二次解析）。</param>
/// <param name="DetailImages">详情图 URL 数组（由 JSON 解析后下发）。</param>
/// <param name="BrandName">品牌名。</param>
/// <param name="CategoryName">分类名。</param>
/// <param name="DeliveryType">配送方式。</param>
/// <param name="Specs">规格项。</param>
/// <param name="Skus">SKU 列表，含每个 SKU 的到手价。</param>
/// <param name="EvaluationScore">评价均分。<b>0 表示还没有评价</b>，展示层要转成 5.0。</param>
/// <param name="EvaluationCount">评价条数（只数首评）。</param>
public sealed record ShopProductDetailDto(
    string ProductId, string MerchantId, string SpuName, string SubTitle,
    string MainImage, IReadOnlyList<string> Images, IReadOnlyList<string> DetailImages,
    string BrandName, string CategoryName, int DeliveryType,
    decimal EvaluationScore, int EvaluationCount,
    IReadOnlyList<ShopSpecDto> Specs, IReadOnlyList<ShopSkuDto> Skus);

/// <summary>前台规格项。</summary>
/// <param name="SpecId">规格项 Id。</param>
/// <param name="SpecName">规格名（颜色 / 尺码）。</param>
/// <param name="Values">规格值。</param>
public sealed record ShopSpecDto(long SpecId, string SpecName, IReadOnlyList<ShopSpecValueDto> Values);

/// <summary>前台规格值。</summary>
/// <param name="SpecValueId">规格值 Id。</param>
/// <param name="ValueName">规格值名。</param>
public sealed record ShopSpecValueDto(long SpecValueId, string ValueName);

/// <summary>前台 SKU。</summary>
/// <param name="SkuId">SKU Id。</param>
/// <param name="SkuName">SKU 名。</param>
/// <param name="SkuSpecText">规格文本。</param>
/// <param name="Image">SKU 图。</param>
/// <param name="OriginalPrice">原价。</param>
/// <param name="FinalPrice">到手价。</param>
/// <param name="DiscountSource">优惠来源标签。</param>
/// <param name="DiscountSourceName">优惠来源名称。</param>
/// <param name="SpecValueIds">该 SKU 覆盖的规格值 Id，前端用于按规格筛选。</param>
public sealed record ShopSkuDto(
    string SkuId, string SkuName, string SkuSpecText, string Image,
    decimal OriginalPrice, decimal FinalPrice,
    string DiscountSource, string DiscountSourceName, IReadOnlyList<long> SpecValueIds);

/// <summary>前台商品命令的校验器注册。</summary>
public static class ShopValidators
{
    /// <summary>注册全部校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddShopValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<QueryShopProductsCommand>, QueryShopProductsValidator>();
        services.AddScoped<IValidator<QueryShopSearchCommand>, QueryShopSearchValidator>();
        services.AddScoped<IValidator<QueryShopProductDetailCommand>, QueryShopDetailValidator>();
    }

    /// <summary>前台列表查询校验。</summary>
    private sealed class QueryShopProductsValidator : AbstractValidator<QueryShopProductsCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryShopProductsValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThanOrEqualTo(0).WithMessage("客户 Id 不正确");
            RuleFor(x => x.PlatformId).GreaterThanOrEqualTo(0).WithMessage("平台信息不正确");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码必须大于 0");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithMessage("每页条数必须在 1 ~ 50 之间");
            RuleFor(x => x.Keyword).MaximumLength(64).WithMessage("搜索关键字最多 64 个字符");
            RuleFor(x => x.SortBy).Must(a => a is >= ShopSorts.Default and <= ShopSorts.Newest)
                .WithMessage("排序方式不正确");
            RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0m).WithMessage("最低价格不能为负数");
            RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0m).WithMessage("最高价格不能为负数");
            RuleFor(x => x.MaxPrice)
                .Must((cmd, max) => cmd.MinPrice <= 0 || max <= 0 || max >= cmd.MinPrice)
                .WithMessage("价格区间不正确");
            RuleFor(x => x.CategoryIds).Must(l => l is null || l.Count <= 50).WithMessage("分类筛选过多");
            RuleFor(x => x.BrandIds).Must(l => l is null || l.Count <= 50).WithMessage("品牌筛选过多");
            RuleFor(x => x.ActivityId).GreaterThanOrEqualTo(0).WithMessage("活动 Id 不正确");
            RuleFor(x => x.CouponId).GreaterThanOrEqualTo(0).WithMessage("券 Id 不正确");
        }
    }

    /// <summary>前台搜索校验。</summary>
    private sealed class QueryShopSearchValidator : AbstractValidator<QueryShopSearchCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryShopSearchValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThanOrEqualTo(0).WithMessage("客户 Id 不正确");
            RuleFor(x => x.PlatformId).GreaterThanOrEqualTo(0).WithMessage("平台信息不正确");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码必须大于 0");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithMessage("每页条数必须在 1 ~ 50 之间");
            RuleFor(x => x.Keyword).MaximumLength(64).WithMessage("搜索关键字最多 64 个字符");
            RuleFor(x => x.SortBy).Must(a => a is >= ShopSorts.Default and <= ShopSorts.Newest)
                .WithMessage("排序方式不正确");
            RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0m).WithMessage("最低价格不能为负数");
            RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0m).WithMessage("最高价格不能为负数");
            RuleFor(x => x.MaxPrice)
                .Must((cmd, max) => cmd.MinPrice <= 0 || max <= 0 || max >= cmd.MinPrice)
                .WithMessage("价格区间不正确");
            RuleFor(x => x.CategoryIds).Must(l => l is null || l.Count <= 50).WithMessage("分类筛选过多");
            RuleFor(x => x.BrandIds).Must(l => l is null || l.Count <= 50).WithMessage("品牌筛选过多");
            RuleFor(x => x.ActivityId).GreaterThanOrEqualTo(0).WithMessage("活动 Id 不正确");
            RuleFor(x => x.CouponId).GreaterThanOrEqualTo(0).WithMessage("券 Id 不正确");
        }
    }

    /// <summary>前台详情查询校验。</summary>
    private sealed class QueryShopDetailValidator : AbstractValidator<QueryShopProductDetailCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryShopDetailValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThanOrEqualTo(0).WithMessage("客户 Id 不正确");
            RuleFor(x => x.PlatformId).GreaterThanOrEqualTo(0).WithMessage("平台信息不正确");
            RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("商品 Id 必须为正数");
        }
    }
}
