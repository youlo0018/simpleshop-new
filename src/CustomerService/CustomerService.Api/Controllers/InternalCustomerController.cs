using Collaboration.Domain.Common;
using CustomerService.Application.Features.Customer.Internal;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>客户内部接口。网关不路由 /internal 前缀，只在服务网络内可达。</summary>
/// <remarks>
/// 与其它服务的 /internal 一样：由 <c>TenantContextMiddleware</c> 做口令 fail-closed 校验，
/// 不带或不带对 <c>X-Internal-Token</c> 一律 401。
/// </remarks>
[ApiController]
[Route("internal/customers")]
public sealed class InternalCustomerController : ControllerBase
{
    private readonly IMediator _mediator;

    /// <summary>构造控制器。</summary>
    /// <param name="mediator">MediatR 入口。</param>
    public InternalCustomerController(IMediator mediator) => _mediator = mediator;

    /// <summary>把尚未归属平台的存量客户回填到指定平台（幂等）。</summary>
    /// <param name="command">回填命令，含目标平台 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>受影响行数。</returns>
    /// <remarks>
    /// 账号改为「平台内账号」前落库的客户没有平台归属，按平台查账号的登录找不到它们。
    /// 运维先用平台编码解析出 platformId，再调这里一次性回填；重复调用影响 0 行。
    /// </remarks>
    [HttpPost("backfill-platform")]
    public Task<ApiResponse<int>> BackfillPlatform(
        [FromBody] BackfillCustomerPlatformCommand command, CancellationToken ct)
        => _mediator.Send(command, ct);
}
