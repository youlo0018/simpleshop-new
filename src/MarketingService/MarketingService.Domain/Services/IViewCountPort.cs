namespace MarketingService.Domain.Services;

/// <summary>秒杀场次 PV 查询端口（埋点数据在日志服务的 Elasticsearch 里）。</summary>
/// <remarks>
/// PV 由日志服务聚合而不是营销服务查 ES：日志索引归日志服务所有，
/// 从别的服务直连它的存储等于把两个服务的边界焊死，索引一改名就全线报废。
/// </remarks>
public interface IViewCountPort
{
    /// <summary>按埋点路径统计每场秒杀的 PV。</summary>
    /// <param name="sessionIds">要统计的场次 Id；空集合直接返回空字典。</param>
    /// <param name="from">开始时间 UTC（含）。</param>
    /// <param name="to">结束时间 UTC（不含）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>场次 Id → PV 次数；日志服务不可用时返回空字典（报表降级为 0，不拖垮整张报表）。</returns>
    Task<IReadOnlyDictionary<long, long>> CountBySessionAsync(
        IReadOnlyCollection<long> sessionIds,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
