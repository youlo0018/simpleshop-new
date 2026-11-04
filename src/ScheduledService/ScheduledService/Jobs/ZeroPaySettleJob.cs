using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ScheduledService.Jobs;

/// <summary>实付 0 元订单的占用结清补偿任务。</summary>
/// <remarks>
/// <para>实付 0 元的单没有支付这一步，创建时就进 20 待发货，但库存仍锁着、
/// 积分仍冻着、券仍占着 —— 这些占用靠下单后当场调 OrderService 的收尾方法结清。
/// 任一步下游抖动都会让占用永远留着，而孤儿对账因为「订单存在」也不会释放。</para>
///
/// <para>任务只是「到点来催一次」：哪些单要补、怎么补都由订单服务自己决定
/// （扫描条件与幂等重放都在订单服务里）。</para>
/// </remarks>
public sealed class ZeroPaySettleJob : IJob
{
    private readonly HttpClient _http;
    private readonly ILogger<ZeroPaySettleJob> _logger;

    /// <summary>构造任务。</summary>
    /// <param name="http">指向订单服务的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public ZeroPaySettleJob(HttpClient http, ILogger<ZeroPaySettleJob> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "zero_pay_settle";

    /// <inheritdoc />
    public int IntervalSeconds => 120;

    /// <inheritdoc />
    public int LockTtlSeconds => 180;

    /// <inheritdoc />
    public int InitialDelaySeconds => 120;

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(CancellationToken ct)
    {
        var response = await _http
            .PostAsJsonAsync("internal/orders/settle-zero-pay", new { limit = 50 }, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return JobRunResult.Fail("0 元订单结清补偿请求失败", $"HTTP {(int)response.StatusCode}");
        }

        var body = await response.Content
            .ReadFromJsonAsync<SettleResponse>(ct)
            .ConfigureAwait(false);

        if (body is null || !body.Success)
        {
            return JobRunResult.Fail("0 元订单结清补偿业务失败", body?.Message ?? "(空响应)");
        }

        var data = body.Data;
        if (data is null || data.Scanned == 0)
        {
            return JobRunResult.Ok("没有待结清的 0 元订单");
        }

        if (data.Failed.Count > 0)
        {
            _logger.LogError(
                "{Count} 张 0 元订单结清仍失败，下轮继续重试：{Items}",
                data.Failed.Count, string.Join("、", data.Failed));
        }

        return JobRunResult.Ok(
            $"扫描 {data.Scanned} 张 0 元订单，结清 {data.Settled} 张"
            + (data.Failed.Count > 0 ? $"，{data.Failed.Count} 张仍失败" : string.Empty));
    }

    /// <summary>订单服务返回的响应。</summary>
    /// <param name="Success">是否成功。</param>
    /// <param name="Message">提示消息。</param>
    /// <param name="Data">补偿统计。</param>
    private sealed record SettleResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("data")] SettleData? Data);

    /// <summary>补偿统计。</summary>
    /// <param name="Scanned">扫描张数。</param>
    /// <param name="Settled">结清张数。</param>
    /// <param name="Failed">失败清单。</param>
    private sealed record SettleData(
        [property: JsonPropertyName("scanned")] int Scanned,
        [property: JsonPropertyName("settled")] int Settled,
        [property: JsonPropertyName("failed")] List<string> Failed);
}
