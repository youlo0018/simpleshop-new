namespace CustomerService.Application.Services;

/// <summary>短信验证码发送端口。</summary>
public interface IPasswordResetCodeSender
{
    /// <summary>发送密码重置验证码。</summary>
    /// <param name="phone">手机号。</param>
    /// <param name="code">验证码。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送是否成功。</returns>
    Task<bool> SendAsync(string phone, string code, CancellationToken ct = default);
}

/// <summary>密码重置验证码存储端口。</summary>
public interface IPasswordResetCodeStore
{
    /// <summary>检查是否仍处于发送冷却期。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="phone">手机号。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可以发送返回 true。</returns>
    Task<bool> CanSendAsync(long platformId, string phone, CancellationToken ct = default);

    /// <summary>保存验证码与冷却标记。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="phone">手机号。</param>
    /// <param name="code">验证码。</param>
    /// <param name="expiresAt">验证码到期时间。</param>
    /// <param name="cooldown">发送冷却时长。</param>
    /// <param name="ct">取消令牌。</param>
    Task SaveAsync(
        long platformId,
        string phone,
        string code,
        DateTime expiresAt,
        TimeSpan cooldown,
        CancellationToken ct = default);

    /// <summary>校验并一次性消费验证码。</summary>
    /// <param name="platformId">平台 Id。</param>
    /// <param name="phone">手机号。</param>
    /// <param name="code">用户输入验证码。</param>
    /// <param name="maxAttempts">最大错误尝试次数。</param>
    /// <param name="error">校验失败原因输出回调。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>校验是否通过；失败原因写入 <paramref name="error"/>。</returns>
    Task<bool> VerifyAndConsumeAsync(
        long platformId,
        string phone,
        string code,
        int maxAttempts,
        Action<string> error,
        CancellationToken ct = default);
}

/// <summary>短信与密码重置配置。</summary>
public sealed class PasswordResetOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "PasswordReset";

    /// <summary>本地固定验证码；真实短信通道接入前使用。</summary>
    public string FixedCode { get; set; } = "123456";

    /// <summary>验证码有效期分钟数。</summary>
    public int ExpireMinutes { get; set; } = 5;

    /// <summary>发送冷却秒数。</summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>最大错误尝试次数。</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>开发环境是否把固定验证码放在响应中。</summary>
    public bool ExposeCodeInResponse { get; set; }

    /// <summary>短信通道名称。</summary>
    public string Provider { get; set; } = "local-log";
}
