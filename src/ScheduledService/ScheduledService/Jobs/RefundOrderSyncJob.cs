using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ScheduledService.Jobs;

/// <summary>退款单与订单侧的对账重放任务。</summary>
/// <remarks>
/// <para>审批链路是「先把退款单改成已退款，再回调订单侧落账」。订单侧失败时
/// （订单服务抖动、网络超时），支付侧已经退款、订单侧台账 / 已退金额 / 库存 / 积分都没动，
/// 而重复审批会被「该退款单已处理」挡住 —— 没有这条重放，不一致状态就永久存在。</para>
///
/// <para>订单侧按退款单号幂等（台账唯一索引 + 回补单号带退款单号），重放不会退两次。</para>
/// </remarks>
public sealed class RefundOrderSyncJob : IJob
{
    private readonly HttpClient _http;
    private readonly ILogger<RefundOrderSyncJob> _logger;

    /// <summary>构造任务。</summary>
    /// <param name="http">指向支付服务的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public RefundOrderSyncJob(HttpClient http, ILogger<RefundOrderSyncJob> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "refund_order_sync";

    /// <inheritdoc />
    public int IntervalSeconds => 120;

    /// <inheritdoc />
    public int LockTtlSeconds => 180;

    /// <inheritdoc />
    public int InitialDelaySeconds => 90;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(CancellationToken ct)
    {
        var response = await _http
            .PostAsJsonAsync("internal/payments/refunds/SyncOrder", new { limit = 50 }, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return JobRunResult.Fail("退款对账重放请求失败", $"HTTP {(int)response.StatusCode}");
        }

        var body = await response.Content
            .ReadFromJsonAsync<SyncResponse>(ct)
            .ConfigureAwait(false);

        if (body is null || !body.Success)
        {
            return JobRunResult.Fail("退款对账重放业务失败", body?.Message ?? "(空响应)");
        }

        var data = body.Data;
        if (data is null || data.Scanned == 0)
        {
            return JobRunResult.Ok("没有待对账的退款单");
        }

        if (data.Failed.Count > 0)
        {
            _logger.LogError(
                "{Count} 笔退款单的订单侧落账仍失败，下轮继续重试：{Items}",
                data.Failed.Count, string.Join("、", data.Failed));
        }

        return JobRunResult.Ok(
            $"扫描 {data.Scanned} 笔待同步退款，成功 {data.Synced} 笔"
            + (data.Failed.Count > 0 ? $"，{data.Failed.Count} 笔仍失败" : string.Empty));
    }

    /// <summary>支付服务返回的响应。</summary>
    /// <param name="Success">是否成功。</param>
    /// <param name="Message">提示消息。</param>
    /// <param name="Data">统计。</param>
    private sealed record SyncResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("data")] SyncData? Data);

    /// <summary>重放统计。</summary>
    /// <param name="Scanned">扫描条数。</param>
    /// <param name="Synced">成功条数。</param>
    /// <param name="Failed">失败清单。</param>
    private sealed record SyncData(
        [property: JsonPropertyName("scanned")] int Scanned,
        [property: JsonPropertyName("synced")] int Synced,
        [property: JsonPropertyName("failed")] List<string> Failed);
}
