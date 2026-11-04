using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Collaboration.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CartService.Application.Services;

/// <summary>商户名称客户端：购物车按店铺分组时批量取店铺名。</summary>
/// <remarks>
/// 名称是展示字段：取不到时降级为空字典，购物车照常打开（前端显示「店铺」），
/// 只记警告。与「取价格 / 可售状态」那种失败会影响交易的调用刻意区分开。
/// </remarks>
public interface IMerchantNameClient
{
    /// <summary>按 Id 集合批量取商户名。</summary>
    /// <param name="merchantIds">商户 Id 集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>商户 Id → 名称；查不到的不出现在结果里。只读、幂等。</returns>
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> merchantIds, CancellationToken ct = default);
}

/// <summary>走内网 HTTP 调 MerchantPlatformService 的商户名实现。</summary>
public sealed class HttpMerchantNameClient : IMerchantNameClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpMerchantNameClient> _logger;

    /// <summary>构造客户端。</summary>
    /// <param name="http">指向 MerchantPlatformService 的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public HttpMerchantNameClient(HttpClient http, ILogger<HttpMerchantNameClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> merchantIds, CancellationToken ct = default)
    {
        var ids = merchantIds.Where(a => a > 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, string>();

        try
        {
            var response = await _http
                .PostAsJsonAsync("internal/platforms/Names", new { merchantIds = ids }, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("取商户名失败：HTTP {Code}", (int)response.StatusCode);
                return new Dictionary<long, string>();
            }

            var body = await response.Content
                .ReadFromJsonAsync<ApiResponse<NameLookupResponse>>(ct)
                .ConfigureAwait(false);

            if (body is null || !body.Success || body.Data is null)
            {
                return new Dictionary<long, string>();
            }

            return body.Data.Merchants.ToDictionary(a => a.Id, a => a.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "取商户名异常，购物车将显示「店铺」");
            return new Dictionary<long, string>();
        }
    }

    /// <summary>名称查询响应体。</summary>
    private sealed record NameLookupResponse(
        [property: JsonPropertyName("merchants")] IReadOnlyList<NamePairResponse> Merchants);

    /// <summary>Id → 名称。</summary>
    private sealed record NamePairResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name);
}

/// <summary>未配置商户服务地址时的降级实现：不返回名称，购物车仍可用。</summary>
public sealed class UnavailableMerchantNameClient : IMerchantNameClient
{
    /// <inheritdoc />
    public Task<IReadOnlyDictionary<long, string>> GetNamesAsync(
        IReadOnlyCollection<long> merchantIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());
}
