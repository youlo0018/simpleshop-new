using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.IRepository;
using PaymentService.Domain.Services;

namespace PaymentService.Application.Features.Payment;

/// <summary>把「订单已支付、支付单还挂在待支付」的单子补成已支付（对账任务调用）。</summary>
/// <param name="Limit">单轮最多检查多少条，1 ~ 200。</param>
public record ReconcilePendingPaymentsCommand(int Limit = 20)
    : IRequest<ApiResponse<ReconcilePendingPaymentsResult>>;

/// <summary>对账结果。</summary>
/// <param name="Scanned">本轮检查的支付单数。</param>
/// <param name="Healed">补成已支付的条数。</param>
/// <param name="Skipped">订单确实还没支付的条数（正常情况）。</param>
/// <param name="Failed">对账时出错的清单（支付单号与原因）。</param>
public sealed record ReconcilePendingPaymentsResult(
    int Scanned, int Healed, int Skipped, IReadOnlyList<string> Failed);

/// <summary>对账命令校验。</summary>
public sealed class ReconcilePendingPaymentsValidator : AbstractValidator<ReconcilePendingPaymentsCommand>
{
    /// <summary>构造校验器。</summary>
    public ReconcilePendingPaymentsValidator()
        => RuleFor(x => x.Limit).InclusiveBetween(1, 200).WithMessage("单轮处理数量需在 1 ~ 200 之间");
}

/// <summary>支付单与订单状态的对账处理器。</summary>
/// <remarks>
/// <para>支付收尾是「先让订单侧置已支付，再改支付单状态」（见 <see cref="PaymentExecutor"/>）。
/// 第二步失败时订单已经付了、支付单还挂在待支付：后台支付列表与报表都会把它当成没收钱，
/// 而用户再次点支付会被「只有待支付的订单可以支付」挡住。</para>
///
/// <para>本任务逐条回查订单状态，发现「订单已在已支付之后的状态」就把支付单补成已支付。
/// 判据只看订单状态，不看金额——金额在收尾时已经由订单侧权威校验过。</para>
/// </remarks>
public sealed class ReconcilePendingPaymentsHandler
    : IRequestHandler<ReconcilePendingPaymentsCommand, ApiResponse<ReconcilePendingPaymentsResult>>
{
    private readonly IPaymentRepository _payments;
    private readonly IOrderPort _orders;
    private readonly ILogger<ReconcilePendingPaymentsHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="payments">支付仓储。</param>
    /// <param name="orders">订单端口。</param>
    /// <param name="logger">日志器。</param>
    public ReconcilePendingPaymentsHandler(
        IPaymentRepository payments, IOrderPort orders,
        ILogger<ReconcilePendingPaymentsHandler> logger)
    {
        _payments = payments;
        _orders = orders;
        _logger = logger;
    }

    /// <summary>执行对账。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>检查 / 补单 / 跳过 / 失败统计。</returns>
    public async Task<ApiResponse<ReconcilePendingPaymentsResult>> Handle(
        ReconcilePendingPaymentsCommand request, CancellationToken ct)
    {
        var pending = await _payments
            .ListPendingForReconcileAsync(request.Limit, ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return ApiResults.Ok(new ReconcilePendingPaymentsResult(0, 0, 0, Array.Empty<string>()));
        }

        var healed = 0;
        var skipped = 0;
        var failed = new List<string>();

        foreach (var payment in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var order = await _orders.GetForPaymentAsync(payment.OrderNo, ct).ConfigureAwait(false);
                if (order is null)
                {
                    // 订单查不到：可能是历史脏数据，跳过并留痕，下轮还会再查
                    skipped++;
                    continue;
                }

                var paidAfterOrder = order.Status is OrderStatusNumbers.PendingShipment
                    or OrderStatusNumbers.PendingReceipt
                    or OrderStatusNumbers.PendingPickup
                    or OrderStatusNumbers.Completed;

                if (!paidAfterOrder)
                {
                    // 订单确实还没支付：正常情况，跳过
                    skipped++;
                    continue;
                }

                var affected = await _payments.TryChangeStatusAsync(
                    payment.Id, PaymentStatuses.Pending, PaymentStatuses.Paid,
                    DateTime.UtcNow, string.Empty, ct).ConfigureAwait(false);

                if (affected > 0)
                {
                    healed++;
                    _logger.LogWarning(
                        "对账补单：订单 {OrderNo} 早已支付，支付单 {PaymentNo} 被补成已支付",
                        payment.OrderNo, payment.PaymentNo);
                }
                else
                {
                    // 条件更新没命中：状态被并发改过（多半已经变成已支付），按已处理跳过
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 逐条 try：一条查询失败不能拖垮整轮
                _logger.LogError(ex, "支付单 {PaymentNo} 对账失败", payment.PaymentNo);
                failed.Add($"{payment.PaymentNo}（异常）");
            }
        }

        var message = healed == 0 && failed.Count == 0
            ? $"检查 {pending.Count} 笔待支付单，没有需要补单的"
            : $"检查 {pending.Count} 笔待支付单，补成已支付 {healed} 笔"
                + (failed.Count > 0 ? $"，{failed.Count} 笔对账失败" : string.Empty);

        return ApiResults.Ok(
            new ReconcilePendingPaymentsResult(pending.Count, healed, skipped, failed), message);
    }
}
