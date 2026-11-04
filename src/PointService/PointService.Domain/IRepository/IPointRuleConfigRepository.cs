using PointService.Domain.Entities;

namespace PointService.Domain.IRepository;

/// <summary>积分规则配置仓储（后台维护的覆盖表）。</summary>
/// <remarks>
/// 规则只有 7 条且永远存在，读路径由 <c>IPointRuleProvider</c> 带缓存提供；
/// 这里只负责后台维护页的读改写，避免 Application 层直接依赖 FreeSql（CODING_STANDARD 1）。
/// </remarks>
public interface IPointRuleConfigRepository
{
    /// <summary>是否至少有一条规则被后台改过。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>有自定义值返回 true。</returns>
    /// <remarks>后台靠它决定要不要显示「恢复默认」按钮。</remarks>
    Task<bool> HasCustomizedAsync(CancellationToken ct = default);

    /// <summary>按规则键取配置行。</summary>
    /// <param name="ruleKey">规则键，见 <see cref="PointRuleKeys"/>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>配置行；没配过返回 null。</returns>
    Task<PointRuleConfig?> GetByKeyAsync(string ruleKey, CancellationToken ct = default);

    /// <summary>写入或更新一条规则值。</summary>
    /// <param name="ruleKey">规则键。</param>
    /// <param name="ruleValue">规则值（数字 / JSON 文本）。</param>
    /// <param name="description">中文说明快照。</param>
    /// <param name="operatorId">操作人 Id。</param>
    /// <param name="operatorName">操作人姓名。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task UpsertAsync(
        string ruleKey, string ruleValue, string description,
        long operatorId, string operatorName, CancellationToken ct = default);
}
