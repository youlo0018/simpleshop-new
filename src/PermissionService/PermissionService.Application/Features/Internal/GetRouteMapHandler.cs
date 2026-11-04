using Collaboration.Domain.Common;
using MediatR;
using PermissionService.Domain.IRepository;

namespace PermissionService.Application.Features.Internal;

/// <summary>网关 RBAC 映射查询处理器。</summary>
public sealed class GetRouteMapHandler : IRequestHandler<GetRouteMapCommand, ApiResponse<List<RouteMapEntry>>>
{
    private readonly IPermissionRepository _permissions;

    /// <summary>构造处理器。</summary>
    /// <param name="permissions">权限点仓储。全局过滤已在注册时装好（软删 / 租户）。</param>
    public GetRouteMapHandler(IPermissionRepository permissions) => _permissions = permissions;

    /// <summary>执行查询。</summary>
    /// <param name="request">查询命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>全部启用且绑定了路径的权限点映射。</returns>
    /// <remarks>
    /// 查询走仓储（Application 层不得依赖 IFreeSql，CODING_STANDARD 1）：
    /// <see cref="IPermissionRepository.QueryAllAsync"/> 带软删 / 租户全局过滤，
    /// 与权限树用的是同一份可见性口径。
    /// </remarks>
    public async Task<ApiResponse<List<RouteMapEntry>>> Handle(GetRouteMapCommand request, CancellationToken ct)
    {
        var rows = (await _permissions.QueryAllAsync(onlyEnabled: true, ct).ConfigureAwait(false))
            .Where(a => !string.IsNullOrEmpty(a.ApiPath))
            .ToList();

        // 一个权限点可以绑**多条**路径（BUSINESS.md 5.4「ApiPath（绑定的 /gateway/* 路径，可多个）」），
        // 用逗号分隔存。这里必须展开成「一条路径一条映射」：
        // 网关的字典是 path → code，一行只能给一个 code，
        // 不展开的话「装修商户」那种没有公共前缀的一组端点就绑不上。
        //
        // 之前只把 ApiPath 当成单条路径，于是这类需求只能退而求其次绑一个公共前缀，
        // 而前缀要么覆盖过大（把平台装修也放进去），要么根本不存在。
        var entries = rows
            .SelectMany(a => SplitPaths(a.ApiPath).Select(p => new RouteMapEntry(p, a.Code.Trim())))
            // 同一路径理论上只绑定一个权限点；真出现重复时保留排序靠前的那个，
            // 不能让网关随机挑一个——那会让「这个接口到底要什么权限」变成薛定谔的。
            .GroupBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ApiResults.Ok(entries);
    }

    /// <summary>把逗号分隔的路径串拆成单条路径。</summary>
    /// <param name="apiPath">原始 ApiPath 文本，可为空。</param>
    /// <returns>去掉空白项并 trim 后的路径列表。</returns>
    /// <remarks>
    /// 用**逗号**而不是换行 / 分号：ApiPath 是单行 varchar(512)，
    /// 换行在管理界面里保存时会被悄悄截断或折行，逗号是最不容易出意外的。
    /// 同时兼容中英文逗号——后台表单里中文输入法打出来的是「，」。
    /// </remarks>
    private static IReadOnlyList<string> SplitPaths(string? apiPath)
        => (apiPath ?? string.Empty)
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToArray();
}
