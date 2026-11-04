using Collaboration.Domain.Common;
using System.Text.Json;
using MediatR;
using PaymentService.Application.Features.Payment;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.IRepository;
using PaymentService.Domain.Services;
using Microsoft.Extensions.Logging;

namespace PaymentService.Application.Features.Refund;

/// <summary>退款申请处理器（后台代客发起）。</summary>
public sealed class ApplyRefundHandler : IRequestHandler<ApplyRefundCommand, ApiResponse<long>>
{
    private readonly IRefundRepository _refunds;
    private readonly IOrderPort _orders;
    private readonly ILogger<ApplyRefundHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="refunds">退款仓储。</param>
    /// <param name="orders">订单服务端口。</param>
    /// <param name="logger">日志器。</param>
    public ApplyRefundHandler(IRefundRepository refunds, IOrderPort orders, ILogger<ApplyRefundHandler> logger)
    {
        _refunds = refunds;
        _orders = orders;
        _logger = logger;
    }

    /// <summary>执行申请。</summary>
    /// <param name="request">命令；Items 留空表示整单退。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>退款单 Id。</returns>
    public async Task<ApiResponse<long>> Handle(ApplyRefundCommand request, CancellationToken ct)
    {
        var orderNo = request.OrderNo.Trim();
        var order = await _orders.GetForPaymentAsync(orderNo, ct).ConfigureAwait(false);
        if (order is null)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.NotFound, "订单不存在或订单服务不可用");
        }

        // 归属校验必须在退款窗口之前：客户令牌下，别人的订单无论什么状态都只能看到 403，
        // 不能通过「状态不可退」的提示反推别人的订单状态。
        var ownershipError = PaymentOwnership.OwnershipError(order);
        if (ownershipError is not null)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.Forbidden, ownershipError);
        }

        // 配送方式取订单行的：虚拟与实物混在一单里时，按虚拟的严格窗口处理
        var deliveryTypes = order.Items.Select(a => a.DeliveryType).Distinct().ToList();
        var strictest = deliveryTypes.Contains(DeliveryTypeNumbers.Virtual)
            ? DeliveryTypeNumbers.Virtual
            : DeliveryTypeNumbers.Express;

        if (!RefundRules.CanRefund(order.Status, strictest))
        {
            return ApiResults.Fail<long>(
                BaseApiResponseCode.OrderStateInvalid,
                $"当前订单状态是「{order.StatusName}」，{RefundRules.WindowDescription(strictest)}");
        }

        // 申请时把「待审批」也算作已占用：否则同一订单能连开两张注定过不了的退款单，
        // 用户填完原因提交才发现余额不够，白填一遍
        var refundedItems = await _refunds.ListRefundedItemsAsync(orderNo, true, ct).ConfigureAwait(false);
        // 订单级余额用**主表金额合计**（含运费）：明细只记商品行金额，
        // 用它比实付会把整单退里那笔运费当成还没退，再退一次就多退一笔运费。
        var alreadyRefunded = await _refunds.SumRefundedAmountAsync(orderNo, true, ct).ConfigureAwait(false);

        var (isWhole, total, itemAmounts, resolveError) =
            ResolveItems(request.Items, order, refundedItems, alreadyRefunded);
        if (resolveError.Length > 0)
        {
            return ApiResults.Fail<long>(BaseApiResponseCode.BusinessError, resolveError);
        }

        if (!RefundRules.WithinRefundableBalance(total, order.PayableAmount, alreadyRefunded))
        {
            return ApiResults.Fail<long>(
                BaseApiResponseCode.BusinessError,
                $"退款金额 {total:0.00} 元超过剩余可退 {(order.PayableAmount - alreadyRefunded):0.00} 元");
        }

        var refund = new RefundOrder
        {
            RefundNo = BuildRefundNo(),
            OrderId = order.OrderId,
            OrderNo = order.OrderNo,
            CustomerId = order.CustomerId,
            PlatformId = order.PlatformId,
            MerchantId = order.MerchantId,
            Amount = total,
            RefundType = isWhole ? RefundTypes.Whole : RefundTypes.Partial,
            Status = RefundStatuses.PendingApproval,
            Reason = request.Reason.Trim(),
            Images = JsonSerializer.Serialize(request.Images ?? Array.Empty<string>())
        };

        var items = new List<RefundOrderItem>();
        foreach (var (orderItemId, quantity, amount) in itemAmounts)
        {
            var line = order.Items.First(a => a.OrderItemId == orderItemId);
            items.Add(new RefundOrderItem
            {
                OrderItemId = line.OrderItemId,
                SkuId = line.SkuId,
                ProductName = line.ProductName,
                SkuSpecText = line.SkuSpecText,
                // 🔴 记调用方声明的**本次退件数**，不是整行数量：
                // 订单侧按这个数回补库存，拿整行数量会让「买 3 退 1」放回 3 件。
                Quantity = quantity,
                Amount = RefundRules.Round2(amount)
            });
        }

        var refundId = await _refunds.InsertAsync(refund, items, ct).ConfigureAwait(false);
        _logger.LogInformation("退款单 {RefundNo} 已创建，金额 {Amount}（{Type}）",
            refund.RefundNo, total, RefundTypes.NameOf(refund.RefundType));

        return ApiResults.Ok(refundId, "退款申请已提交，等待审批");
    }

    /// <summary>解析本次要退的行、件数与金额。</summary>
    /// <param name="inputs">调用方传入的行；空表示整单退。</param>
    /// <param name="order">订单信息。</param>
    /// <param name="refundedItems">该订单已退明细（含待审批）。</param>
    /// <param name="alreadyRefunded">该订单累计已退金额（主表口径，含运费）。</param>
    /// <returns>是否整单退、总金额、逐行（订单行 / 件数 / 金额）、错误信息。出错时 Error 非空。</returns>
    private static (bool IsWhole, decimal Total, List<(long ItemId, int Quantity, decimal Amount)> ItemAmounts, string Error)
        ResolveItems(
            IReadOnlyList<RefundItemInput>? inputs,
            Services.OrderForPayment order,
            IReadOnlyList<RefundOrderItem> refundedItems,
            decimal alreadyRefunded)
    {
        if (inputs is null || inputs.Count == 0)
        {
            // 整单退：每行退「行实付 − 已退」与「行数量 − 已退件数」，再加运费（规格 10.2）。
            // 原实现直接用行实付原值：先部分退一次、再整单退时，行金额会超过该行剩余可退，
            // 台账与行级余额当场写错。
            var result = new List<(long ItemId, int Quantity, decimal Amount)>();
            var goodsRemaining = 0m;

            foreach (var line in order.Items)
            {
                var lineRefunded = refundedItems.Where(a => a.OrderItemId == line.OrderItemId).ToList();
                var remainingAmount = RefundRules.Round2(
                    Math.Max(0m, line.PayableAmount - lineRefunded.Sum(a => a.Amount)));
                if (remainingAmount <= 0m) continue;

                var remainingQuantity = Math.Max(0, line.Quantity - lineRefunded.Sum(a => a.Quantity));
                result.Add((line.OrderItemId, remainingQuantity, remainingAmount));
                goodsRemaining += remainingAmount;
            }

            // 可退总额取「订单剩余余额」与「商品剩余 + 运费」的较小者：
            // 运费已经随上一次整单退退过之后，后者会偏大。
            var remainingOrder = RefundRules.Round2(order.PayableAmount - alreadyRefunded);
            var total = RefundRules.Round2(Math.Min(remainingOrder, goodsRemaining + order.Freight));
            if (total <= 0m)
            {
                // 常见原因就是「已经有一张待审批的整单退款申请」把余额占住了 ——
                // 这里必须挡住：同一订单连开两张注定过不了的退款单，用户填完才发现白填。
                return (true, 0m, [], "该订单没有可退余额（可能已申请过退款，或余额已退完）");
            }

            return (true, total, result, string.Empty);
        }

        var items = new List<(long ItemId, int Quantity, decimal Amount)>();
        foreach (var input in inputs)
        {
            var line = order.Items.FirstOrDefault(a => a.OrderItemId == input.OrderItemId);
            if (line is null)
            {
                return (false, 0m, [], $"订单行 {input.OrderItemId} 不属于该订单");
            }

            var lineRefunded = refundedItems
                .Where(a => a.OrderItemId == input.OrderItemId)
                .ToList();
            var refundedAmount = lineRefunded.Sum(a => a.Amount);

            if (!RefundRules.WithinLineBalance(input.Amount, line.PayableAmount, refundedAmount))
            {
                return (false, 0m, [],
                    $"「{line.ProductName}」本行可退余额不足，申请 {input.Amount:0.00} 元，" +
                    $"最多还能退 {(line.PayableAmount - refundedAmount):0.00} 元");
            }

            // 件数 0 = 只退钱不回补库存（价保 / 补偿场景）；件数 > 0 时不能超过还没退的件数。
            var remainingQuantity = Math.Max(0, line.Quantity - lineRefunded.Sum(a => a.Quantity));
            if (input.Quantity > remainingQuantity)
            {
                return (false, 0m, [],
                    $"「{line.ProductName}」本次退 {input.Quantity} 件超过还可退 {remainingQuantity} 件");
            }

            items.Add((input.OrderItemId, input.Quantity, RefundRules.Round2(input.Amount)));
        }

        return (false, RefundRules.Round2(items.Sum(a => a.Amount)), items, string.Empty);
    }

    /// <summary>生成退款单号：时间戳 + 6 位随机数。</summary>
    /// <returns>退款单号。</returns>
    private static string BuildRefundNo()
        => $"RFD{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100000, 1000000)}";
}
