using OrderService.Domain.Entities;
using OrderService.Domain.Ports;
using OrderService.Domain.Services;

namespace OrderService.Application.Features.Orders;

/// <summary>订单详情组装器：C 端与后台共用同一份。</summary>
/// <remarks>
/// 为什么抽出来而不是各写一份：详情页的后台版与 C 端版字段完全相同，
/// 复制一份之后改一处忘一处，表现为「后台少显示优惠构成、C 端多显示一行」，
/// 运营会以为数据错了。两边共用一份就没有这个问题。
/// </remarks>
public static class OrderDetailAssembler
{
    /// <summary>用已查出的订单与订单行组装详情。</summary>
    /// <param name="order">订单实体。</param>
    /// <param name="items">订单行。</param>
    /// <param name="imagesBySku">SKU Id → 商品图；可选，取不到时订单行图片为空串。</param>
    /// <returns>详情 DTO。</returns>
    public static OrderDetailDto Build(
        Order order, IReadOnlyCollection<OrderItem> items,
        IReadOnlyDictionary<long, string>? imagesBySku = null)
        => new(
            order.Id, order.OrderNo, order.Status, OrderStatusMachine.NameOf(order.Status),
            OrderStatusMachine.CanCancel(order.Status),
            OrderStatusMachine.CanConfirmReceipt(order.Status),
            order.GoodsTotal, order.Freight, order.PointsDeduction, order.PayableAmount,
            order.PointsUsed, order.CouponId, order.CouponDiscount,
            order.ReceiverName, order.ReceiverPhone, order.ReceiverAddress,
            order.Remark, order.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            items.Select(a => new OrderItemDto(
                a.Id, a.SkuId, a.SpuId, a.ProductName, a.SkuSpecText,
                a.Price, a.Quantity, a.OriginalAmount,
                a.ActivityDiscount, a.CouponDiscount, a.PayableAmount, a.DeliveryType,
                a.SourceType,
                Image: imagesBySku?.GetValueOrDefault(a.SkuId) ?? string.Empty)).ToList(),
            order.LogisticsCompanyId,
            order.LogisticsCompanyName,
            order.TrackingNo,
            // 未发货时给空串而不是 null：DTO 里所有字符串字段都是非空约定，
            // 前端直接显示即可，少写一处 ?. 判断。
            order.ShippedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
            order.RefundedAmount,
            // 剩余可退给到分位就当 0：差 0.004 元还提示「还能退 0.00 元」很奇怪
            Math.Max(0m, decimal.Round(
                order.PayableAmount - order.RefundedAmount, 2, MidpointRounding.AwayFromZero)),
            // 活动优惠合计由行汇总：金额恒等式里它与行优惠是一回事，
            // 单独存一个整单字段是为了让详情页能直接显示「省了多少」，
            // 而不必让前端自己把行加一遍。
            decimal.Round(
                items.Sum(a => a.ActivityDiscount), 2, MidpointRounding.AwayFromZero),
            // 发货内容：虚拟商品是卡号 / 激活码（客户凭它收货），快递是发货备注。
            // 未发货为空串，与其它字符串字段的非空约定一致。
            order.ShipRemark,
            PickupCode: "",
            // 与订单列表使用同一条退款窗口规则，避免列表有入口、详情却没有。
            OrderStatusMachine.CanRefund(order.Status, items.Select(a => a.DeliveryType)));

    /// <summary>按订单状态与配送方式解析自提取货码。</summary>
    /// <param name="order">订单实体。</param>
    /// <param name="items">订单行。</param>
    /// <param name="codec">RSA 取货码编解码器。</param>
    /// <returns>待取货且含自提行时返回取货码，其余情况返回空串。</returns>
    /// <remarks>
    /// 只在「待取货」下发：其它状态要么还没备货（客户拿到码也提不了货），
    /// 要么已经核销（码已失效）。每次调用重新加密是刻意的 ——
    /// PKCS#1 带随机填充，同一订单号每次密文不同，但都能解回订单号，不影响核销。
    /// </remarks>
    public static string ResolvePickupCode(
        Order order, IReadOnlyCollection<OrderItem> items, IPickupCodeCodec codec)
        => order.Status == OrderStatuses.PendingPickup
           && items.Any(a => a.DeliveryType == DeliveryTypes.SelfPickup)
            ? codec.Encrypt(order.OrderNo)
            : string.Empty;
}
