using Collaboration.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Features.Payment;
using PaymentService.Application.Features.Refund;
using PaymentService.Domain.IRepository;

namespace PaymentService.Api.Controllers;

/// <summary>支付内部接口，供订单服务的工作台报表调用。网关不路由 /internal 前缀。</summary>
[ApiController]
[Route("internal/payments")]
public sealed class InternalPaymentController : ControllerBase
{
    private readonly IRefundRepository _refunds;
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="refunds">退款仓储。</param>
    /// <param name="mediator">MediatR 入口。</param>
    public InternalPaymentController(IRefundRepository refunds, IMediator mediator)
    {
        _refunds = refunds;
        _mediator = mediator;
    }

    /// <summary>按区间汇总退款金额（报表用）。</summary>
    /// <param name="command">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>退款金额合计。</returns>
    /// <remarks>
    /// 为什么退款统计在支付服务而不在订单服务：退款单在支付库，
    /// 订单服务跨库查不到，放过去只能让它把整段退款数据拉回来自己算。
    /// </remarks>
    [HttpPost("refunds/SumApproved")]
    public async Task<ApiResponse<RefundSumResult>> SumApproved(
        [FromBody] RefundSumQuery command, CancellationToken ct)
    {
        var amount = await _refunds.SumApprovedAmountAsync(
            command.From, command.To, command.MerchantId, command.PlatformId, ct)
            .ConfigureAwait(false);

        return ApiResults.Ok(new RefundSumResult(amount));
    }

    /// <summary>把「已审批但订单侧没落账」的退款单重放给订单服务（对账任务调用）。</summary>
    /// <param name="command">重放命令，Limit 控制单轮数量。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>扫描 / 成功 / 失败统计。</returns>
    /// <remarks>
    /// 审批先改退款单状态、再回调订单侧；回调失败时订单侧台账与余额都没动，
    /// 而重复审批会被「已处理」挡住 —— 只能靠这条重放把账补齐（订单侧按退款单号幂等）。
    /// </remarks>
    [HttpPost("refunds/SyncOrder")]
    public Task<ApiResponse<SyncRefundOrdersResult>> SyncRefundOrders(
        [FromBody] SyncRefundOrdersCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);

    /// <summary>把「订单已支付、支付单还挂在待支付」的单子补成已支付（对账任务调用）。</summary>
    /// <param name="command">对账命令，Limit 控制单轮检查数量。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>检查 / 补单 / 跳过 / 失败统计。</returns>
    /// <remarks>
    /// 支付收尾第二步（改支付单状态）失败时订单已经付了，支付单却还是待支付；
    /// 用户再点支付又会被「只有待支付的订单可以支付」挡住，只能靠这条对账补回来。
    /// </remarks>
    [HttpPost("ReconcilePending")]
    public Task<ApiResponse<ReconcilePendingPaymentsResult>> ReconcilePending(
        [FromBody] ReconcilePendingPaymentsCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}

/// <summary>退款汇总查询条件。</summary>
/// <param name="From">区间起（含）。</param>
/// <param name="To">区间止（不含）。</param>
/// <param name="MerchantId">商户 Id，0 表示不限。</param>
/// <param name="PlatformId">平台 Id，0 表示不限。</param>
public sealed record RefundSumQuery(
    DateTime From, DateTime To, long MerchantId = 0, long PlatformId = 0);

/// <summary>退款汇总结果。</summary>
/// <param name="Amount">审批通过的退款金额合计。</param>
public sealed record RefundSumResult(decimal Amount);
