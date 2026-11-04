using System.Net.Http.Json;
using Collaboration.Domain.Common;
using Microsoft.Extensions.Logging;

namespace ProductService.Application.Services;

/// <summary>营销活动 / 用户券的商品范围查询端口。</summary>
public interface IMarketingTargetClient
{
    /// <summary>解析活动或用户券适用的 SPU / SKU。</summary>
    /// <param name="activityId">活动 Id，0 表示未指定。</param>
    /// <param name="couponId">用户券 Id，0 表示未指定。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析结果；营销服务不可用时返回 null。</returns>
    Task<MarketingProductTargets?> ResolveAsync(
        long activityId, long couponId, CancellationToken ct = default);
}

/// <summary>营销商品范围。</summary>
/// <param name="Exists">对象是否存在。</param>
/// <param name="TargetType">1 全场 / 2 SPU / 3 SKU。</param>
/// <param name="SpuIds">指定 SPU。</param>
/// <param name="SkuIds">指定 SKU。</param>
public sealed record MarketingProductTargets(
    bool Exists, int TargetType, IReadOnlyList<long> SpuIds, IReadOnlyList<long> SkuIds);

/// <summary>通过内网查询营销服务商品范围。</summary>
public sealed class HttpMarketingTargetClient : IMarketingTargetClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpMarketingTargetClient> _logger;

    /// <summary>构造客户端。</summary>
    /// <param name="http">指向营销服务的客户端。</param>
    /// <param name="logger">日志器。</param>
    public HttpMarketingTargetClient(HttpClient http, ILogger<HttpMarketingTargetClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MarketingProductTargets?> ResolveAsync(
        long activityId, long couponId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "internal/marketing/product-targets",
                new { activityId, couponId },
                ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("营销商品范围查询返回 HTTP {Code}", (int)response.StatusCode);
                return null;
            }

            var envelope = await response.Content
                .ReadFromJsonAsync<ApiResponse<MarketingProductTargets>>(ct)
                .ConfigureAwait(false);
            return envelope is { Success: true, Data: not null } ? envelope.Data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "营销商品范围查询失败");
            return null;
        }
    }
}
