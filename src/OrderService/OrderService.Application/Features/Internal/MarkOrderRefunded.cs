using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Domain.Entities;
using OrderService.Domain.Ports;
using OrderService.Domain.Services;

namespace OrderService.Application.Features.Internal;

/// <summary>把订单标记为已退款（PaymentService 审批通过后调用）。</summary>
/// <param name="OrderNo">订单号。</param>
/// <param name="RefundAmount">本次退款金额。</param>
/// <param name="RefundNo">
/// 支付服务侧的退款单号。订单侧台账用它做退款单号（有唯一索引）——
/// 同一订单会有多次部分退款，固定用订单号拼单号会在第二次撞唯一键。
/// 留空时回退成 <c>PR{OrderNo}</c>，只用于兼容历史调用。
/// </param>
/// <param name="Items">
/// 本次退款的行明细。<b>不能省</b>：订单侧的「行级可退余额」就是靠它算的
/// （后台订单详情与再次退款的累计校验都读这张表）。为空时按「整单退」处理。
/// </param>
public record MarkOrderRefundedCommand(
    string OrderNo, decimal RefundAmount, string RefundNo = "",
    IReadOnlyList<MarkOrderRefundedItem>? Items = null)
    : IRequest<ApiResponse>;

/// <summary>退款的一行。</summary>
/// <param name="OrderItemId">订单行 Id。</param>
/// <param name="Quantity">本次退的件数。</param>
/// <param name="Amount">该行本次退款金额，两位小数。</param>
public record MarkOrderRefundedItem(long OrderItemId, int Quantity, decimal Amount);

/// <summary>命令校验器注册。</summary>
public static class MarkOrderRefundedValidators
{
    /// <summary>注册校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddMarkOrderRefundedValidators(IServiceCollection services)
        => services.AddScoped<IValidator<MarkOrderRefundedCommand>, MarkOrderRefundedValidator>();

    /// <summary>校验规则。</summary>
    private sealed class MarkOrderRefundedValidator : AbstractValidator<MarkOrderRefundedCommand>
    {
        /// <summary>构造校验器。</summary>
        public MarkOrderRefundedValidator()
        {
            RuleFor(x => x.OrderNo).NotEmpty().MaximumLength(64).WithMessage("缺少订单号");
            RuleFor(x => x.RefundNo).MaximumLength(64).WithMessage("退款单号不正确");
            RuleFor(x => x.RefundAmount).GreaterThan(0m).WithMessage("退款金额必须大于 0");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(a => a.OrderItemId).GreaterThanOrEqualTo(0).WithMessage("订单行信息不正确");
                item.RuleFor(a => a.Quantity).GreaterThanOrEqualTo(0).WithMessage("退款件数不能为负数");
                item.RuleFor(a => a.Amount).GreaterThan(0m).WithMessage("行退款金额必须大于 0");
            });
        }
    }
}

/// <summary>标记订单已退款的处理器。</summary>
public sealed class MarkOrderRefundedHandler : IRequestHandler<MarkOrderRefundedCommand, ApiResponse>
{
    private readonly IOrderStore _store;
    private readonly IInventoryPort _inventory;
    private readonly IPointPort _points;
    private readonly ISeckillPort _seckill;
    private readonly ILogger<MarkOrderRefundedHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="store">订单存储端口。</param>
    /// <param name="inventory">库存端口（退款回补）。</param>
    /// <param name="points">积分端口（退款按比例回收）。</param>
    /// <param name="seckill">秒杀端口（秒杀单的货要还回秒杀池）。</param>
    /// <param name="logger">日志器。</param>
    public MarkOrderRefundedHandler(
        IOrderStore store,
        IInventoryPort inventory,
        IPointPort points,
        ISeckillPort seckill,
        ILogger<MarkOrderRefundedHandler> logger)
    {
        _store = store;
        _inventory = inventory;
        _points = points;
        _seckill = seckill;
        _logger = logger;
    }

