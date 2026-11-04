using Collaboration.Domain.Common;
using MediatR;
using MerchantPlatformService.Application.Features.Notice;
using Microsoft.AspNetCore.Mvc;

namespace MerchantPlatformService.Api.Controllers;

/// <summary>小程序公告只读接口（无需登录）。</summary>
/// <remarks>
/// 与后台控制器分开：前台只返回启用中的公告；后台需要看停用、排序与创建时间。
/// </remarks>
[ApiController]
[Route("platforms/notices")]
public sealed class ShopNoticeController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public ShopNoticeController(IMediator mediator) => _mediator = mediator;

    /// <summary>按平台编码读取启用中的公告。</summary>
    /// <param name="command">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>公告列表（已按排序值升序）。</returns>
    [HttpPost("Public")]
    public Task<ApiResponse<List<PublicNoticeDto>>> Public(
        [FromBody] QueryPublicNoticesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
