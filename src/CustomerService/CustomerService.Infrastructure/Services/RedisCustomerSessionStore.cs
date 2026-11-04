using Collaboration.Domain.Configuration;
using Collaboration.Domain.Security;
using CustomerService.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CustomerService.Infrastructure.Services;

/// <summary>客户会话存储的 Redis 实现（写共享库，网关读同一把键）。</summary>
public sealed class RedisCustomerSessionStore : ICustomerSessionStore
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisCustomerSessionStore> _logger;

    /// <summary>构造存储。</summary>
    /// <param name="redis">Redis 连接。</param>
    /// <param name="redisOptions">Redis 配置，取共享库号（必须与网关一致）。</param>
    /// <param name="logger">日志器。</param>
    public RedisCustomerSessionStore(
        IConnectionMultiplexer redis, IOptions<RedisOptions> redisOptions,
        ILogger<RedisCustomerSessionStore> logger)
    {
        _redis = redis.GetDatabase(redisOptions.Value.SharedDatabase);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        long customerId, string sessionId, DateTime expiresAt, CancellationToken ct = default)
    {
        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromMinutes(1);

        try
        {
            await _redis
                .StringSetAsync(CustomerSession.RedisKey(customerId, sessionId), "1", ttl)
                .ConfigureAwait(false);
            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "写客户会话失败：客户 {CustomerId} 会话 {SessionId}", customerId, sessionId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        long customerId, string sessionId, CancellationToken ct = default)
    {
        try
        {
            await _redis.KeyDeleteAsync(CustomerSession.RedisKey(customerId, sessionId)).ConfigureAwait(false);
            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "删客户会话失败：客户 {CustomerId} 会话 {SessionId}", customerId, sessionId);
            return false;
        }
    }
}
