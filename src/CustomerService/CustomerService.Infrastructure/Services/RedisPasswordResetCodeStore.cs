using Collaboration.Domain.Configuration;
using CustomerService.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CustomerService.Infrastructure.Services;

/// <summary>密码重置验证码的 Redis 实现。</summary>
public sealed class RedisPasswordResetCodeStore : IPasswordResetCodeStore
{
    private readonly IDatabase _redis;
    private readonly ILogger<RedisPasswordResetCodeStore> _logger;

    /// <summary>构造存储。</summary>
    /// <param name="redis">Redis 连接。</param>
    /// <param name="options">Redis 配置。</param>
    /// <param name="logger">日志器。</param>
    public RedisPasswordResetCodeStore(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisPasswordResetCodeStore> logger)
    {
        _redis = redis.GetDatabase(options.Value.SharedDatabase);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CanSendAsync(
        long platformId, string phone, CancellationToken ct = default)
        => !await _redis.KeyExistsAsync(CooldownKey(platformId, phone)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveAsync(
        long platformId,
        string phone,
        string code,
        DateTime expiresAt,
        TimeSpan cooldown,
        CancellationToken ct = default)
    {
        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromMinutes(1);
        await _redis.StringSetAsync(CodeKey(platformId, phone), code, ttl).ConfigureAwait(false);
        await _redis.KeyDeleteAsync(AttemptsKey(platformId, phone)).ConfigureAwait(false);
        await _redis.StringSetAsync(CooldownKey(platformId, phone), "1", cooldown).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAndConsumeAsync(
        long platformId,
        string phone,
        string code,
        int maxAttempts,
        Action<string> error,
        CancellationToken ct = default)
    {
        var key = CodeKey(platformId, phone);
        var saved = await _redis.StringGetAsync(key).ConfigureAwait(false);
        if (saved.IsNullOrEmpty)
        {
            error("验证码已过期，请重新获取");
            return false;
        }

        var attempts = await _redis.StringIncrementAsync(AttemptsKey(platformId, phone)).ConfigureAwait(false);
        if (attempts == 1)
        {
            await _redis.KeyExpireAsync(AttemptsKey(platformId, phone), TimeSpan.FromMinutes(10))
                .ConfigureAwait(false);
        }

        if (attempts > Math.Max(1, maxAttempts))
        {
            await _redis.KeyDeleteAsync(key).ConfigureAwait(false);
            error("验证码错误次数过多，请重新获取");
            return false;
        }

        if (!string.Equals(saved.ToString(), code, StringComparison.Ordinal))
        {
            error("验证码不正确");
            return false;
        }

        await _redis.KeyDeleteAsync(key).ConfigureAwait(false);
        await _redis.KeyDeleteAsync(AttemptsKey(platformId, phone)).ConfigureAwait(false);
        return true;
    }

    /// <summary>验证码键。</summary>
    private static string CodeKey(long platformId, string phone)
        => $"customer:password-reset:{platformId}:{phone}:code";

    /// <summary>尝试次数键。</summary>
    private static string AttemptsKey(long platformId, string phone)
        => $"customer:password-reset:{platformId}:{phone}:attempts";

    /// <summary>发送冷却键。</summary>
    private static string CooldownKey(long platformId, string phone)
        => $"customer:password-reset:{platformId}:{phone}:cooldown";
}
