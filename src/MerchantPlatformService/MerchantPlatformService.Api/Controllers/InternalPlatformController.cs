using Collaboration.Domain.Common;
using MerchantPlatformService.Domain.Entities;
using MerchantPlatformService.Domain.IRepository;
using Microsoft.AspNetCore.Mvc;

namespace MerchantPlatformService.Api.Controllers;

/// <summary>平台内部接口。网关不路由 /internal 前缀。</summary>
/// <remarks>
/// 运费是**平台级配置**（BUSINESS.md 6.2），但运费是订单金额的一部分，
/// 算账的职责在订单服务。两边都不管的话，运费就只能由客户端上报 ——
/// 而客户端报什么，商家就收什么。
/// </remarks>
[ApiController]
[Route("internal/platforms")]
public sealed class InternalPlatformController : ControllerBase
{
    private readonly IPlatformRepository _platforms;
    private readonly IMerchantRepository _merchants;

    /// <summary>构造控制器。</summary>
    /// <param name="platforms">平台仓储。</param>
    /// <param name="merchants">商户仓储。</param>
    public InternalPlatformController(IPlatformRepository platforms, IMerchantRepository merchants)
    {
        _platforms = platforms;
        _merchants = merchants;
    }

    /// <summary>取某平台的运费配置（订单服务算运费时调用）。</summary>
    /// <param name="platformId">平台 Id；0 表示平台自营。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>平台运费与满额包邮门槛。</returns>
    /// <remarks>
    /// <para><b>查不到平台时返回 0 元配置而不是 404。</b>
    /// 平台自营（platformId = 0）本来就没有对应的 platform 行，而运费配置在这套设计里
    /// 始终是「默认 0 元」。抛错会让自营单直接下不了 —— 用一个后端根本没法查、
    /// 只能去数据库里看的事实去拦住下单，是把配置问题变成业务故障。</para>
    /// </remarks>
    [HttpGet("shipping-config")]
    public async Task<ActionResult<ApiResponse<ShippingConfigSnapshot>>> ShippingConfig(
        [FromQuery] long platformId, CancellationToken ct)
    {
        var platform = await _platforms.GetByIdAsync(platformId, ct).ConfigureAwait(false);
        var row = platform is null
            ? null
            : new ShippingConfigSnapshot(
                platform.Id, platform.ShippingFee, platform.FreeShippingThreshold);

        return Ok(ApiResults.Ok(row ?? new ShippingConfigSnapshot(platformId, 0m, 0m)));
    }

    /// <summary>按 Id 集合取平台 / 商户的**显示名**（其它服务的列表冗余展示用）。</summary>
    /// <param name="command">平台 Id 与商户 Id 集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>命中的名称清单；查不到的 Id 不会出现在结果里。</returns>
    /// <remarks>
    /// <para><b>为什么要有它</b>：DATA_SPEC 4.3 要求订单 / 退款等列表**冗余返回名称**，
    /// 而名称只存在于本服务。没有这个接口，其它服务只能回一个雪花 Id ——
    /// 运营在列表上看到的就是一串数字（用户明确要求「直接显示 name，不要显示 id」）。</para>
    ///
    /// <para>返回的是<b>展示名</b>（平台名 / 店铺名），不是凭证类数据；
    /// 与其它 /internal 接口同一条边界：网关不路由 /internal，只在服务网络内可达。</para>
    /// </remarks>
    [HttpPost("Names")]
    public async Task<ActionResult<ApiResponse<NameLookupResult>>> Names(
        [FromBody] NameLookupCommand command, CancellationToken ct)
    {
        var platforms = (await _platforms
                .ListNamesByIdsAsync(command.PlatformIds ?? Array.Empty<long>(), ct)
                .ConfigureAwait(false))
            .Select(a => new NamePair(a.Id, a.Name))
            .ToList();

        var merchants = (await _merchants
                .ListNamesByIdsAsync(command.MerchantIds ?? Array.Empty<long>(), ct)
                .ConfigureAwait(false))
            .Select(a => new NamePair(a.Id, a.Name))
            .ToList();

        return Ok(ApiResults.Ok(new NameLookupResult(platforms, merchants)));
    }

    /// <summary>按平台编码解析平台（客户注册 / 登录校验平台用）。</summary>
    /// <param name="platformCode">平台编码，6 位字母，忽略大小写。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>平台 Id、编码、商城名与状态；不存在返回 404。</returns>
    /// <remarks>
    /// 客户账号是平台内账号：注册 / 登录必须先把客户端持有的 platformCode 换成 platformId。
    /// 这里<b>不替调用方决定「停用平台能不能登录」</b>——只回状态，由客户服务按业务规则处理
    /// （当前口径：停用平台禁止登录）。
    /// </remarks>
    [HttpGet("resolve")]
    public async Task<ActionResult<ApiResponse<PlatformResolveResult>>> Resolve(
        [FromQuery] string platformCode, CancellationToken ct)
    {
        var platform = await _platforms
            .GetByCodeAsync(platformCode ?? string.Empty, ct)
            .ConfigureAwait(false);

        if (platform is null)
        {
            return Ok(ApiResults.Fail<PlatformResolveResult>(
                BaseApiResponseCode.NotFound, "平台不存在"));
        }

        return Ok(ApiResults.Ok(new PlatformResolveResult(
            platform.Id,
            platform.PlatformCode,
            platform.MallName,
            platform.Status,
            PlatformStatuses.NameOf(platform.Status))));
    }
}

/// <summary>平台解析结果。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="PlatformCode">平台编码。</param>
/// <param name="MallName">商城名称。</param>
/// <param name="Status">平台状态：1 启用 / 2 停用。</param>
/// <param name="StatusName">状态中文名。</param>
public sealed record PlatformResolveResult(
    long PlatformId, string PlatformCode, string MallName, int Status, string StatusName);

/// <summary>名称查询请求。</summary>
/// <param name="PlatformIds">平台 Id 集合。</param>
/// <param name="MerchantIds">商户 Id 集合。</param>
public sealed record NameLookupCommand(
    IReadOnlyList<long>? PlatformIds = null,
    IReadOnlyList<long>? MerchantIds = null);

/// <summary>名称查询结果。</summary>
/// <param name="Platforms">平台 Id → 名称。</param>
/// <param name="Merchants">商户 Id → 名称。</param>
public sealed record NameLookupResult(
    IReadOnlyList<NamePair> Platforms,
    IReadOnlyList<NamePair> Merchants);

/// <summary>一个 Id → 名称。</summary>
/// <param name="Id">平台或商户 Id。</param>
/// <param name="Name">展示名。</param>
public sealed record NamePair(long Id, string Name);

/// <summary>平台运费配置快照。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="ShippingFee">平台运费，仅对实物快递收取，两位小数。</param>
/// <param name="FreeShippingThreshold">满额包邮门槛，按商品实付判定；0 表示不启用。</param>
public sealed record ShippingConfigSnapshot(long PlatformId, decimal ShippingFee, decimal FreeShippingThreshold);

