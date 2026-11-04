using Collaboration.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ProductService.Application.Features.Search;

namespace ProductService.Api.Controllers;

/// <summary>搜索热搜词后台接口。</summary>
/// <remarks>权限点：<c>search:hotword</c>；商户账号由 Handler 显式拒绝。</remarks>
[ApiController]
[Route("search/hot-keywords")]
public sealed class SearchHotKeywordController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public SearchHotKeywordController(IMediator mediator) => _mediator = mediator;

    /// <summary>热搜词分页。</summary>
    /// <param name="command">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>热搜词分页。</returns>
    [HttpPost("List")]
    public Task<ApiResponse<PagedResult<HotKeywordListItemDto>>> List(
        [FromBody] QueryHotKeywordsAdminCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>新建热搜词。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新热搜词 Id。</returns>
    [HttpPost("Create")]
    public Task<ApiResponse<long>> Create(
        [FromBody] CreateHotKeywordCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>编辑热搜词。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("Update")]
    public Task<ApiResponse> Update(
        [FromBody] UpdateHotKeywordCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>删除热搜词。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("Delete")]
    public Task<ApiResponse> Delete(
        [FromBody] DeleteHotKeywordCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>启停热搜词。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("SetStatus")]
    public Task<ApiResponse> SetStatus(
        [FromBody] SetHotKeywordStatusCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
