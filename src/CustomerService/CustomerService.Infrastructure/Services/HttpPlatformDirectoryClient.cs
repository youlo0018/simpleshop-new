using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Collaboration.Domain.Common;
using CustomerService.Application.Services;
using Microsoft.Extensions.Logging;

namespace CustomerService.Infrastructure.Services;

/// <summary>走内网 HTTP 调 MerchantPlatformService 解析平台的实现。</summary>
/// <remarks>
/// 平台不存在返回 null（调用方回「平台不存在」的 400 / 404），
/// 网络异常则抛出：登录 / 注册不能靠猜平台，拿不到就应当失败而不是落一个错误的平台 Id。
/// </remarks>
public sealed class HttpPlatformDirectoryClient : IPlatformDirectoryClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpPlatformDirectoryClient> _logger;

    /// <summary>构造客户端。</summary>
    /// <param name="http">指向 MerchantPlatformService 的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public HttpPlatformDirectoryClient(HttpClient http, ILogger<HttpPlatformDirectoryClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlatformSnapshot?> ResolveAsync(
        string platformCode, CancellationToken ct = default)
    {
        var path = $"internal/platforms/resolve?platformCode={Uri.EscapeDataString(platformCode.Trim())}";
        var response = await _http.GetAsync(path, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("解析平台失败：HTTP {Code}（编码 {Code2}）",
                (int)response.StatusCode, platformCode);
            response.EnsureSuccessStatusCode();
        }

        var body = await response.Content
            .ReadFromJsonAsync<ApiResponse<PlatformResolveResponse>>(ct)
            .ConfigureAwait(false);

        if (body is null || !body.Success || body.Data is null)
        {
            return null;
        }

        return new PlatformSnapshot(
            body.Data.PlatformId, body.Data.PlatformCode, body.Data.MallName, body.Data.Status);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 名称是**展示字段**：取不到时返回空字典让列表照常打开（显示为空，不显示裸 Id），
    /// 只记一条警告。与「解析平台」那种缺了就不能继续的调用刻意区分开。
    /// </remarks>
    public async Task<IReadOnlyDictionary<long, string>> GetPlatformNamesAsync(
        IReadOnlyCollection<long> platformIds, CancellationToken ct = default)
    {
        var ids = platformIds.Where(a => a > 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, string>();

        try
        {
            var response = await _http
                .PostAsJsonAsync("internal/platforms/Names", new { platformIds = ids }, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("取平台名失败：HTTP {Code}", (int)response.StatusCode);
                return new Dictionary<long, string>();
            }

            var body = await response.Content
                .ReadFromJsonAsync<ApiResponse<NameLookupResponse>>(ct)
                .ConfigureAwait(false);

            if (body is null || !body.Success || body.Data is null)
            {
                return new Dictionary<long, string>();
            }

            return body.Data.Platforms.ToDictionary(a => a.Id, a => a.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "取平台名异常，列表将不显示平台名");
            return new Dictionary<long, string>();
        }
    }

    /// <summary>平台解析响应体（字段名与 MerchantPlatformService 的契约一致）。</summary>
    private sealed record PlatformResolveResponse(
        [property: JsonPropertyName("platformId")] long PlatformId,
        [property: JsonPropertyName("platformCode")] string PlatformCode,
        [property: JsonPropertyName("mallName")] string MallName,
        [property: JsonPropertyName("status")] int Status);

    /// <summary>名称查询响应体。</summary>
    private sealed record NameLookupResponse(
        [property: JsonPropertyName("platforms")] IReadOnlyList<NamePairResponse> Platforms);

    /// <summary>Id → 名称。</summary>
    private sealed record NamePairResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name);
}
