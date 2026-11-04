using System.Text.Json;
using MarketingService.Domain.Entities;

namespace MarketingService.Domain.Services;

/// <summary>参与优惠计算的一行（只保留计算需要的字段）。</summary>
/// <param name="SpuId">SPU Id。</param>
/// <param name="SkuId">SKU Id。</param>
/// <param name="Amount">该行金额（单价 × 数量），已含数量。</param>
/// <param name="MerchantId">
/// 该行所属商户，0 表示平台自营 / 未知。
/// </param>
/// <remarks>
/// <b>为什么行上必须有商户</b>：活动分平台级与商户级。不带商户维度的话，
/// 一条商户级的活动会减到同平台**其它商户**的商品上 —— 跨商户改价。
/// 判据是 <c>activity.MerchantId &lt;= 0 || activity.MerchantId == line.MerchantId</c>：
/// 平台级活动（0）作用于所有行，商户级只作用于自己家的行。
/// </remarks>
public readonly record struct PromotionLine(long SpuId, long SkuId, decimal Amount, long MerchantId = 0);

/// <summary>一个活动对一行订单的试算结果。</summary>
/// <param name="ActivityId">命中的活动 Id，0 表示没命中。</param>
/// <param name="ActivityName">活动名，用于前端展示「满减」标签。</param>
/// <param name="DiscountAmount">优惠金额，两位小数。</param>
/// <param name="ThresholdBase">门槛判定基数（该活动的适用行金额合计）。</param>
/// <param name="ReachedThreshold">是否达到门槛。</param>
/// <param name="Applicable">该行是否落在活动适用范围内。</param>
/// <param name="IsGift">是否满赠（折扣额记 0）。</param>
/// <param name="Reason">未命中的原因，便于「为什么没享受活动」的提示。</param>
public readonly record struct ActivityQuote(
    long ActivityId,
    string ActivityName,
    decimal DiscountAmount,
    decimal ThresholdBase,
    bool ReachedThreshold,
    bool Applicable,
    bool IsGift,
    string Reason);

/// <summary>一行的最终优惠结果。</summary>
/// <param name="SpuId">SPU Id。</param>
/// <param name="SkuId">SKU Id。</param>
/// <param name="OriginalAmount">原价。</param>
/// <param name="ActivityDiscount">活动优惠额。</param>
/// <param name="CouponDiscount">分摊到该行的券优惠额。</param>
/// <param name="PayableAmount">到手价，<b>封底 0.01</b>。</param>
/// <param name="Source">优惠来源标签，见 <see cref="DiscountSources"/>。</param>
/// <param name="SourceName">来源名称（活动名或券码），用于角标文案。</param>
/// <param name="IsFloored">是否被 0.01 封底抬起来了。</param>
public readonly record struct PromotionLineResult(
    long SpuId,
    long SkuId,
    decimal OriginalAmount,
    decimal ActivityDiscount,
    decimal CouponDiscount,
    decimal PayableAmount,
    string Source,
    string SourceName,
    bool IsFloored);

/// <summary>整单到手价试算结果。</summary>
/// <param name="Lines">逐行结果。</param>
/// <param name="OriginalTotal">原价合计。</param>
/// <param name="ActivityDiscountTotal">活动优惠合计。</param>
/// <param name="CouponDiscountTotal">券优惠合计。</param>
/// <param name="FinalPrice">到手价合计。<b>不含运费</b>（BUSINESS.md 11.5）。</param>
/// <param name="CouponId">实际选中的券 Id，0 表示没用券。</param>
/// <param name="CouponCode">实际选中的券码。</param>
/// <param name="UsedActivity">是否命中了活动。</param>
/// <param name="ActivityHits">命中的活动汇总（含满赠），供活动报表与满赠承诺使用。</param>
/// <param name="GiftActivityId">命中的满赠活动 Id；没有命中时为 0。</param>
public readonly record struct FinalPriceResult(
    IReadOnlyList<PromotionLineResult> Lines,
    decimal OriginalTotal,
    decimal ActivityDiscountTotal,
    decimal CouponDiscountTotal,
    decimal FinalPrice,
    long CouponId,
    string CouponCode,
    bool UsedActivity,
    IReadOnlyList<ActivityHit> ActivityHits,
    long GiftActivityId);

