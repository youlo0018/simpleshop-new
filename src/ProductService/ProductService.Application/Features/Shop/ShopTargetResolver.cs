using ProductService.Application.Services;
using ProductService.Domain.IRepository;

namespace ProductService.Application.Features.Shop;

/// <summary>把活动 / 用户券的目标解析成商品 Id 过滤条件。</summary>
internal static class ShopTargetResolver
{
    /// <summary>解析活动与券的目标交集。</summary>
    /// <param name="products">商品仓储，用于把 SKU 目标映射回 SPU。</param>
    /// <param name="targets">营销范围客户端。</param>
    /// <param name="activityId">活动 Id，0 表示不限制。</param>
    /// <param name="couponId">用户券 Id，0 表示不限制。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否启用过滤及最终商品 Id 集合。</returns>
    public static async Task<(bool Specified, IReadOnlyList<long> ProductIds)> ResolveAsync(
        IProductRepository products,
        IMarketingTargetClient targets,
        long activityId,
        long couponId,
        CancellationToken ct)
    {
        if (activityId <= 0 && couponId <= 0)
        {
            return (false, []);
        }

        HashSet<long>? intersection = null;
        foreach (var (id, isActivity) in new[]
                 {
                     (activityId, true),
                     (couponId, false)
                 })
        {
            if (id <= 0) continue;

            var target = await targets.ResolveAsync(
                isActivity ? id : 0,
                isActivity ? 0 : id,
                ct).ConfigureAwait(false);
            if (target is null || !target.Exists)
            {
                return (true, []);
            }

            var ids = target.TargetType switch
            {
                1 => await products.ListAllVisibleIdsAsync(ct).ConfigureAwait(false),
                2 => target.SpuIds.ToList(),
                3 => (await products.GetSkusByIdsAsync(target.SkuIds, false, ct)
                        .ConfigureAwait(false))
                    .Select(a => a.ProductId)
                    .Distinct()
                    .ToList(),
                _ => []
            };

            if (intersection is null) intersection = ids.ToHashSet();
            else intersection.IntersectWith(ids);
        }

        return (true, intersection?.OrderBy(a => a).ToArray() ?? []);
    }
}
