namespace CustomerService.Application.Services;

/// <summary>客户会话存储（Redis 共享库，网关要读同一把键）。</summary>
/// <remarks>
/// 键的格式与共享库约定见 <c>Collaboration.Domain.Security.CustomerSession</c>。
/// 存储失败一律返回 false（而不是抛）：调用方要据此决定「登录算不算成功」——
/// 会话没写进去的令牌在网关会被拒，发给用户等于给了一个死令牌。
/// </remarks>
public interface ICustomerSessionStore
{
    /// <summary>写会话键。</summary>
    /// <param name="customerId">客户 Id。</param>
    /// <param name="sessionId">会话 Id（令牌 jti）。</param>
    /// <param name="expiresAt">会话过期时刻（UTC），与令牌有效期一致。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>写入成功返回 true；Redis 不可用返回 false。</returns>
    Task<bool> SaveAsync(
        long customerId, string sessionId, DateTime expiresAt, CancellationToken ct = default);

    /// <summary>删会话键（登出、刷新后删旧会话）。</summary>
    /// <param name="customerId">客户 Id。</param>
    /// <param name="sessionId">会话 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>删除成功（含键本来就不存在）返回 true；Redis 不可用返回 false。</returns>
    Task<bool> DeleteAsync(long customerId, string sessionId, CancellationToken ct = default);
}
