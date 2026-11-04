using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using OrderService.Domain.Ports;

namespace OrderService.Application.Features.Internal;

/// <summary>重放实付 0 元订单的占用结清（ScheduledService 补偿任务调用）。</summary>
/// <param name="Limit">单轮最多处理多少张，1 ~ 500。</param>
public record SettleZeroPayOrdersCommand(int Limit = 50)
    : IRequest<ApiResponse<SettleZeroPayResult>>;

/// <summary>补偿结果。</summary>
/// <param name="Scanned">本轮扫描到的待结清订单数。</param>
/// <param name="Settled">本轮结清成功的订单数。</param>
/// <param name="Failed">失败清单（订单号与原因）。</param>
public sealed record SettleZeroPayResult(int Scanned, int Settled, IReadOnlyList<string> Failed);

/// <summary>命令校验器。</summary>
public sealed class SettleZeroPayOrdersValidator : AbstractValidator<SettleZeroPayOrdersCommand>
{
    /// <summary>构造校验器。</summary>
    public SettleZeroPayOrdersValidator()
        => RuleFor(x => x.Limit).InclusiveBetween(1, 500).WithMessage("单轮处理数量需在 1 ~ 500 之间");
}

/// <summary>实付 0 元订单的占用结清补偿处理器。</summary>
/// <remarks>
/// <para><b>为什么需要它</b>：实付 0 元的单没有支付这一步，创建时直接进 20 待发货，
/// 但库存仍锁着、积分仍冻着、券仍占着 —— 这些占用靠下单后当场调
/// <see cref="OrderPaymentCompleter"/> 结清。任何一步下游抖动（库存服务 503、
/// 积分服务超时）都会让占用永远留着：订单在用户眼里已经「待发货」，
/// 孤儿对账也因为「订单存在」不会释放，只能靠这里重放。</para>
///
/// <para><b>重放安全</b>：<see cref="OrderPaymentCompleter"/> 的每一步下游调用
/// 都按订单号拼出的 bizNo 幂等（库存 (bizNo, skuId, action) 唯一、
/// 积分与券按 bizNo 幂等），所以对同一张单重复调用不会重复扣减。</para>
/// </remarks>
public sealed class SettleZeroPayOrdersHandler
    : IRequestHandler<SettleZeroPayOrdersCommand, ApiResponse<SettleZeroPayResult>>
{
    private readonly IOrderStore _store;
    private readonly OrderPaymentCompleter _completer;
    private readonly ILogger<SettleZeroPayOrdersHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="store">订单存储端口。</param>
    /// <param name="completer">支付收尾服务，与真实支付走同一段代码。</param>
    /// <param name="logger">日志器。</param>
    public SettleZeroPayOrdersHandler(
        IOrderStore store, OrderPaymentCompleter completer, ILogger<SettleZeroPayOrdersHandler> logger)
    {
        _store = store;
        _completer = completer;
        _logger = logger;
    }

    /// <summary>执行补偿。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>扫描 / 成功 / 失败统计。</returns>
    public async Task<ApiResponse<SettleZeroPayResult>> Handle(
        SettleZeroPayOrdersCommand request, CancellationToken ct)
    {
        var orders = await _store
            .ListPendingZeroPaySettlementsAsync(request.Limit, ct)
            .ConfigureAwait(false);

        if (orders.Count == 0)
        {
            return ApiResults.Ok(new SettleZeroPayResult(0, 0, Array.Empty<string>()));
        }

        var settled = 0;
        var failed = new List<string>();

        foreach (var order in orders)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var outcome = await _completer.CompleteAsync(order, ct).ConfigureAwait(false);
                if (!outcome.Succeeded)
                {
                    _logger.LogWarning(
                        "0 元订单 {OrderNo} 结清重试失败（步骤 {Step}）：{Error}",
                        order.OrderNo, outcome.FailedStep, outcome.Error);
                    failed.Add($"{order.OrderNo}（步骤 {outcome.FailedStep}：{outcome.Error}）");
                    continue;
                }

                await _store.MarkSettledAsync(order.Id, ct).ConfigureAwait(false);
                settled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 逐个 try：一张单的下游异常不能拖垮整轮，下一轮还会再扫到它
                _logger.LogError(ex, "0 元订单 {OrderNo} 结清重试异常", order.OrderNo);
                failed.Add($"{order.OrderNo}（异常）");
            }
        }

        var message = failed.Count == 0
            ? $"扫描 {orders.Count} 张 0 元订单，结清 {settled} 张"
            : $"扫描 {orders.Count} 张 0 元订单，结清 {settled} 张，{failed.Count} 张仍失败待下轮重试";

        return ApiResults.Ok(new SettleZeroPayResult(orders.Count, settled, failed), message);
    }
}
