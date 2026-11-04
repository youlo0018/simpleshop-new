using Collaboration.Domain.Common;
using MarketingService.Application.Features.Reports;
using MarketingService.Domain.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace MarketingService.Api.Controllers;

/// <summary>营销与秒杀效果报表（BUSINESS.md 17）。</summary>
/// <remarks>
/// 权限点：<c>report:marketing</c>（路径 <c>/gateway/reports/Marketing</c>）。
/// 这里只挂路由标注，鉴权由网关统一拦截。
/// </remarks>
[ApiController]
[Route("reports")]
public sealed class ReportController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public ReportController(IMediator mediator) => _mediator = mediator;

    /// <summary>营销效果报表（活动 + 券）。</summary>
    /// <param name="command">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐活动的参与订单数 / 参与金额 / 折扣总额，以及券的发放 / 领取 / 核销 / 核销率 / 折扣总额。</returns>
    /// <remarks>
    /// 活动段就是「下钻订单明细」的入口：拿到 <c>activities[].activityId</c> 后调
    /// <c>POST /marketing/activities/Records</c> 翻该活动的参与订单。
    /// </remarks>
    [HttpPost("Marketing")]
    public Task<ApiResponse<MarketingReport>> Marketing(
        [FromBody] QueryMarketingReportCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>秒杀效果报表。</summary>
    /// <param name="command">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>逐场次的参与人数、抢购成功数、售罄率、PV 与成交额。</returns>
    /// <remarks>
    /// 场次 PV 的口径（BUSINESS.md 17）：小程序进入秒杀频道时按场次调用
    /// <c>GET /marketing/seckill/sessions/View?sessionId=</c> 打埋点，
    /// 请求日志中间件把它记进 pv.log，日志服务按「路径 + 场次 Id + 时间区间」聚合。
    /// 只统计营销服务侧那条日志，网关代理日志不计入，避免一次曝光算两次。
    /// </remarks>
    [HttpPost("Seckill")]
    public Task<ApiResponse<SeckillReport>> Seckill(
        [FromBody] QuerySeckillReportCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
