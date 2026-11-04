using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Services;
using PaymentService.Domain.IRepository;

namespace PaymentService.Application.Features.Refund;

/// <summary>把「已审批但订单侧没落账」的退款单重放给订单服务（ScheduledService 调用）。</summary>
/// <param name="Limit">单轮最多处理多少条，1 ~ 500。</param>
public record SyncRefundOrdersCommand(int Limit = 50)
    : IRequest<ApiResponse<SyncRefundOrdersResult>>;

/// <summary>重放结果。</summary>
/// <param name="Scanned">本轮扫描到的待同步退款单数。</param>
/// <param name="Synced">本轮同步成功的条数。</param>
/// <param name="Failed">仍失败的清单（退款单号与原因）。</param>
public sealed record SyncRefundOrdersResult(
    int Scanned, int Synced, IReadOnlyList<string> Failed);

/// <summary>重放命令校验。</summary>
public sealed class SyncRefundOrdersValidator : AbstractValidator<SyncRefundOrdersCommand>
{
    /// <summary>构造校验器。</summary>
    public SyncRefundOrdersValidator()
        => RuleFor(x => x.Limit).InclusiveBetween(1, 500).WithMessage("单轮处理数量需在 1 ~ 500 之间");
}

/// <summary>退款单与订单侧的对账重放处理器。</summary>
/// <remarks>
/// <para><b>为什么需要它</b>：审批链路是「先把退款单改成已退款，再回调订单侧」。
/// 订单侧回调失败时（订单服务抖动、网络超时），钱在支付侧已经退了，
/// 而订单侧的台账 / 已退金额 / 库存 / 积分都没动 —— 重复审批又会被「该退款单已处理」挡住。
/// 没有这条重放，这个不一致状态就永久存在，另一条「代客退款」入口还会基于旧余额再退一次。</para>
///
/// <para><b>重放安全</b>：订单侧按退款单号幂等（台账 refund_no 唯一），
/// 重复重放不会写第二条台账、也不会把库存还两遍（回补单号带退款单号）。</para>
/// </remarks>
public sealed class SyncRefundOrdersHandler
    : IRequestHandler<SyncRefundOrdersCommand, ApiResponse<SyncRefundOrdersResult>>
{
    private readonly IRefundRepository _refunds;
    private readonly IOrderPort _orders;
    private readonly ILogger<SyncRefundOrdersHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="refunds">退款仓储。</param>
    /// <param name="orders">订单端口。</param>
    /// <param name="logger">日志器。</param>
    public SyncRefundOrdersHandler(
        IRefundRepository refunds, IOrderPort orders, ILogger<SyncRefundOrdersHandler> logger)
    {
        _refunds = refunds;
        _orders = orders;
        _logger = logger;
    }

    /// <summary>执行重放。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>扫描 / 成功 / 失败统计。</returns>
    public async Task<ApiResponse<SyncRefundOrdersResult>> Handle(
        SyncRefundOrdersCommand request, CancellationToken ct)
    {
        var refunds = await _refunds
            .ListUnsyncedApprovedAsync(request.Limit, ct)
            .ConfigureAwait(false);

        if (refunds.Count == 0)
        {
            return ApiResults.Ok(new SyncRefundOrdersResult(0, 0, Array.Empty<string>()));
        }

        var synced = 0;
        var failed = new List<string>();

        foreach (var refund in refunds)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var items = await _refunds.ListItemsAsync(refund.Id, ct).ConfigureAwait(false);
                var snapshots = items
                    .Select(a => new RefundLineSnapshot(a.OrderItemId, a.Quantity, a.Amount))
                    .ToList();

                var ok = await _orders
                    .MarkRefundedAsync(refund.OrderNo, refund.Amount, refund.RefundNo, snapshots, ct)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    failed.Add($"{refund.RefundNo}（订单侧返回失败）");
                    continue;
                }

                await _refunds.MarkOrderSyncedAsync(refund.Id, ct).ConfigureAwait(false);
                synced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 逐条 try：一笔重放失败不能拖垮整轮，下一轮还会再扫到它
                _logger.LogError(ex, "退款单 {RefundNo} 重放订单侧落账失败", refund.RefundNo);
                failed.Add($"{refund.RefundNo}（异常）");
            }
        }

        var message = failed.Count == 0
            ? $"扫描 {refunds.Count} 笔待同步退款，全部同步成功"
            : $"扫描 {refunds.Count} 笔待同步退款，成功 {synced} 笔，{failed.Count} 笔仍失败";

        return ApiResults.Ok(new SyncRefundOrdersResult(refunds.Count, synced, failed), message);
    }
}
