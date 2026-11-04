using Collaboration.Domain.Security;
using StackExchange.Redis;

namespace Gateway.Api;

/// <summary>客户令牌的服务端会话校验：读共享库里的会话键，命中时滑动续期。</summary>
/// <remarks>
/// <para>客户令牌是自包含的，验签只能证明「是我们签发的、还没过期」，
/// 证明不了「这个会话已经被登出 / 被踢下线」。会话键由 CustomerService 在登录时写入，
/// 这里逐请求校验：键在 → 放行并续期；键没了 → 401（登出、被清理、或令牌是旧版本）。</para>
///
/// <para><b>Redis 不可用时拒绝（fail-closed）</b>：读不到会话状态就放行，
/// 等于「Redis 一挂，所有已登出的令牌全部复活」。与后台会话吊销同一套取舍。</para>
/// </remarks>
public sealed class CustomerSessionChecker
{
    private readonly IDatabase _redis;
    private readonly ILogger<CustomerSessionChecker> _logger;

    /// <summary>构造检查器。</summary>
    /// <param name="redis">Redis 连接。</param>
    /// <param name="sharedDatabase">共享逻辑库索引，必须与 CustomerService 写入时一致。</param>
    /// <param name="logger">日志器。</param>
    public CustomerSessionChecker(
        IConnectionMultiplexer redis, int sharedDatabase, ILogger<CustomerSessionChecker> logger)
    {
        _redis = redis.GetDatabase(sharedDatabase);
        _logger = logger;
    }

    /// <summary>校验结果。</summary>
    /// <param name="Valid">会话是否有效。</param>
    /// <param name="StoreUnavailable">会话存储是否不可用（调用方应回 503 而不是 401）。</param>
    public readonly record struct SessionCheckResult(bool Valid, bool StoreUnavailable);

    /// <summary>校验会话并滑动续期。</summary>
    /// <param name="customerId">客户 Id（令牌 sub）。</param>
    /// <param name="sessionId">会话 Id（令牌 jti）。</param>
    /// <param name="slidingTtl">续期后的存活时长（与客户令牌有效期一致）。</param>
    /// <returns>校验结果。</returns>
    public async Task<SessionCheckResult> CheckAsync(
        long customerId, string sessionId, TimeSpan slidingTtl)
    {
        // 没有 jti 的客户令牌是本次改造之前签发的：一律视为失效（用户重新登录即可）
        if (customerId <= 0 || string.IsNullOrWhiteSpace(sessionId))
        {
            return new SessionCheckResult(false, false);
        }

        var key = CustomerSession.RedisKey(customerId, sessionId);

        try
        {
            if (!await _redis.KeyExistsAsync(key).ConfigureAwait(false))
            {
                return new SessionCheckResult(false, false);
            }

            // 滑动续期：只要用户还在用，会话就不会因为「静止」而过期。
            // 续期失败不拦请求（键还在，只是 TTL 没延长），下一轮还有机会。
            await _redis.KeyExpireAsync(key, slidingTtl).ConfigureAwait(false);
            return new SessionCheckResult(true, false);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "读取客户 {CustomerId} 的会话失败，按拒绝处理", customerId);
            return new SessionCheckResult(false, true);
        }
    }
}
