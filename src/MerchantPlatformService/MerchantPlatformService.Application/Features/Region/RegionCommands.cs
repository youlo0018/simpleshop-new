using Collaboration.Domain.Common;
using MediatR;

namespace MerchantPlatformService.Application.Features.Region;

/// <summary>读取某平台的地区地址数据。</summary>
/// <param name="PlatformId">平台 Id。</param>
public record QueryRegionsCommand(long PlatformId) : IRequest<ApiResponse<RegionsResult>>;

/// <summary>小程序按平台编码读取地区地址数据（无需登录）。</summary>
/// <param name="PlatformCode">平台编码，6 位字母。</param>
public record QueryPublicRegionsCommand(string PlatformCode)
    : IRequest<ApiResponse<PublicRegionsResult>>;

/// <summary>保存某平台的地区地址数据。<b>传空串 = 恢复内置默认</b>。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="RegionsJson">三级地区 JSON 数组；<b>空字符串表示清空配置、回落内置默认</b>。</param>
public record SaveRegionsCommand(long PlatformId, string RegionsJson) : IRequest<ApiResponse>;

/// <summary>地区数据读取结果。</summary>
/// <param name="RegionsJson">地区 JSON 数组。</param>
/// <param name="IsCustom">true 用的是平台自定义数据，false 是回落的内置默认。</param>
/// <param name="ByteSize">字节数，用于前端提示是否接近 2MB 上限。</param>
public sealed record RegionsResult(string RegionsJson, bool IsCustom, int ByteSize);

/// <summary>小程序读取地区数据的结果（结构化，不再把 JSON 原文丢给前端解析）。</summary>
/// <param name="Regions">省 / 市 / 区县三级节点。</param>
/// <param name="IsCustom">true 用的是平台自定义数据，false 是内置默认。</param>
public sealed record PublicRegionsResult(IReadOnlyList<RegionNode> Regions, bool IsCustom);

/// <summary>地区节点。</summary>
/// <param name="Name">地区名称（省 / 市 / 区县）。</param>
/// <param name="Children">下级地区；区县为空数组。</param>
public sealed record RegionNode(string Name, IReadOnlyList<RegionNode> Children);