    /// <summary>执行标记。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    /// <remarks>
    /// <para><b>这里必须做库存回补与积分回收</b>。BUSINESS.md 10.2 写得很明确：
    /// 「审批通过 → 退款单已退款 → 发 payment.refunded → 订单转 60 +
    /// <b>库存回补</b> + <b>积分回收</b>」。</para>
    ///
    /// <para>🔴 之前这里**只改了状态**：两段式退款（/refunds/Apply + Approve，
    /// 也就是规格里那套带审批的正式流程）走完之后，货永久从库存里消失、
    /// 积分也不会还。实测：下单 2 件 → 支付 → 申请 → 审批通过，
    /// available 停在 98、deducted 停在 2，而订单已经显示「已退款」。
    /// 订单服务另有一条 /admin/orders/Refund 的单步退款做了回补，
    /// 但后台的退款对话框走的是两段式那条 —— 规则又一次被写在了两条路径里。</para>
    ///
    /// <para>回补用的是与单步退款相同的 bizNo（订单号 + SKU），库存侧按它幂等，
    /// 所以重试不会把货还两遍。</para>
    /// </remarks>
    public async Task<ApiResponse> Handle(MarkOrderRefundedCommand request, CancellationToken ct)
    {
        var order = await _store.FindByOrderNoAsync(request.OrderNo.Trim(), ct).ConfigureAwait(false);
        if (order is null) return ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "订单不存在");

