using Collaboration.Domain.Common;
using MediatR;
using MerchantPlatformService.Application.Features.Notice;
using Microsoft.AspNetCore.Mvc;

namespace MerchantPlatformService.Api.Controllers;

/// <summary>平台公告后台接口。</summary>
/// <remarks>权限点：<c>notice:read</c> / <c>notice:manage</c>；商户账号由 Handler 显式拒绝。</remarks>
[ApiController]
[Route("platforms/notices")]
public sealed class PlatformNoticeController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public PlatformNoticeController(IMediator mediator) => _mediator = mediator;

    /// <summary>公告分页。</summary>
    /// <param name="command">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>公告分页。</returns>
    [HttpPost("List")]
    public Task<ApiResponse<PagedResult<NoticeListItemDto>>> List(
        [FromBody] QueryNoticesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>新建公告。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新公告 Id。</returns>
    [HttpPost("Create")]
    public Task<ApiResponse<long>> Create(
        [FromBody] CreateNoticeCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>编辑公告。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("Update")]
    public Task<ApiResponse> Update(
        [FromBody] UpdateNoticeCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>删除公告（软删）。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("Delete")]
    public Task<ApiResponse> Delete(
        [FromBody] DeleteNoticeCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>启停公告。</summary>
    /// <param name="command">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    [HttpPost("SetStatus")]
    public Task<ApiResponse> SetStatus(
        [FromBody] SetNoticeStatusCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
