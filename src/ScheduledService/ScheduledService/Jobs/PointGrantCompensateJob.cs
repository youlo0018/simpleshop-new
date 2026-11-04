using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Collaboration.Domain.Infrastructure;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ScheduledService.Jobs;

/// <summary>积分发放补偿任务。</summary>
/// <remarks>
/// <para>注册赠分（CustomerService）、首评赠分（EvaluateService）、订单完成赠分（OrderService）
/// 都在发放失败时把任务写进共享 Redis 队列（<see cref="PointGrantCompensationQueue"/>）。
/// 没有这个任务时那些失败只留一行日志，积分就静默消失了 —— 而代码注释里还写着
/// 「由补偿任务补发」，实际上并不存在。</para>
///
/// <para>补发调的是与首次发放<b>同一个</b>积分服务接口：金额由积分规则决定，
/// 队列里不带金额，规则改了以后补发口径也不会与首次发放漂移。接口按 bizNo 幂等，
/// 重复消费不会多发。</para>
/// </remarks>
public sealed class PointGrantCompensateJob : IJob
{
    /// <summary>单轮最多补发多少条。</summary>
    private const int MaxPerRun = 100;

    /// <summary>同一条连续失败多少次后转人工（不再回队，避免毒消息永久堵住队列）。</summary>
    private const int MaxAttempts = 10;

    private readonly HttpClient _http;
    private readonly IDatabase _redis;
    private readonly ILogger<PointGrantCompensateJob> _logger;

    /// <summary>构造任务。</summary>
    /// <param name="http">指向积分服务的 HttpClient。</param>
    /// <param name="redis">共享库的 Redis 句柄（写入方是三个业务服务）。</param>
    /// <param name="logger">日志器。</param>
    public PointGrantCompensateJob(
        HttpClient http, IDatabase redis, ILogger<PointGrantCompensateJob> logger)
    {
        _http = http;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "point_grant_compensate";

    /// <inheritdoc />
    public int IntervalSeconds => 300;

    /// <inheritdoc />
    public int LockTtlSeconds => 240;

    /// <inheritdoc />
    public int InitialDelaySeconds => 150;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(CancellationToken ct)
    {
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var dropped = 0;

        // 先把上一轮崩溃时留在「处理中」列表里的记录收回来（取任务用的是 RPOPLPUSH，
        // 崩在两步之间只会留在处理中列表，不会丢）
        var recovered = await PointGrantCompensationQueue.RecoverAsync(_redis).ConfigureAwait(false);
        if (recovered > 0)
        {
            _logger.LogWarning("回收 {Count} 条上一轮未处理完的积分补发记录", recovered);
        }

        for (var i = 0; i < MaxPerRun; i++)
        {
            ct.ThrowIfCancellationRequested();

            var lease = await PointGrantCompensationQueue.DequeueAsync(_redis).ConfigureAwait(false);
            if (lease is null) break;

            var item = lease.Item;

            processed++;

            if (await ReplayAsync(item, ct).ConfigureAwait(false))
            {
                await PointGrantCompensationQueue.CompleteAsync(_redis, lease).ConfigureAwait(false);
                succeeded++;
                continue;
            }

            if (item.Attempts + 1 >= MaxAttempts)
            {
                dropped++;
                _logger.LogError(
                    "积分补发连续 {Attempts} 次失败，转人工处理：类型 {Kind}，客户 {CustomerId}，业务号 {BizNo}",
                    item.Attempts + 1, item.Kind, item.CustomerId, item.BizNo);
                await PointGrantCompensationQueue.CompleteAsync(_redis, lease).ConfigureAwait(false);
                continue;
            }

            failed++;
            await PointGrantCompensationQueue
                .RequeueAsync(_redis, lease, item with { Attempts = item.Attempts + 1 })
                .ConfigureAwait(false);
        }

        if (processed == 0 && recovered == 0)
        {
            return JobRunResult.Ok("没有待补发的积分");
        }

        return JobRunResult.Ok(
            $"回收 {recovered} 条，处理 {processed} 条积分补发，成功 {succeeded}，失败待重试 {failed}"
            + (dropped > 0 ? $"，转人工 {dropped}" : string.Empty));
    }

    /// <summary>补发一条。</summary>
    /// <param name="item">待补发记录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功（或毒消息被丢弃）返回 true；应重试返回 false。</returns>
    private async Task<bool> ReplayAsync(PointGrantCompensation item, CancellationToken ct)
    {
        string path;
        object payload;

        switch (item.Kind)
        {
            case PointGrantKinds.Register:
                path = "internal/points/EarnRegisterGift";
                payload = new { customerId = item.CustomerId };
                break;

            case PointGrantKinds.Evaluate:
                if (!long.TryParse(item.BizNo, out var evaluateId) || evaluateId <= 0)
                {
                    // 坏数据丢弃：留在队列里会永久堵住后面所有补偿
                    _logger.LogError(
                        "积分补发记录的评价 Id 不合法（{BizNo}），已丢弃", item.BizNo);
                    return true;
                }

                path = "internal/points/EarnEvaluateGift";
                payload = new { customerId = item.CustomerId, evaluateId };
                break;

            case PointGrantKinds.Order:
                path = "internal/points/EarnByOrder";
                payload = new
                {
                    customerId = item.CustomerId,
                    bizNo = item.BizNo,
                    paidAmount = item.PaidAmount,
                    remark = "订单完成发放（补偿）"
                };
                break;

            default:
                _logger.LogError("未知的积分补偿类型 {Kind}，已丢弃", item.Kind);
                return true;
        }

        try
        {
            var response = await _http.PostAsJsonAsync(path, payload, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "积分补发失败：HTTP {Code}（类型 {Kind}，客户 {CustomerId}）",
                    (int)response.StatusCode, item.Kind, item.CustomerId);
                return false;
            }

            // 幂等命中（已经发过）也会返回 success=true，视为成功；
            // 业务失败（例如规则校验不过）当失败重试，连续失败到上限后转人工。
            var body = await response.Content.ReadFromJsonAsync<ApiEnvelope>(ct).ConfigureAwait(false);
            if (body is null || !body.Success)
            {
                _logger.LogWarning(
                    "积分补发业务失败（类型 {Kind}，客户 {CustomerId}）：{Message}",
                    item.Kind, item.CustomerId, body?.Message ?? "(空响应)");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "积分补发调用异常（类型 {Kind}，客户 {CustomerId}）", item.Kind, item.CustomerId);
            return false;
        }
    }

    /// <summary>统一响应体。</summary>
    /// <param name="Success">是否成功。</param>
    /// <param name="Message">提示消息。</param>
    private sealed record ApiEnvelope(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message);
}