        // 🔴 0 元单的占用还没结清时不能退款：此时库存还占着 locked、积分还冻着，
        // 退款侧却会按「状态 20 = 已扣库存」去 replenish（回补必然失败），
        // 积分也会在冻结状态下被「回收」而重复计入可用余额。
        // 结清由 ScheduledService 的 zero_pay_settle 任务自动重试，等它跑完再退。
        if (order.PayableAmount == 0m && order.SettleStatus == 0)
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.BusinessError,
                "该订单的占用尚未结清（0 元单由系统自动结清），请稍后重试退款");
        }

        var items = await _store.ListItemsAsync(order.Id, ct).ConfigureAwait(false);

        // 只有**未支付**（10）的单还占着 locked；支付成功后库存已从 locked 变成 deducted。
        // 两段式退款只可能发生在 20 及以后，所以这里基本都是 replenish，
        // 但仍然按状态判定，免得将来放宽窗口时踩到同一个坑。
        var lockedPhase = order.Status == OrderStatuses.PendingPayment;

        var priorRefunds = await _store.ListRefundsAsync(order.Id, ct).ConfigureAwait(false);
        var refundNo = string.IsNullOrWhiteSpace(request.RefundNo) ? string.Empty : request.RefundNo.Trim();

        // 🔴 幂等重放：同一退款单号的台账已经落过账，说明上一次调用至少完成了
        // 「台账 + 已退金额」这一步，只是在后续副作用（库存 / 积分）前中断了
        // （退款对账任务会把失败的那次重放过来）。这里把副作用按台账明细补跑一遍：
        // 库存回补按「订单号:SKU:退款单号」幂等、积分回收按退款单号幂等，重放不会做两遍。
        (OrderRefund Refund, IReadOnlyList<OrderRefundItem> Items)? existingRefund = null;
        if (refundNo.Length > 0)
        {
            foreach (var candidate in priorRefunds)
            {
                if (string.Equals(candidate.Refund.RefundNo, refundNo, StringComparison.Ordinal))
                {
                    existingRefund = candidate;
                    break;
                }
            }
        }

        if (existingRefund is not null)
        {
            var replayLines = existingRefund.Value.Items
                .Select(a => new MarkOrderRefundedItem(a.OrderItemId, a.Quantity, a.Amount))
                .ToList();

            await ApplySideEffectsAsync(order, items, replayLines, refundNo, request.RefundAmount, lockedPhase, ct)
                .ConfigureAwait(false);

            return ApiResponseFactory.Ok("该退款已入账（幂等重放）");
        }

        if (order.Status == OrderStatuses.Refunded)
        {
            // 幂等：已经退过直接成功。退款审批可能被重复调用（重试、运营多点一次），
            // 报错会让上游以为失败而反复重试
            return ApiResponseFactory.Ok("订单已是已退款状态");
        }

        var alreadyRefunded = await _store.AggregateRefundedItemsAsync(order.Id, ct).ConfigureAwait(false);

        // ---- 解析本次要退的行与件数 ----
        // 🔴 件数必须来自请求明细，不能拿「整行数量」顶替：
        // 买了 3 件退 1 件时按整行回补会把 3 件全部放回库存，直接超卖。
        // 金额只用来算钱，件数才决定回补多少货 —— 两者是独立的两个口径。
        var refundItems = new List<MarkOrderRefundedItem>();

        if (request.Items is { Count: > 0 })
        {
            foreach (var input in request.Items)
            {
                var line = items.FirstOrDefault(b => b.Id == input.OrderItemId);
                if (line is null)
                {
                    return ApiResponseFactory.Fail(
                        BaseApiResponseCode.BadRequest, $"订单行 {input.OrderItemId} 不属于该订单");
                }

                if (input.Amount <= 0m) continue;
                if (input.Quantity < 0)
                {
                    return ApiResponseFactory.Fail(
                        BaseApiResponseCode.BusinessError, "退款件数不能为负数");
                }

                alreadyRefunded.TryGetValue(line.Id, out var already);
                var remainingQuantity = Math.Max(0, line.Quantity - already.Quantity);
                var remainingAmount = Math.Max(0m, line.PayableAmount - already.Amount);

                if (input.Quantity > remainingQuantity)
                {
                    return ApiResponseFactory.Fail(
                        BaseApiResponseCode.BusinessError,
                        $"「{line.ProductName}」本次退 {input.Quantity} 件超过还可退 {remainingQuantity} 件");
                }

                if (input.Amount > remainingAmount)
                {
                    return ApiResponseFactory.Fail(
                        BaseApiResponseCode.BusinessError,
                        $"「{line.ProductName}」退款金额超过本行可退余额 {remainingAmount:0.00} 元");
                }

                refundItems.Add(new MarkOrderRefundedItem(
                    line.Id, input.Quantity, OrderRefundRules.Round2(input.Amount)));
            }
        }
        else
        {
            // 没带明细就按整单退处理：每行按「行实付 − 已退」与「行数量 − 已退件数」记一遍
            refundItems = items
                .Select(item =>
                {
                    alreadyRefunded.TryGetValue(item.Id, out var already);
                    return new MarkOrderRefundedItem(
                        item.Id,
                        Math.Max(0, item.Quantity - already.Quantity),
                        Math.Max(0m, item.PayableAmount - already.Amount));
                })
                .Where(a => a.Amount > 0m)
                .ToList();
        }

        // 台账金额 = 商品行退款额合计；整单退含运费时，差额就是运费部分。
        // 商品行都退完、只剩运费（或优惠分摊零头）时记一条伪行，
        // 台账才有金额可显示 —— 与单步退款的 OrderItemId = 0 约定一致。
        var ledgerAmount = OrderRefundRules.Round2(refundItems.Sum(a => a.Amount));
        if (refundItems.Count == 0 && request.RefundAmount > 0m)
        {
            refundItems.Add(new MarkOrderRefundedItem(0, 0, OrderRefundRules.Round2(request.RefundAmount)));
        }

        if (refundItems.Count == 0 || refundItems.Sum(a => a.Amount) <= 0m)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.BusinessError, "该订单没有可退的行");
        }

        ledgerAmount = OrderRefundRules.Round2(refundItems.Sum(a => a.Amount));

        // 只有**累计退满**才把订单转 60。
        // 部分退款把整单标成「已退款」的话，剩下的钱客户再也退不了 ——
        // BUSINESS 10.2 明确支持按行部分退，单步退款那条路径也是这么做的
        // （只退一行时订单保持原状态）。两段式这条以前无条件转 60，
        // 于是「退了一件」等于「整单作废」。
        // 累计已退金额取**退款单主表的金额合计**，不是行明细合计 ——
        // 整单退含运费，而行明细只记商品金额（规格 10.2：整单退含运费、部分退不退运费）。
        // 用行明细去比实付，一笔含运费的整单退（商品 51 + 运费 10 = 61）永远比不满 61，
        // 订单就永远转不成已退款。
        var priorRefunded = priorRefunds.Sum(a => a.Refund.Amount);

        var fullyRefunded = OrderRefundRules.Round2(priorRefunded + request.RefundAmount) >= order.PayableAmount;

        // 台账里的金额口径与单步退款一致：主表只记商品行明细合计（不含运费），
        // 运费仍体现在订单级 refunded_amount 上。
        var ledger = new OrderRefund
        {
            // 🔴 退款单号必须用**支付服务的退款单号**：订单侧原来固定写
            // `PR{orderNo}`，而 order_refund.refund_no 有唯一索引 ——
            // 同一订单的第二次部分退款必然撞唯一键，导致「支付侧已退款、
            // 订单侧余额与台账没更新」，之后还能基于旧余额再退一次。
            RefundNo = string.IsNullOrWhiteSpace(request.RefundNo)
                ? $"PR{order.OrderNo}"
                : request.RefundNo.Trim(),
            OrderId = order.Id,
            OrderNo = order.OrderNo,
            PlatformId = order.PlatformId,
            MerchantId = order.MerchantId,
            CustomerId = order.CustomerId,
            Amount = ledgerAmount,
            // 与单步退款同一个判据：退满剩余余额才算整单退，否则是部分退。
            RefundType = fullyRefunded ? OrderRefundTypes.Whole : OrderRefundTypes.Partial,
            FullyRefunded = fullyRefunded,
            Reason = "支付服务审批通过的退款",
            OperatorId = 0,
            OperatorName = "payment-service",
        };

        var ledgerItems = refundItems
            .Select(a =>
            {
                var line = items.FirstOrDefault(b => b.Id == a.OrderItemId);
                return new OrderRefundItem
                {
                    OrderItemId = line?.Id ?? 0,
                    SkuId = line?.SkuId ?? 0,
                    ProductName = line?.ProductName ?? "运费与优惠分摊",
                    SkuSpecText = line?.SkuSpecText ?? string.Empty,
                    Quantity = a.Quantity,
                    Amount = OrderRefundRules.Round2(a.Amount),
                };
            })
            .ToList();

        // 🔴 台账与订单余额同事务写：余额条件更新命中 0 行时台账一起回滚，
        // 不会留下「钱没退、账上却记了一笔」的幽灵记录。
        // 顺序也必须是「先落账（事务内）→ 再做库存 / 积分副作用」，避免余额没生效却先放了货。
        var applied = await _store.ApplyRefundWithLedgerAsync(
            order.Id, order.Status, request.RefundAmount, fullyRefunded, ledger, ledgerItems, ct)
            .ConfigureAwait(false);

        if (!applied)
        {
            _logger.LogWarning(
                "订单 {OrderNo} 的退款未生效（可退余额不足或状态已变），退款单已审批但订单未更新",
                order.OrderNo);

            return ApiResponseFactory.Fail(
                BaseApiResponseCode.OrderStateInvalid, "订单可退余额不足或状态已变更，请刷新后重试");
        }

        // ---- 副作用：按**本次退的件数**回补库存 + 按比例回收积分 ----
        // 台账已落、余额已扣；副作用失败只能记 Error（抛错会让上游以为没退而重复退款）。
        // 幂等重放时同一条路径会被再走一遍，所以每个副作用都必须按退款单号幂等。
        await ApplySideEffectsAsync(
            order, items, refundItems, ledger.RefundNo, request.RefundAmount, lockedPhase, ct)
            .ConfigureAwait(false);

        if (!fullyRefunded)
        {
            _logger.LogInformation(
                "订单 {OrderNo} 部分退款 {Amount} 元已生效，订单保持 {Status}，剩余仍可退",
                order.OrderNo, request.RefundAmount, order.Status);

            return ApiResponseFactory.Ok("退款已生效（部分退款，订单仍可继续退）");
        }

        return ApiResponseFactory.Ok("订单已标记为已退款");
    }

    /// <summary>执行退款副作用：按件数回补库存、按比例回收积分。</summary>
    /// <param name="order">订单。</param>
    /// <param name="items">订单行。</param>
    /// <param name="refundLines">本次退款明细（幂等重放时取自台账）。</param>
    /// <param name="refundNo">退款单号，作为副作用的幂等键。</param>
    /// <param name="refundAmount">本次退款金额，用于算积分回收比例。</param>
    /// <param name="lockedPhase">true 表示未支付（货还占着 locked）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// 抽出来是为了让「首次退款」与「对账重放」走同一段代码：
    /// 重放时明细取自台账，件数 / 金额与首次完全一致，不会因为
    /// 订单行余额已经被扣过而算出不同的回补量。
    /// </remarks>
    private async Task ApplySideEffectsAsync(
        Order order, IReadOnlyList<OrderItem> items, IReadOnlyList<MarkOrderRefundedItem> refundLines,
        string refundNo, decimal refundAmount, bool lockedPhase, CancellationToken ct)
    {
        foreach (var refundLine in refundLines.Where(a => a.OrderItemId != 0 && a.Quantity > 0))
        {
            var item = items.FirstOrDefault(a => a.Id == refundLine.OrderItemId);
            if (item is null) continue;

            try
            {
                // 秒杀行不看 lockedPhase：它的货在发布场次时就划走了，
                // 与常规池的 locked / deducted 都没有关系，只能减 sold_count 还回秒杀池。
                if (item.SourceType == OrderSourceTypes.Seckill)
                {
                    await _seckill.ReleaseGrabAsync(
                        order.CustomerId, item.SkuId, refundLine.Quantity, order.OrderNo, ct).ConfigureAwait(false);
                }
                else if (lockedPhase)
                {
                    // 🔴 bizNo 带退款单号：同一订单可以有多次部分退款，只用「订单号:SKU」
                    // 会被库存侧的 (BizNo, SkuId, Action) 幂等当成重复请求，
                    // 第二次之后的回补静默不生效（与单步退款同一条约束）。
                    await _inventory.ReleaseAsync(
                        item.SkuId, refundLine.Quantity, $"{order.OrderNo}:{item.SkuId}:{refundNo}", ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _inventory.ReplenishAsync(
                        item.SkuId, refundLine.Quantity, $"{order.OrderNo}:{item.SkuId}:{refundNo}", ct)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "退款回补库存失败：订单 {OrderNo} SKU {SkuId} 数量 {Quantity}",
                    order.OrderNo, item.SkuId, refundLine.Quantity);
            }
        }

        // 按比例回收已扣积分（BUSINESS.md 10.3）。整单退时比例是 1，等价于全额回收。
        // 幂等键带上退款单号：同一订单的多次部分退款要**各自累计**，
        // 只按订单号幂等会把第二次之后的部分退款全部当成重复请求丢掉。
        if (order.PointsUsed > 0 && order.PayableAmount > 0)
        {
            var ratio = Math.Clamp(refundAmount / order.PayableAmount, 0m, 1m);
            try
            {
                await _points.RecoverByRefundAsync(
                    order.CustomerId, order.OrderNo, refundNo, ratio, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "退款已生效但积分回收失败：订单 {OrderNo} 比例 {Ratio}，需人工补回收",
                    order.OrderNo, ratio);
            }
        }
    }
}
