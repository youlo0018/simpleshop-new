namespace CustomerService.Application.Services;

/// <summary>平台目录客户端：把客户端持有的平台编码解析成平台信息。</summary>
/// <remarks>
/// 客户账号是平台内账号，注册 / 登录必须先确定平台。平台数据归 MerchantPlatformService，
/// 这里只做「按编码换 Id + 状态」的协议适配；平台是否允许登录由调用方按业务规则判断。
/// </remarks>
public interface IPlatformDirectoryClient
{
    /// <summary>按平台编码解析平台。</summary>
    /// <param name="platformCode">平台编码，6 位字母，忽略大小写。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>平台快照；平台不存在返回 null。只读、幂等。</returns>
    Task<PlatformSnapshot?> ResolveAsync(string platformCode, CancellationToken ct = default);

    /// <summary>按 Id 集合批量取平台名（后台列表冗余展示用，避免显示裸 Id）。</summary>
    /// <param name="platformIds">平台 Id 集合，建议一次带上当前页用到的全部 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>平台 Id → 名称；查不到的 Id 不出现在结果里。只读、幂等。</returns>
    Task<IReadOnlyDictionary<long, string>> GetPlatformNamesAsync(
        IReadOnlyCollection<long> platformIds, CancellationToken ct = default);
}

/// <summary>平台快照。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="PlatformCode">平台编码。</param>
/// <param name="MallName">商城名称。</param>
/// <param name="Status">平台状态：1 启用 / 2 停用。</param>
public sealed record PlatformSnapshot(long PlatformId, string PlatformCode, string MallName, int Status)
{
    /// <summary>平台是否启用。</summary>
    public bool IsEnabled => Status == 1;
}
