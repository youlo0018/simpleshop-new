using Collaboration.Domain.Common;
using MarketingService.Application.Features.Promotion;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingService.Api.Controllers;

/// <summary>前台营销活动只读接口（小程序活动专区 / 商城页活动 Tab，无需登录）。</summary>
/// <remarks>
/// 与后台 <c>PromotionAdminController</c> 分开：后台要看停用、未来、已结束的活动做管理，
/// 前台只看「启用且进行中」；混在一个控制器里靠参数区分，迟早会把后台能力暴露给游客。
/// </remarks>
[ApiController]
[Route("marketing/activities")]
public sealed class ShopActivityController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public ShopActivityController(IMediator mediator) => _mediator = mediator;

    /// <summary>当前平台 / 商户正在进行的活动列表。</summary>
    /// <param name="command">查询命令：平台、商户、条数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>活动卡片列表，含主图、说明、时间与优惠文案。</returns>
    [HttpPost("Public")]
    public Task<ApiResponse<List<PublicActivityDto>>> Public(
        [FromBody] QueryPublicActivitiesCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
