using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Collaboration.Domain.Common;
using MarketingService.Domain.Services;
using Microsoft.Extensions.Logging;

namespace MarketingService.Infrastructure.Ports;

/// <summary>走内网 HTTP 向日志服务要秒杀场次 PV。</summary>
/// <remarks>
/// 失败返回空字典而不是抛异常：PV 只是报表里的一项指标，
/// 日志服务 / ES 没起来（Linux minimal 档就不起日志服务）时，
/// 报表应该显示 0 并继续可用，而不是整页 500。
/// </remarks>
public sealed class HttpViewCountPort : IViewCountPort
{
    /// <summary>埋点接口在下游服务记录的路径。</summary>
    /// <remarks>
    /// 必须用**下游服务**记录的 <c>/marketing/...</c> 精确匹配，而不是网关记录的
    /// <c>/gateway/marketing/...</c>：一次埋点会同时产生两条 pv（网关一条、营销服务一条），
    /// 拿网关路径统计没错，但两边都算就会把 PV 翻倍。这里只认营销服务自己那条。
    /// </remarks>
    private const string ViewPath = "/marketing/seckill/sessions/View";

    private readonly HttpClient _http;
    private readonly ILogger<HttpViewCountPort> _logger;

    /// <summary>构造端口。</summary>
    /// <param name="http">指向日志服务的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public HttpViewCountPort(HttpClient http, ILogger<HttpViewCountPort> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, long>> CountBySessionAsync(
        IReadOnlyCollection<long> sessionIds,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        var result = new Dictionary<long, long>();
        if (sessionIds is null || sessionIds.Count == 0) return result;

        try
        {
            var body = new Query
            {
                Path = ViewPath,
                SessionIds = sessionIds.Distinct().ToList(),
                From = from,
                To = to
            };

            var response = await _http
                .PostAsJsonAsync("internal/logs/pv-by-session", body, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("日志服务 PV 统计失败：HTTP {Code}", (int)response.StatusCode);
                return result;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<ApiResponse<List<Item>>>(ct).ConfigureAwait(false);

            if (payload is null || !payload.Success || payload.Data is null)
            {
                _logger.LogError("日志服务 PV 统计业务失败：{Message}", payload?.Message ?? "(空响应)");
                return result;
            }

            foreach (var item in payload.Data)
            {
                result[item.SessionId] = item.Count;
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "调用日志服务统计 PV 失败，PV 按 0 计");
            return result;
        }
    }

    /// <summary>PV 统计请求体。</summary>
    private sealed class Query
    {
        /// <summary>埋点路径。</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>场次 Id 集合。</summary>
        [JsonPropertyName("sessionIds")]
        public List<long> SessionIds { get; set; } = new();

        /// <summary>开始时间 UTC（含）。</summary>
        [JsonPropertyName("from")]
        public DateTime From { get; set; }

        /// <summary>结束时间 UTC（不含）。</summary>
        [JsonPropertyName("to")]
        public DateTime To { get; set; }
    }

    /// <summary>单个场次的 PV。</summary>
    private sealed class Item
    {
        /// <summary>场次 Id。</summary>
        [JsonPropertyName("sessionId")]
        public long SessionId { get; set; }

        /// <summary>PV 次数。</summary>
        [JsonPropertyName("count")]
        public long Count { get; set; }
    }
}