/// <summary>命中的某个活动的汇总。</summary>
/// <param name="ActivityId">活动 Id。</param>
/// <param name="ActivityName">活动名。</param>
/// <param name="DiscountAmount">本次落到订单上的实际优惠额（满赠为 0）。</param>
public readonly record struct ActivityHit(long ActivityId, string ActivityName, decimal DiscountAmount);

/// <summary>活动计算。纯函数，不碰数据库，所以可以单元测试。</summary>
/// <remarks>
/// 规则集中在 BUSINESS.md 11.2 / 11.3，逐条对应下面的注释：
/// <list type="number">
/// <item><b>贪心 + 逐行独立</b>：每行单独算自己那份，互不影响。</item>
/// <item><b>多活动冲突</b>：优惠力度最大 → 门槛最高 → 创建最早，保证结果确定。</item>
/// <item><b>单行封底 0.01</b>：券与活动不能把某一行打成 0 元。</item>
/// <item><b>满赠折扣额记 0</b>，且只在没有任何折扣可用时才命中。</item>
/// <item><b>活动与券互斥</b>：落在券作用域内的行不再命中活动；作用域外的行照常参与活动。</item>
/// <item><b>平台优先级</b>：1 活动优先 / 2 券优先（默认）。</item>
/// </list>
/// </remarks>
public static class PromotionCalculator
{
    /// <summary>单行封底金额。券与活动都打不到 0。</summary>
    public const decimal LineFloor = 0.01m;

    /// <summary>金额舍入：两位小数、四舍五入（AwayFromZero）。</summary>
    /// <param name="value">原始金额。</param>
    /// <returns>两位小数的金额。</returns>
    public static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>算一个活动对一行订单的优惠。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="lines">整单的行（门槛基数是<b>适用行金额合计</b>）。</param>
    /// <param name="nowUtc">当前时间，用于过滤时间窗。</param>
    /// <returns>试算结果。</returns>
    public static ActivityQuote Quote(PromotionActivity activity, IReadOnlyList<PromotionLine> lines, DateTime nowUtc)
    {
        var applicable = ResolveApplicableAmount(activity, lines);

        // 时间窗与状态<b>不过滤也不参与计算</b>：查询层已经滤过一次，
        // 这里再滤一次是为了让纯函数单独测试时行为确定（不能依赖调用方先滤过）
        if (activity.Status != 1)
        {
            return Miss(activity, applicable, "活动已停用");
        }

        if (nowUtc < activity.StartTime || nowUtc > activity.EndTime)
        {
            return Miss(activity, applicable, "不在活动时间内");
        }

        if (!IsLineInScope(activity, lines))
        {
            return new ActivityQuote(activity.Id, activity.ActivityName, 0m, applicable, false, false, false, "该商品不在活动范围内");
        }

        if (activity.ActivityType == ActivityTypes.Gift)
        {
            // 满赠折扣额是 0，但门槛必须达到——达门槛送券、没达到什么都不送
            return applicable < activity.ThresholdAmount
                ? Miss(activity, applicable, $"未满 {activity.ThresholdAmount:0.00} 元")
                : new ActivityQuote(activity.Id, activity.ActivityName, 0m, applicable, true, true, true, "满赠活动");
        }

        if (applicable < activity.ThresholdAmount)
        {
            return Miss(activity, applicable, $"未满 {activity.ThresholdAmount:0.00} 元");
        }

        var discount = activity.ActivityType switch
        {
            ActivityTypes.FullReduction => Round2(activity.DiscountAmount),
            ActivityTypes.Discount => Round2(applicable * (10m - activity.DiscountRate) / 10m),
            _ => 0m
        };

        if (discount < 0m) discount = 0m;
        if (discount > applicable) discount = Round2(applicable);

        return new ActivityQuote(activity.Id, activity.ActivityName, discount, applicable, true, true, false, string.Empty);
    }

