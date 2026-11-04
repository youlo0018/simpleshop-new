using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Collaboration.Domain.Common;
using Microsoft.Extensions.Logging;

namespace CartService.Application.Services;

/// <summary>商品服务客户端（购物车要拿 SKU 快照）。</summary>
public interface IProductClient
{
    /// <summary>按 SKU Id 集合取快照信息。</summary>
    /// <param name="skuIds">SKU Id 集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>命中的快照；未命中的 SKU 不会出现在结果里。</returns>
    Task<IReadOnlyList<SkuSnapshot>> GetSkuSnapshotsAsync(IReadOnlyCollection<long> skuIds, CancellationToken ct = default);

    /// <summary>按 SKU Id 集合取**当前可售状态与权威售价**。</summary>
    /// <param name="skuIds">SKU Id 集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>skuId → 状态；已删除的 SKU 不会出现在结果里。</returns>
    /// <remarks>
    /// 走的是 <c>skus/pricing</c> 而不是 <c>skus</c>：后者会过滤掉停用 SKU，
    /// 于是购物车分不清「商品没了」与「商品下架了」，只能笼统报一个错。
    /// 而购物车恰恰需要这个区别 —— 前者该删行，后者该保留并标灰。
    /// </remarks>
    Task<IReadOnlyDictionary<long, SkuAvailability>> GetSkuAvailabilityAsync(
        IReadOnlyCollection<long> skuIds, CancellationToken ct = default);
}

/// <summary>SKU 的当前可售状态。</summary>
/// <param name="SkuId">SKU Id。</param>
/// <param name="Price">权威售价，两位小数。</param>
/// <param name="Enabled">SKU 是否启用。</param>
/// <param name="SpuApproved">所属 SPU 是否审核通过。</param>
/// <param name="SpuOnShelf">所属 SPU 是否已上架。</param>
/// <param name="MerchantId">归属商户 Id，0 表示平台自营。</param>
public sealed record SkuAvailability(
    long SkuId, decimal Price, bool Enabled, bool SpuApproved, bool SpuOnShelf,
    long MerchantId = 0);

/// <summary>SKU 快照。字段与 ProductService 的内部接口保持一致。</summary>
public sealed record SkuSnapshot(
    [property: JsonPropertyName("skuId")] long SkuId,
    [property: JsonPropertyName("productId")] long ProductId,
    [property: JsonPropertyName("skuCode")] string SkuCode,
    [property: JsonPropertyName("skuName")] string SkuName,
    [property: JsonPropertyName("skuSpecText")] string SkuSpecText,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("originalPrice")] decimal OriginalPrice,
    [property: JsonPropertyName("image")] string Image,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("merchantId")] long MerchantId = 0);

/// <summary>走内网 HTTP 调用商品服务的实现。</summary>
public sealed class HttpProductClient : IProductClient
{
    /// <summary>单次批量查询的 SKU 上限。</summary>
    /// <remarks>
    /// 下游两个接口上限不同（快照 200 / 定价 50），统一按**较小的 50** 分片。
    /// 不分片的话，几百个 SKU 会被下游 `Take(50)` 悄悄截断 —— 后段商品的价格与
    /// 可售状态全部缺失，而购物车页面看不出哪里不对（还可能是 URL 过长直接 414）。
    /// </remarks>
    private const int MaxSkuIdsPerRequest = 50;

    private readonly HttpClient _http;
    private readonly ILogger<HttpProductClient> _logger;

    /// <summary>构造客户端。</summary>
    /// <param name="http">指向商品服务的 HttpClient。</param>
    /// <param name="logger">日志器。</param>
    public HttpProductClient(HttpClient http, ILogger<HttpProductClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkuSnapshot>> GetSkuSnapshotsAsync(
        IReadOnlyCollection<long> skuIds, CancellationToken ct = default)
    {
        if (skuIds.Count == 0) return Array.Empty<SkuSnapshot>();

        var result = new List<SkuSnapshot>();

        foreach (var chunk in skuIds.Distinct().Chunk(MaxSkuIdsPerRequest))
        {
            var query = string.Join(',', chunk);
            var response = await _http.GetAsync($"internal/products/skus?skuIds={query}", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<SkuSnapshot>>>(ct);
            if (body is null || !body.Success || body.Data is null)
            {
                _logger.LogError("商品服务返回异常：{Message}", body?.Message ?? "(空响应)");
                continue;
            }

            result.AddRange(body.Data);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, SkuAvailability>> GetSkuAvailabilityAsync(
        IReadOnlyCollection<long> skuIds, CancellationToken ct = default)
    {
        if (skuIds.Count == 0) return new Dictionary<long, SkuAvailability>();

        var result = new Dictionary<long, SkuAvailability>();

        foreach (var chunk in skuIds.Distinct().Chunk(MaxSkuIdsPerRequest))
        {
            var query = string.Join(',', chunk);
            var response = await _http.GetAsync($"internal/products/skus/pricing?skuIds={query}", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<SkuPricing>>>(ct);
            if (body is null || !body.Success || body.Data is null)
            {
                // 跳过这一片而不是抛：商品服务抖动不该让整个购物车打不开。
                // 代价是这一片不刷新价格（显示的还是库里那份），但购物车仍然可用。
                _logger.LogError("商品服务可售状态返回异常：{Message}", body?.Message ?? "(空响应)");
                continue;
            }

            foreach (var item in body.Data)
            {
                result[item.SkuId] = new SkuAvailability(
                    item.SkuId, item.Price, item.SkuEnabled == 1, item.SpuApproved, item.SpuOnShelf,
                    item.MerchantId);
            }
        }

        return result;
    }

    /// <summary>商品服务的定价响应。</summary>
    /// <param name="SkuId">SKU Id。</param>
    /// <param name="ProductId">所属 SPU Id。</param>
    /// <param name="Price">权威售价。</param>
    /// <param name="SkuEnabled">SKU 是否启用，1 启用 / 2 停用。</param>
    /// <param name="SpuApproved">SPU 是否审核通过。</param>
    /// <param name="SpuOnShelf">SPU 是否已上架。</param>
    /// <param name="MerchantId">归属商户 Id。</param>
    /// <param name="PlatformId">归属平台 Id。</param>
    /// <param name="DeliveryType">配送方式。</param>
    private sealed record SkuPricing(
        [property: JsonPropertyName("skuId")] long SkuId,
        [property: JsonPropertyName("productId")] long ProductId,
        [property: JsonPropertyName("price")] decimal Price,
        [property: JsonPropertyName("skuEnabled")] int SkuEnabled,
        [property: JsonPropertyName("spuApproved")] bool SpuApproved,
        [property: JsonPropertyName("spuOnShelf")] bool SpuOnShelf,
        [property: JsonPropertyName("merchantId")] long MerchantId,
        [property: JsonPropertyName("platformId")] long PlatformId,
        [property: JsonPropertyName("deliveryType")] int DeliveryType);
}
