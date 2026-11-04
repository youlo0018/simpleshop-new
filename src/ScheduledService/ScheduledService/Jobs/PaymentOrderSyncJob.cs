using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ScheduledService.Jobs;

/// <summary>支付单与订单状态的对账任务。</summary>
/// <remarks>
/// <para>支付收尾是「先让订单侧置已支付，再改支付单状态」。第二步失败时订单已经付了、
/// 支付单还挂在待支付：后台支付列表与报表都会把它当成没收钱，用户再点支付又被
/// 「只有待支付的订单可以支付」挡住。</para>
///
/// <para>本任务让支付服务回查订单状态，发现订单已支付就把支付单补成已支付。</para>
/// </remarks>
public sealed class PaymentOrderSyncJob : IJob
{
    private readonly HttpClient _http;
    private readonly ILogger<PaymentOrderSyncJob> _logger;

    /// <summary>构造任务。</summary>
    /// <param name="http">指向支付服务的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public PaymentOrderSyncJob(HttpClient http, ILogger<PaymentOrderSyncJob> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "payment_order_sync";

    /// <inheritdoc />
    public int IntervalSeconds => 300;

    /// <inheritdoc />
    public int LockTtlSeconds => 240;

    /// <inheritdoc />
    public int InitialDelaySeconds => 120;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(CancellationToken ct)
    {
        var response = await _http
            .PostAsJsonAsync("internal/payments/ReconcilePending", new { limit = 20 }, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return JobRunResult.Fail("支付单对账请求失败", $"HTTP {(int)response.StatusCode}");
        }

        var body = await response.Content
            .ReadFromJsonAsync<ReconcileResponse>(ct)
            .ConfigureAwait(false);

        if (body is null || !body.Success)
        {
            return JobRunResult.Fail("支付单对账业务失败", body?.Message ?? "(空响应)");
        }

        var data = body.Data;
        if (data is null || data.Scanned == 0)
        {
            return JobRunResult.Ok("没有待对账的支付单");
        }

        if (data.Failed.Count > 0)
        {
            _logger.LogError(
                "{Count} 笔支付单对账失败，下轮继续：{Items}",
                data.Failed.Count, string.Join("、", data.Failed));
        }

        return JobRunResult.Ok(
            $"检查 {data.Scanned} 笔待支付单，补成已支付 {data.Healed} 笔，跳过 {data.Skipped} 笔"
            + (data.Failed.Count > 0 ? $"，{data.Failed.Count} 笔失败" : string.Empty));
    }

    /// <summary>支付服务返回的响应。</summary>
    /// <param name="Success">是否成功。</param>
    /// <param name="Message">提示消息。</param>
    /// <param name="Data">统计。</param>
    private sealed record ReconcileResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("data")] ReconcileData? Data);

    /// <summary>对账统计。</summary>
    /// <param name="Scanned">检查条数。</param>
    /// <param name="Healed">补单条数。</param>
    /// <param name="Skipped">跳过条数。</param>
    /// <param name="Failed">失败清单。</param>
    private sealed record ReconcileData(
        [property: JsonPropertyName("scanned")] int Scanned,
        [property: JsonPropertyName("healed")] int Healed,
        [property: JsonPropertyName("skipped")] int Skipped,
        [property: JsonPropertyName("failed")] List<string> Failed);
}
