using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Domain.IRepository;

namespace ProductService.Application.Features.Shop;

/// <summary>查询商户在售商品实际使用的分类。</summary>
/// <param name="MerchantId">商户 Id。</param>
/// <param name="PlatformId">平台 Id，0 表示不限。</param>
public record QueryMerchantCategoriesCommand(long MerchantId, long PlatformId = 0)
    : IRequest<ApiResponse<List<MerchantCategoryDto>>>;

/// <summary>店铺页分类 chip。</summary>
/// <param name="CategoryId">分类 Id（字符串）。</param>
/// <param name="CategoryName">分类名。</param>
/// <param name="Icon">图标。</param>
public sealed record MerchantCategoryDto(
    string CategoryId, string CategoryName, string Icon);

/// <summary>商户分类查询处理器。</summary>
public sealed class QueryMerchantCategoriesHandler
    : IRequestHandler<QueryMerchantCategoriesCommand, ApiResponse<List<MerchantCategoryDto>>>
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;

    /// <summary>构造处理器。</summary>
    /// <param name="products">商品仓储。</param>
    /// <param name="categories">分类仓储。</param>
    public QueryMerchantCategoriesHandler(
        IProductRepository products, ICategoryRepository categories)
    {
        _products = products;
        _categories = categories;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>按分类排序值排列的分类列表。</returns>
    public async Task<ApiResponse<List<MerchantCategoryDto>>> Handle(
        QueryMerchantCategoriesCommand request, CancellationToken ct)
    {
        var usedIds = await _products
            .ListVisibleCategoryIdsAsync(request.MerchantId, request.PlatformId, ct)
            .ConfigureAwait(false);
        if (usedIds.Count == 0)
        {
            return ApiResults.Ok(new List<MerchantCategoryDto>());
        }

        var used = usedIds.ToHashSet();
        var rows = (await _categories.ListAllAsync(ct).ConfigureAwait(false))
            .Where(a => used.Contains(a.Id) && a.Status == 1)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Id)
            .Select(a => new MerchantCategoryDto(
                a.Id.ToString(), a.CategoryName, a.Icon))
            .ToList();

        return ApiResults.Ok(rows);
    }
}

/// <summary>商户分类查询校验器注册。</summary>
public static class MerchantCategoryValidators
{
    /// <summary>注册校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddMerchantCategoryValidators(IServiceCollection services)
        => services.AddScoped<IValidator<QueryMerchantCategoriesCommand>,
            QueryMerchantCategoriesValidator>();

    /// <summary>参数校验。</summary>
    private sealed class QueryMerchantCategoriesValidator
        : AbstractValidator<QueryMerchantCategoriesCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryMerchantCategoriesValidator()
        {
            RuleFor(x => x.MerchantId).GreaterThan(0).WithMessage("商户 Id 不正确");
            RuleFor(x => x.PlatformId).GreaterThanOrEqualTo(0).WithMessage("平台信息不正确");
        }
    }
}
