using Collaboration.Domain.Common;
using MarketingService.Application.Features.Coupon;
using MarketingService.Application.Features.Promotion;
using MarketingService.Application.Features.Seckill;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingService.Api.Controllers;

/// <summary>营销服务内部接口，只供受信任服务调用。</summary>
[ApiController]
[Route("internal/marketing")]
public sealed class InternalMarketingController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public InternalMarketingController(IMediator mediator) => _mediator = mediator;

    /// <summary>解析活动或用户券适用的商品范围。</summary>
    /// <param name="command">活动 / 券二选一。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>SPU / SKU 目标集合。</returns>
    [HttpPost("product-targets")]
    public Task<ApiResponse<MarketingProductTargetsDto>> ProductTargets(
        [FromBody] QueryMarketingProductTargetsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>发放订单满赠券（支付成功后调用）。</summary>
    /// <param name="command">订单号。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发放统计。</returns>
    [HttpPost("gifts/Issue")]
    public Task<ApiResponse<GiftIssueResult>> IssueGifts(
        [FromBody] IssueGiftGrantsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>按订单行试算活动优惠（下单链路）。</summary>
    /// <param name="command">订单行。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐行活动优惠。</returns>
    [HttpPost("activities/Quote")]
    public Task<ApiResponse<QuoteOrderDiscountResult>> QuoteActivities(
        [FromBody] QuoteOrderDiscountCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>列出孤儿活动参与记录候选。</summary>
    /// <param name="command">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>候选订单号。</returns>
    [HttpPost("activities/orphan-candidates")]
    public Task<ApiResponse<List<ActivityRecordOrphanItem>>> OrphanCandidates(
        [FromBody] ListActivityRecordOrphansCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>清理孤儿活动参与记录。</summary>
    /// <param name="command">订单号集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>清理条数。</returns>
    [HttpPost("activities/discard-orphans")]
    public Task<ApiResponse<int>> DiscardOrphans(
        [FromBody] DiscardActivityRecordOrphansCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>结束到点的秒杀场次。</summary>
    /// <param name="command">单轮上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>扫描、结束与回补统计。</returns>
    [HttpPost("seckill/sessions/FinishExpired")]
    public Task<ApiResponse<FinishExpiredResult>> FinishExpiredSessions(
        [FromBody] FinishExpiredSessionsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>秒杀单退款时退回秒杀池。</summary>
    /// <param name="command">退款行。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>回退件数。</returns>
    [HttpPost("seckill/grabs/Release")]
    public Task<ApiResponse<ReleaseGrabResult>> ReleaseGrab(
        [FromBody] ReleaseSeckillGrabCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
