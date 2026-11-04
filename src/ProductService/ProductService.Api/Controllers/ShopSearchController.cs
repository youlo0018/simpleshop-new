using Collaboration.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProductService.Application.Features.Search;

namespace ProductService.Api.Controllers;

/// <summary>前台搜索辅助接口（热搜词 / 联想词，无需登录）。</summary>
/// <remarks>
/// 只返回公开搜索建议，不含商品价格等权威数据；真正的搜索仍走
/// <c>shop/products/Search</c>（ES 召回 + 回库取权威值）。
/// </remarks>
[ApiController]
[Route("shop/search")]
public sealed class ShopSearchController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public ShopSearchController(IMediator mediator) => _mediator = mediator;

    /// <summary>当前平台的热搜词。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="limit">最多返回条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>热搜词列表。</returns>
    [HttpGet("HotWords")]
    public Task<ApiResponse<List<string>>> HotWords(
        [FromQuery] long platformId, [FromQuery] int limit = 10, CancellationToken ct = default)
        => _mediator.Send(new QueryHotKeywordsCommand(platformId, limit), ct);

    /// <summary>搜索联想词（热搜词匹配 + 商品名前缀）。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="keyword">已输入的关键词；为空时返回热搜词。</param>
    /// <param name="limit">最多返回条数，1-20。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>建议词列表。</returns>
    [HttpGet("Suggest")]
    public Task<ApiResponse<List<string>>> Suggest(
        [FromQuery] long platformId, [FromQuery] string keyword = "",
        [FromQuery] int limit = 10, CancellationToken ct = default)
        => _mediator.Send(new QuerySearchSuggestCommand(platformId, keyword, limit), ct);
}
