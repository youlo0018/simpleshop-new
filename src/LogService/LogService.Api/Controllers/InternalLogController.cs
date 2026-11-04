using Collaboration.Domain.Common;
using LogService.Application;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace LogService.Api.Controllers;

/// <summary>日志内部接口，供其它服务做报表聚合。网关不路由 /internal 前缀。</summary>
/// <remarks>
/// 与 <see cref="LogAdminController"/> 分开：后台查询走 <c>/logs/*</c>（权限点 <c>log:read</c>），
/// 这里是服务间调用（秒杀报表取 PV），不经过网关、也不该绑后台权限点。
/// </remarks>
[ApiController]
[Route("internal/logs")]
public sealed class InternalLogController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public InternalLogController(IMediator mediator) => _mediator = mediator;

    /// <summary>按埋点路径统计每个秒杀场次的 PV。</summary>
    /// <param name="command">统计条件：埋点路径 + 场次 Id 集合 + 时间区间。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>每个场次的 PV；没有流量的场次返回 0。</returns>
    [HttpPost("pv-by-session")]
    public Task<ApiResponse<IReadOnlyList<SessionPvCount>>> PvBySession(
        [FromBody] QueryPvCountBySessionCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
