namespace MarketingService.Domain.Entities;

/// <summary>秒杀相关的 Redis 键前缀。</summary>
/// <remarks>
/// 单独抽出来是因为<b>发布场次写这个键、抢购读这个键</b>，两处各写一遍字符串的话，
/// 改了一处就会导致「发布了但抢购读不到余量」，而现象是所有人抢到 0 件——
/// 这种错现场几乎看不出来，只能对键名。
/// </remarks>
public static class SeckillStockKeys
{
    /// <summary>秒杀余量键前缀，后接商品 Id。</summary>
    /// <remarks>值 = 剩余可抢数量，抢购时 DECBY 1。</remarks>
    public const string Stock = "seckill:stock:";
}

/// <summary>秒杀与库存服务之间的业务单号格式（幂等键）。</summary>
/// <remarks>
/// 划出与回补各用一套固定单号：重复调用命中库存侧幂等，不会把同一批货划两次 / 还两次。
/// 单号格式集中在这里而不是散在调用点 —— 「部分划出后中止」的回补要按同一格式
/// 反查划出流水是否存在，格式写岔一处就会把货凭空多还一批。
/// </remarks>
public static class SeckillBizNos
{
    /// <summary>发布场次时的划出单号。</summary>
    /// <param name="sessionId">场次 Id。</param>
    /// <param name="skuId">SKU Id。</param>
    /// <returns>业务单号。</returns>
    public static string Reserve(long sessionId, long skuId) => $"SKL-RESERVE-{sessionId}-{skuId}";

    /// <summary>结束 / 中止场次时的回补单号。</summary>
    /// <param name="sessionId">场次 Id。</param>
    /// <param name="skuId">SKU Id。</param>
    /// <returns>业务单号。</returns>
    public static string Release(long sessionId, long skuId) => $"SKL-RELEASE-{sessionId}-{skuId}";
}