    /// <summary>整单到手价试算。</summary>
    /// <param name="lines">订单行。</param>
    /// <param name="activities">候选活动。</param>
    /// <param name="coupons">客户可用券；游客传空集合。</param>
    /// <param name="priority">平台优惠优先级，见 <see cref="MarketingPriorities"/>。</param>
    /// <param name="nowUtc">当前时间。</param>
    /// <returns>整单结果，含逐行拆分与命中的活动。</returns>
    /// <remarks>
    /// <para><b>逐订单行独立计算（BUSINESS.md 11.2）</b>：每一行单独挑对自己最有利的来源，
    /// 行与行互不影响。活动与券的优惠额都是「整单一个数」，再<b>按行金额比例分摊</b>到
    /// 各自覆盖的行上——不是每行各减一次。「满 100 减 20」遇到三行各 50 元，
    /// 正确结果是三行共减 20 而不是各减 20。</para>
    ///
    /// <para><b>券与活动互斥（11.3）</b>：券是订单级唯一一张，作用于它适用范围
    /// （全场 / SPU / SKU）内的行；券覆盖的行不再命中活动，作用域外的行照常参与活动。
    /// 券优先（默认）时先落券、再算作用域外的活动；活动优先时先算活动，
    /// 没命中活动的券覆盖行才用券，并按这些行重算券门槛。</para>
    ///
    /// <para><b>活动池与门槛</b>：券优先时券覆盖的行不参与活动、也不计入活动的门槛基数。
    /// 各行的活动资格互相独立——A 用券不会替 B 凑满减门槛，也不会把 B 的满减整个压掉。</para>
    ///
    /// <para><b>游客</b>传空券集合，逻辑与「没有可用券」完全一致，只算活动价——
    /// 这正是 11.5 要求的「游客只计算活动价，不计券」。</para>
    /// </remarks>
    public static FinalPriceResult Calculate(
        IReadOnlyList<PromotionLine> lines,
        IReadOnlyList<PromotionActivity> activities,
        IReadOnlyList<UserCoupon> coupons,
        int priority,
        DateTime nowUtc)
    {
        var originals = lines.Select(a => Round2(a.Amount)).ToArray();
        var originalTotal = Round2(originals.Sum());

        // ---- ① 订单级选券（11.3 / 11.4：订单级唯一一张，取最优） ----
        var couponLines = lines.Select(a => new CouponOrderLine(a.SpuId, a.SkuId, a.Amount)).ToArray();
        CouponQuote? couponQuote = null;
        UserCoupon? pickedCoupon = null;

        if (coupons.Count > 0 && CouponCalculator.TryPickBest(coupons, couponLines, out var picked) && picked is not null)
        {
            couponQuote = picked;
            pickedCoupon = coupons.First(a => a.Id == picked.Value.CouponId);
        }

        var couponFirst = priority != MarketingPriorities.ActivityFirst;

        // 券覆盖的行：券优先时这些行只用券；活动优先时先看有没有活动，没有才轮到券
        var couponCovered = new bool[lines.Count];
        if (pickedCoupon is not null)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                couponCovered[i] = CouponCalculator.CoversLine(pickedCoupon, couponLines[i]);
            }
        }

        // ---- ② 活动池：券优先时排除券覆盖的行 ----
        // 各行互不影响（11.2）：券覆盖的行不参与活动、也不计入门槛基数。
        var pool = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (couponFirst && couponQuote is not null && couponCovered[i]) continue;
            pool.Add(i);
        }

        var poolLines = pool.Select(i => lines[i]).ToArray();

        // ---- ③ 每个活动在活动池上的试算（门槛 = 池内适用行金额合计） ----
        var quotes = new Dictionary<long, ActivityQuote>(activities.Count);
        foreach (var activity in activities)
        {
            quotes[activity.Id] = Quote(activity, poolLines, nowUtc);
        }

        // ---- ④ 逐行贪心选活动：该行分到的优惠最大 → 门槛最高 → 创建最早 ----
        var chosen = new Dictionary<long, List<int>>();
        for (var p = 0; p < pool.Count; p++)
        {
            var i = pool[p];
            PromotionActivity? best = null;
            var bestShare = 0m;

            foreach (var activity in activities)
            {
                var quote = quotes[activity.Id];
                if (!quote.Applicable || !quote.ReachedThreshold) continue;
                if (!CoversLine(activity, lines[i])) continue;

                // 券覆盖的行在活动优先时也不该命中满赠：这张券就是「可用的折扣」
                if (quote.IsGift && couponQuote is not null && couponCovered[i]) continue;

                // 该行能分到的优惠 = 活动优惠额按适用行金额比例分到这一行
                var share = quote.ThresholdBase <= 0m || quote.DiscountAmount <= 0m
                    ? 0m
                    : quote.DiscountAmount * Round2(lines[i].Amount) / quote.ThresholdBase;

                if (best is null
                    || share > bestShare
                    || (share == bestShare && activity.ThresholdAmount > best.ThresholdAmount)
                    || (share == bestShare && activity.ThresholdAmount == best.ThresholdAmount
                        && activity.CreatedAt < best.CreatedAt))
                {
                    best = activity;
                    bestShare = share;
                }
            }

            if (best is null) continue;

            if (!chosen.TryGetValue(best.Id, out var indexes))
            {
                indexes = [];
                chosen[best.Id] = indexes;
            }

            indexes.Add(i);
        }

        // ---- ⑤ 组装逐行结果并落地活动 ----
        var results = new List<PromotionLineResult>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            results.Add(new PromotionLineResult(
                lines[i].SpuId, lines[i].SkuId, originals[i], 0m, 0m, originals[i],
                DiscountSources.None, string.Empty, false));
        }

        var hits = new List<ActivityHit>();
        long giftActivityId = 0;

        foreach (var (activityId, indexes) in chosen)
        {
            var activity = activities.First(a => a.Id == activityId);
            var quote = quotes[activityId];

            // 分摊基数取「活动在池内的全部适用行」，而不是「选了它的行」：
            // 选它的行变多或变少，其它行分到的金额不受影响（各行互不影响）。
            var applicableCount = poolLines.Count(l => CoversLine(activity, l));
            var distributable = Round2(indexes.Sum(i => results[i].OriginalAmount));
            var total = Math.Min(quote.DiscountAmount, distributable);

            if (total > 0m)
            {
                ApplyDiscountByBase(
                    results, indexes, total, quote.ThresholdBase,
                    indexes.Count == applicableCount, DiscountSources.Activity, activity.ActivityName);
            }
            else if (quote.IsGift)
            {
                // 满赠折扣额是 0，但要给命中行打上角标：商品卡要显示「满 X 赠券」而不是「无优惠」
                foreach (var i in indexes)
                {
                    results[i] = results[i] with
                    {
                        Source = DiscountSources.Gift,
                        SourceName = activity.ActivityName
                    };
                }

                giftActivityId = activity.Id;
            }

            hits.Add(new ActivityHit(
                activity.Id, activity.ActivityName,
                Round2(indexes.Sum(i => results[i].ActivityDiscount))));
        }

        // ---- ⑥ 券落地 ----
        // 券优先：券覆盖的所有行都用券。
        // 活动优先：只有没命中活动的券覆盖行才用券；参与活动的行已不在券的实际作用范围里，
        // 门槛按这些行重算——否则会出现「券把已经参加活动的行也算进门槛」的虚高结论。
        CouponQuote? effectiveCoupon = null;
        var couponTargets = new List<int>();
        if (couponQuote is not null && pickedCoupon is not null)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (!couponCovered[i]) continue;
                if (!couponFirst && results[i].ActivityDiscount > 0m) continue;
                couponTargets.Add(i);
            }

            if (couponFirst)
            {
                effectiveCoupon = couponQuote;
            }
            else if (couponTargets.Count > 0)
            {
                var sub = CouponCalculator.Quote(
                    pickedCoupon, couponTargets.Select(i => couponLines[i]).ToArray());
                effectiveCoupon = sub.ReachedThreshold && sub.DiscountAmount > 0m ? sub : null;
            }

            if (effectiveCoupon is not null && couponTargets.Count > 0)
            {
                var distributable = Round2(couponTargets.Sum(i => results[i].OriginalAmount));
                var total = Math.Min(effectiveCoupon.Value.DiscountAmount, distributable);
                ApplyDiscountByBase(
                    results, couponTargets, total, effectiveCoupon.Value.ThresholdBase,
                    true, DiscountSources.Coupon, effectiveCoupon.Value.CouponCode);
            }
            else
            {
                effectiveCoupon = null;
            }
        }

        // ---- ⑦ 逐行算到手价并封底 ----
        for (var i = 0; i < results.Count; i++)
        {
            var a = results[i];
            var payable = Round2(a.OriginalAmount - a.ActivityDiscount - a.CouponDiscount);
            var floored = false;

            if (payable < LineFloor)
            {
                payable = LineFloor;
                floored = true;
            }

            results[i] = a with { PayableAmount = payable, IsFloored = floored };
        }

        var activityTotal = Round2(results.Sum(a => a.ActivityDiscount));
        var couponTotal = Round2(results.Sum(a => a.CouponDiscount));

        return new FinalPriceResult(
            results,
            originalTotal,
            activityTotal,
            couponTotal,
            Round2(results.Sum(a => a.PayableAmount)),
            effectiveCoupon?.CouponId ?? 0,
            effectiveCoupon?.CouponCode ?? string.Empty,
            hits.Count > 0,
            hits,
            giftActivityId);
    }

    /// <summary>把一个整单优惠额按行金额比例分摊到选中的行上。</summary>
    /// <param name="results">逐行结果，原地写入折扣额与来源。</param>
    /// <param name="indexes">本次实际落到的行下标（只包含选了该优惠的行）。</param>
    /// <param name="total">要分摊的优惠总额。</param>
    /// <param name="baseSum">分摊基数：该优惠<b>全部适用行</b>的原价合计。</param>
    /// <param name="absorbRemainder">是否让金额最大的行吸收舍入余数（适用行全部落上时为 true）。</param>
    /// <param name="source">来源标签，见 <see cref="DiscountSources"/>。</param>
    /// <param name="sourceName">来源名称（活动名或券码），展示在优惠角标上。</param>
    /// <remarks>
    /// <b>分摊基数为什么是「全部适用行」而不是「选中行」</b>：
    /// 逐行贪心下同一活动的适用行可能只有一部分选了它，
    /// 用「选中行」做基数会让选它的行分到更大的份额（行与行互相影响），
    /// 违反 11.2 的「各行互不影响，取该行可用最大优惠」。
    /// </remarks>
    private static void ApplyDiscountByBase(
        List<PromotionLineResult> results, IReadOnlyList<int> indexes, decimal total,
        decimal baseSum, bool absorbRemainder, string source, string sourceName)
    {
        if (indexes.Count == 0 || total <= 0m) return;

        baseSum = Round2(baseSum);
        if (baseSum <= 0m) return;

        var largest = indexes[0];
        for (var k = 1; k < indexes.Count; k++)
        {
            if (results[indexes[k]].OriginalAmount > results[largest].OriginalAmount) largest = indexes[k];
        }

        var allocated = 0m;
        foreach (var i in indexes)
        {
            if (absorbRemainder && i == largest) continue;

            var part = Round2(total * results[i].OriginalAmount / baseSum);
            // 单行优惠不能超过这一行本身（否则订单行会出现「优惠 > 原价」）
            if (part > results[i].OriginalAmount) part = results[i].OriginalAmount;
            results[i] = WithDiscount(results[i], part, source, sourceName);
            allocated = Round2(allocated + part);
        }

        if (!absorbRemainder) return;

        // 余数全给金额最大的那一行：按比例逐行 Round 一定会丢掉几分钱，
        // 结果就是「各行优惠之和 ≠ 整单优惠」，对账时又是一次「差一分钱」
        var last = Round2(total - allocated);
        if (last < 0m) last = 0m;
        if (last > results[largest].OriginalAmount) last = results[largest].OriginalAmount;
        results[largest] = WithDiscount(results[largest], last, source, sourceName);
    }

    /// <summary>给一行写入折扣额与来源标签。</summary>
    /// <param name="line">该行当前结果。</param>
    /// <param name="discount">该行分摊到的优惠额。</param>
    /// <param name="source">来源标签。</param>
    /// <param name="sourceName">来源名称。</param>
    /// <returns>写入后的行结果。</returns>
    private static PromotionLineResult WithDiscount(
        PromotionLineResult line, decimal discount, string source, string sourceName)
        => source == DiscountSources.Coupon
            ? line with { CouponDiscount = discount, Source = source, SourceName = sourceName }
            : line with { ActivityDiscount = discount, Source = source, SourceName = sourceName };

    /// <summary>判断某一行是否落在活动范围内（商户 + SPU / SKU）。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="line">订单行。</param>
    /// <returns>落在范围内返回 true。</returns>
    /// <remarks>逐行贪心要靠它把「这个活动对这个行是否适用」单独判一次，
    /// 不能拿整单的适用性（<see cref="IsLineInScope"/>）顶替。</remarks>
    public static bool CoversLine(PromotionActivity activity, PromotionLine line)
    {
        // 商户级活动只覆盖自己家的行：不判这一条，优惠额会分摊到别的商户的商品上
        if (!CoversMerchant(activity, line)) return false;
        if (activity.TargetType == TargetTypes.All) return true;

        var ids = ParseTargets(activity.Targets);
        return activity.TargetType == TargetTypes.BySpu
            ? ids.Contains(line.SpuId)
            : ids.Contains(line.SkuId);
    }

    /// <summary>算活动「适用行金额合计」。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="lines">订单行。</param>
    /// <returns>适用行金额合计。</returns>
    public static decimal ResolveApplicableAmount(PromotionActivity activity, IReadOnlyList<PromotionLine> lines)
    {
        // 商户维度先过一遍：不属于本活动的行不计入门槛基数
        var own = lines.Where(a => CoversMerchant(activity, a)).ToList();

        if (activity.TargetType == TargetTypes.All) return Round2(own.Sum(a => Round2(a.Amount)));

        var ids = ParseTargets(activity.Targets);

        return Round2(own
            .Where(a => activity.TargetType == TargetTypes.BySpu ? ids.Contains(a.SpuId) : ids.Contains(a.SkuId))
            .Sum(a => Round2(a.Amount)));
    }

    /// <summary>活动是否至少覆盖到一行。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="lines">订单行。</param>
    /// <returns>覆盖到任意一行返回 true。</returns>
    public static bool IsLineInScope(PromotionActivity activity, IReadOnlyList<PromotionLine> lines)
    {
        var own = lines.Where(a => CoversMerchant(activity, a)).ToList();

        if (activity.TargetType == TargetTypes.All) return own.Count > 0;

        var ids = ParseTargets(activity.Targets);
        return own.Any(a => activity.TargetType == TargetTypes.BySpu ? ids.Contains(a.SpuId) : ids.Contains(a.SkuId));
    }

    /// <summary>判断活动是否覆盖该订单行的商户。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="line">订单行。</param>
    /// <returns>覆盖返回 true。</returns>
    /// <remarks>
    /// 平台级活动（<c>MerchantId = 0</c>）作用于所有行；商户级只作用于本商户的行。
    /// 行上的商户为 0（未知）时只有平台级活动能命中 —— 宁可少给优惠，
    /// 也不能把一条商户级活动算到身份不明的行上。
    /// </remarks>
    public static bool CoversMerchant(PromotionActivity activity, PromotionLine line)
        => activity.MerchantId <= 0 || activity.MerchantId == line.MerchantId;

    /// <summary>解析适用范围的 JSON。</summary>
    /// <param name="targets">JSON 文本，解析失败按空集合处理。</param>
    /// <returns>Id 集合。</returns>
    /// <remarks>
    /// 解析失败返回空集合而不是抛异常：适用范围写坏的后果应该是
    /// 「这个活动对谁都��生效」，而不是整个下单页 500。
    /// </remarks>
    public static HashSet<long> ParseTargets(string? targets)
    {
        if (string.IsNullOrWhiteSpace(targets)) return new HashSet<long>();

        try
        {
            return JsonSerializer.Deserialize<long[]>(targets)?.ToHashSet() ?? new HashSet<long>();
        }
        catch (JsonException)
        {
            return new HashSet<long>();
        }
    }

    /// <summary>构造一个「没命中」的活动试算结果。</summary>
    /// <param name="activity">活动。</param>
    /// <param name="baseAmount">门槛判定基数（适用行金额合计）。</param>
    /// <param name="reason">未命中的中文原因，便于前端提示。</param>
    /// <returns>优惠额为 0、ReachedThreshold 为 false 的试算结果。</returns>
    private static ActivityQuote Miss(PromotionActivity activity, decimal baseAmount, string reason)
        => new(activity.Id, activity.ActivityName, 0m, baseAmount, false, true, false, reason);
}
