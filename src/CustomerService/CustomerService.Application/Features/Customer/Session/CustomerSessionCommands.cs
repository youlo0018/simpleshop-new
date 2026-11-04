using Collaboration.Domain.Common;
using CustomerService.Application.Features.Customer.Login;
using MediatR;

namespace CustomerService.Application.Features.Customer.Session;

/// <summary>刷新客户令牌：用当前会话换一张新令牌（旧会话同时作废）。</summary>
/// <remarks>
/// <b>无请求参数</b>：客户 Id 与会话 Id 只能来自网关注入的租户头（<c>X-Claim-UserId</c> /
/// <c>X-Claim-SessionId</c>）。如果让请求体传客户 Id，就等于给了「拿自己的令牌刷新出别人的会话」
/// 的入口；会话 Id 更必须来自令牌 jti，客户端编造的值不能碰任何一把真实会话键。
/// </remarks>
public sealed record RefreshCustomerTokenCommand : IRequest<ApiResponse<LoginResult>>;

/// <summary>退出登录：删掉当前设备的服务端会话键（幂等）。</summary>
/// <remarks>
/// 与刷新同一口径：只操作「当前令牌对应的那一把键」，所以同一客户的其它设备不受影响。
/// 会话 Id 缺失（旧令牌 / 直连调用）时视为已登出，直接返回成功。
/// </remarks>
public sealed record LogoutCustomerCommand : IRequest<ApiResponse>;
