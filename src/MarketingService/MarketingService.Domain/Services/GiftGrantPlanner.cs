using MarketingService.Domain.Entities;
using MarketingService.Domain.IRepository;

namespace MarketingService.Domain.Services;

/// <summary>满赠发放承诺的判定。</summary>
/// <remarks>
/// <para><b>纯函数</b>：只决定「这单该承诺送什么」，不碰数据库，所以可以直接单元测试。
/// 判定必须发生在<b>下单当时</b>：活动时间窗、门槛、赠送张数都按那一刻的口径定下来，
/// 支付时再重算的话，活动一旦被改或过期，用户就会「下单页写着送券、付完钱没有」。</para>
/// </remarks>
public static class GiftGrantPlanner
{
    /// <summary>把优惠引擎判定命中的满赠活动实体找回来。</summary>
    /// <param name="activities">候选活动（查询层已按平台与场次滤过）。</param>
    /// <param name="giftActivityId">
    /// 优惠引擎给出的满赠活动 Id（<see cref="PromotionCalculator.Calculate"/> 的
    /// <c>GiftActivityId</c>）；0 表示没命中。
    /// </param>
    /// <returns>命中的满赠活动；没有则返回 null。</returns>
    /// <remarks>
    /// <b>为什么由引擎给出活动 Id，而不是这里再判一次</b>：满赠只在「该行没有任何折扣可用」
    /// 时才命中（BUSINESS.md 11.2），而「哪一行用了哪个活动 / 券」是逐行贪心的结论，
    /// 只有算过整单优惠的人知道。这里再算一遍就会出现「引擎说命中满减、发券说命中满赠」
    /// 这种两套判定漂移。所以判定只做一次，这个方法只负责把实体查出来。
    /// </remarks>
    public static PromotionActivity? FindHitGiftActivity(
        IReadOnlyList<PromotionActivity> activities, long giftActivityId)
        => giftActivityId <= 0 ? null : activities.FirstOrDefault(a => a.Id == giftActivityId);

    /// <summary>这张赠送模板还能不能再承诺一次。</summary>
    /// <param name="template">赠送的券模板；查不到（已删除）时为 null。</param>
    /// <param name="quantity">本次要送的张数。</param>
    /// <returns>还能承诺返回 true。</returns>
    /// <remarks>
    /// 池子在<b>承诺时</b>判：券发不出去就不该向用户承诺。
    /// <c>TotalQuantity = 0</c> 表示不限量（DATA_SPEC 5.12）。
    /// 模板被停用不拦——停用的语义是「不能再领」，而下单时它还是启用的，
    /// 承诺已经作出，付完钱就该兑现。
    /// </remarks>
    public static bool CanPromise(CouponTemplate? template, int quantity)
    {
        if (template is null || quantity <= 0) return false;
        if (template.TotalQuantity <= 0) return true;

        return template.TotalQuantity - template.IssuedQuantity >= quantity;
    }
}
