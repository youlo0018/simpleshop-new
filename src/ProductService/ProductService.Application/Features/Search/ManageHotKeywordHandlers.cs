using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using MediatR;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

namespace ProductService.Application.Features.Search;

/// <summary>新建热搜词处理器。</summary>
public sealed class CreateHotKeywordHandler
    : IRequestHandler<CreateHotKeywordCommand, ApiResponse<long>>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public CreateHotKeywordHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行新建。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新热搜词 Id。</returns>
    public async Task<ApiResponse<long>> Handle(CreateHotKeywordCommand request, CancellationToken ct)
    {
        var ctx = TenantContextHolder.Current;
        if (ctx.IsMerchant)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.Forbidden, "商户账号不能维护平台热搜词");
        }

        var platformId = ctx.IsPlatform ? ctx.PlatformId : request.PlatformId;
        if (platformId <= 0)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.BadRequest, "请选择要维护热搜词的平台");
        }

        var id = await _hotKeywords.InsertAsync(new SearchHotKeyword
        {
            PlatformId = platformId,
            MerchantId = 0,
            Keyword = request.Keyword.Trim(),
            SortOrder = request.SortOrder,
            Status = request.Status
        }, ct).ConfigureAwait(false);

        return ApiResults.Ok(id, "热搜词已保存");
    }
}

/// <summary>编辑热搜词处理器。</summary>
public sealed class UpdateHotKeywordHandler : IRequestHandler<UpdateHotKeywordCommand, ApiResponse>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public UpdateHotKeywordHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行编辑。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(UpdateHotKeywordCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台热搜词");
        }

        var keyword = await _hotKeywords.GetByIdAsync(request.KeywordId, ct).ConfigureAwait(false);
        if (keyword is null)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "热搜词不存在");
        }

        keyword.Keyword = request.Keyword.Trim();
        keyword.SortOrder = request.SortOrder;
        keyword.Status = request.Status;

        var affected = await _hotKeywords.UpdateAsync(keyword, ct).ConfigureAwait(false);
        return affected > 0
            ? ApiResponseFactory.Ok("保存成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.InternalError, "保存失败，请重试");
    }
}

/// <summary>删除热搜词处理器。</summary>
public sealed class DeleteHotKeywordHandler : IRequestHandler<DeleteHotKeywordCommand, ApiResponse>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public DeleteHotKeywordHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行软删。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(DeleteHotKeywordCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台热搜词");
        }

        var ok = await _hotKeywords.SoftDeleteAsync(request.KeywordId, ct).ConfigureAwait(false);
        return ok
            ? ApiResponseFactory.Ok("删除成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "热搜词不存在");
    }
}

/// <summary>启停热搜词处理器。</summary>
public sealed class SetHotKeywordStatusHandler
    : IRequestHandler<SetHotKeywordStatusCommand, ApiResponse>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public SetHotKeywordStatusHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行启停。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(SetHotKeywordStatusCommand request, CancellationToken ct)
    {
        if (TenantContextHolder.Current.IsMerchant)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.Forbidden, "商户账号不能维护平台热搜词");
        }

        var ok = await _hotKeywords
            .SetStatusAsync(request.KeywordId, request.Status, ct)
            .ConfigureAwait(false);

        return ok
            ? ApiResponseFactory.Ok("保存成功")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "热搜词不存在");
    }
}

/// <summary>后台热搜词分页处理器。</summary>
public sealed class QueryHotKeywordsAdminHandler
    : IRequestHandler<QueryHotKeywordsAdminCommand, ApiResponse<PagedResult<HotKeywordListItemDto>>>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public QueryHotKeywordsAdminHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>热搜词分页。</returns>
    public async Task<ApiResponse<PagedResult<HotKeywordListItemDto>>> Handle(
        QueryHotKeywordsAdminCommand request, CancellationToken ct)
    {
        var (items, total) = await _hotKeywords
            .PageAsync(request.Status, request.Keyword, request.Page, request.PageSize, ct)
            .ConfigureAwait(false);

        var dtos = items.Select(a => new HotKeywordListItemDto(
            a.Id.ToString(), a.Keyword, a.SortOrder, a.Status,
            SearchHotKeywordStatuses.NameOf(a.Status), a.PlatformId,
            a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"))).ToList();

        return ApiResults.Ok(new PagedResult<HotKeywordListItemDto>(
            dtos, total, request.Page, request.PageSize));
    }
}

/// <summary>小程序热搜词处理器。</summary>
public sealed class QueryHotKeywordsHandler
    : IRequestHandler<QueryHotKeywordsCommand, ApiResponse<List<string>>>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    public QueryHotKeywordsHandler(ISearchHotKeywordRepository hotKeywords)
        => _hotKeywords = hotKeywords;

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>启用中的热搜词。</returns>
    public async Task<ApiResponse<List<string>>> Handle(
        QueryHotKeywordsCommand request, CancellationToken ct)
    {
        var items = await _hotKeywords
            .ListEnabledAsync(request.PlatformId, request.Limit, ct)
            .ConfigureAwait(false);

        return ApiResults.Ok(items.Select(a => a.Keyword).Where(a => a.Length > 0).ToList());
    }
}

/// <summary>小程序搜索联想处理器。</summary>
public sealed class QuerySearchSuggestHandler
    : IRequestHandler<QuerySearchSuggestCommand, ApiResponse<List<string>>>
{
    private readonly ISearchHotKeywordRepository _hotKeywords;
    private readonly IProductRepository _products;

    /// <summary>构造处理器。</summary>
    /// <param name="hotKeywords">热搜词仓储。</param>
    /// <param name="products">商品仓储（按名称前缀取建议）。</param>
    public QuerySearchSuggestHandler(
        ISearchHotKeywordRepository hotKeywords, IProductRepository products)
    {
        _hotKeywords = hotKeywords;
        _products = products;
    }

    /// <summary>执行联想。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>最多 limit 条建议词。</returns>
    /// <remarks>
    /// 关键词为空时不查商品名，只返回该平台的热搜词（等价于初始推荐）。
    /// 热词匹配 + 商品名前缀合并去重，保持热词在前。
    /// </remarks>
    public async Task<ApiResponse<List<string>>> Handle(
        QuerySearchSuggestCommand request, CancellationToken ct)
    {
        var keyword = (request.Keyword ?? string.Empty).Trim();
        var limit = Math.Clamp(request.Limit, 1, 20);

        var hot = keyword.Length == 0
            ? await _hotKeywords.ListEnabledAsync(request.PlatformId, limit, ct).ConfigureAwait(false)
            : await _hotKeywords.ListMatchingAsync(request.PlatformId, keyword, limit, ct)
                .ConfigureAwait(false);

        var names = keyword.Length == 0
            ? []
            : await _products.SuggestNamesAsync(request.PlatformId, keyword, limit, ct)
                .ConfigureAwait(false);

        var merged = hot.Select(a => a.Keyword)
            .Concat(names)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return ApiResults.Ok(merged);
    }
}
