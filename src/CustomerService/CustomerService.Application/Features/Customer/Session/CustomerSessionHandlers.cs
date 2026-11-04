using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using CustomerService.Application.Features.Customer.Login;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.IRepository;
using MediatR;
using Microsoft.Extensions.Logging;

namespace CustomerService.Application.Features.Customer.Session;

/// <summary>刷新客户令牌处理器。</summary>
/// <remarks>
/// <para>链路位置：小程序在令牌临近过期时调 <c>/gateway/customers/Refresh</c>，
/// 网关验签（令牌必须还没过期）并校验会话键后转发到这里。</para>
///
/// <para><b>换发而不是延长</b>：签发一张带新 jti 的令牌，写新会话键，再删旧键。
/// 旧令牌从此在网关 401 —— 即使它被复制过，刷新这一步就把攻击者手里的令牌作废了。
/// 这也是「滑动续期」只作用于会话键、不作用于令牌本身的原因：
/// 令牌有效期固定，客户端靠刷新换新，服务端始终掌握每一把会话的生死。</para>
/// </remarks>
public sealed class RefreshCustomerTokenHandler
    : IRequestHandler<RefreshCustomerTokenCommand, ApiResponse<LoginResult>>
{
    private readonly ICustomerRepository _customers;
    private readonly CustomerTokenService _tokens;
    private readonly ICustomerSessionStore _sessions;
    private readonly ILogger<RefreshCustomerTokenHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="customers">客户仓储。</param>
    /// <param name="tokens">客户令牌签发器。</param>
    /// <param name="sessions">客户会话存储。</param>
    /// <param name="logger">日志器。</param>
    public RefreshCustomerTokenHandler(
        ICustomerRepository customers,
        CustomerTokenService tokens,
        ICustomerSessionStore sessions,
        ILogger<RefreshCustomerTokenHandler> logger)
    {
        _customers = customers;
        _tokens = tokens;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>执行刷新。</summary>
    /// <param name="request">空命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新令牌与资料；上下文缺失返回 401，账号异常返回 400/404。</returns>
    public async Task<ApiResponse<LoginResult>> Handle(
        RefreshCustomerTokenCommand request, CancellationToken ct)
    {
        var context = TenantContextHolder.Current;

        // 网关已校验过会话，这里再判一次上下文：直连服务端口（没有 X-Claim-*）时必须拒绝，
        // 否则任何人调一下 Refresh 就等于拿到了「无中生有签发客户令牌」的能力。
        if (!context.IsCustomer || context.UserId <= 0 || string.IsNullOrWhiteSpace(context.SessionId))
        {
            return ApiResults.Fail<LoginResult>(BaseApiResponseCode.Unauthorized, "请先登录");
        }

        var customer = await _customers.GetByIdAsync(context.UserId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return ApiResults.Fail<LoginResult>(BaseApiResponseCode.NotFound, "客户不存在");
        }

        if (customer.Status == CustomerStatuses.Disabled)
        {
            return ApiResults.Fail<LoginResult>(BaseApiResponseCode.BadRequest, "账号已停用，请联系客服");
        }

        var issued = _tokens.Issue(customer.Id, customer.CustomerNo, customer.PlatformId);

        // 顺序不能反：先写新会话，再删旧会话。
        // 反过来的话，「删旧成功、写新失败」会让用户手里新旧令牌全废 —— 直接被登出；
        // 当前顺序最坏情况是旧键删失败，旧令牌多活一小段（可接受，且有日志）。
        if (!await _sessions.SaveAsync(customer.Id, issued.SessionId, issued.ExpiresAt, ct))
        {
            return ApiResults.Fail<LoginResult>(
                BaseApiResponseCode.InternalError, "刷新失败，请稍后重试");
        }

        if (!await _sessions.DeleteAsync(customer.Id, context.SessionId, ct))
        {
            // 不阻断刷新：新令牌已经可用。旧键删不掉只是让旧令牌多活一会儿。
            _logger.LogWarning(
                "刷新后旧会话删除失败：客户 {CustomerId} 旧会话 {SessionId}", customer.Id, context.SessionId);
        }

        var result = new LoginResult(
            customer.Id.ToString(),
            issued.Token,
            customer.CustomerName,
            customer.NickName,
            customer.CustomerNo,
            customer.Avatar);

        return ApiResults.Ok(result, "刷新成功");
    }
}

/// <summary>退出登录处理器。</summary>
/// <remarks>
/// 幂等：重复登出、旧令牌（没有会话 Id）都返回成功 —— 用户点两次「退出」不该看到报错。
/// 删键失败时返回 500 而不是假装成功：此时令牌在网关仍然有效，
/// 「告诉用户已登出」是一种安全假象，必须让客户端知道这次没成功。
/// </remarks>
public sealed class LogoutCustomerHandler : IRequestHandler<LogoutCustomerCommand, ApiResponse>
{
    private readonly ICustomerSessionStore _sessions;

    /// <summary>构造处理器。</summary>
    /// <param name="sessions">客户会话存储。</param>
    public LogoutCustomerHandler(ICustomerSessionStore sessions) => _sessions = sessions;

    /// <summary>执行登出。</summary>
    /// <param name="request">空命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功（含重复登出）；Redis 不可用返回 500。</returns>
    public async Task<ApiResponse> Handle(LogoutCustomerCommand request, CancellationToken ct)
    {
        var context = TenantContextHolder.Current;

        // 没有会话 Id 说明这是改造前的旧令牌或直连调用：服务端本来就没有对应会话键，视为已登出。
        if (!context.IsCustomer || context.UserId <= 0 || string.IsNullOrWhiteSpace(context.SessionId))
        {
            return ApiResponseFactory.Ok();
        }

        if (!await _sessions.DeleteAsync(context.UserId, context.SessionId, ct))
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.InternalError, "退出登录失败，请稍后重试");
        }

        return ApiResponseFactory.Ok();
    }
}
