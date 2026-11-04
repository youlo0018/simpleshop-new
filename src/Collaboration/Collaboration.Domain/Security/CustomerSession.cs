using StackExchange.Redis;

namespace Collaboration.Domain.Security;

/// <summary>客户会话约定（REVIEW 链路 2）：登录写会话键，网关校验并滑动续期，登出删键。</summary>
/// <remarks>
/// <para><b>为什么要有服务端会话</b>：客户令牌是自包含的，验签通过就代表「是我们签发的、还没过期」，
/// 但它表达不了「这个会话已经被登出 / 被踢下线」。没有会话键时，用户点了「退出登录」
/// 只是把本地令牌删掉，令牌在过期前仍然可用 —— 手机丢了、令牌被复制了都收不回来。</para>
///
/// <para><b>键放在共享库</b>（<c>Redis:SharedDatabase</c>）：写入方是 CustomerService、
/// 读取方是网关，两边库号不同就永远读不到，而且失败是静默的。与后台会话吊销同一个约定。</para>
///
/// <para><b>一个会话一把键</b>（<c>{customerId}:{jti}</c>）：同一客户可以在多台设备登录，
/// 登出只删自己那把键，不会把别的设备一起踢掉。</para>
/// </remarks>
public static class CustomerSession
{
    /// <summary>会话键前缀。完整键形如 <c>customer:session:1234567890:9f8e...</c>。</summary>
    public const string KeyPrefix = "customer:session:";

    /// <summary>拼会话键。</summary>
    /// <param name="customerId">客户 Id（令牌 sub）。</param>
    /// <param name="sessionId">会话 Id（令牌 jti）。</param>
    /// <returns>Redis 键。</returns>
    public static string Key(long customerId, string sessionId)
        => $"{KeyPrefix}{customerId}:{sessionId}";

    /// <summary>拼会话键（RedisKey 形式）。</summary>
    /// <param name="customerId">客户 Id。</param>
    /// <param name="sessionId">会话 Id。</param>
    /// <returns>Redis 键。</returns>
    public static RedisKey RedisKey(long customerId, string sessionId)
        => new(Key(customerId, sessionId));
}
