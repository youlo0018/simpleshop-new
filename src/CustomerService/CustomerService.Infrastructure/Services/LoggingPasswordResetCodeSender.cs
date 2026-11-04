using CustomerService.Application.Services;
using Microsoft.Extensions.Logging;

namespace CustomerService.Infrastructure.Services;

/// <summary>本地开发短信实现：只写日志，不接真实短信供应商。</summary>
public sealed class LoggingPasswordResetCodeSender : IPasswordResetCodeSender
{
    private readonly ILogger<LoggingPasswordResetCodeSender> _logger;

    /// <summary>构造发送器。</summary>
    /// <param name="logger">日志器。</param>
    public LoggingPasswordResetCodeSender(ILogger<LoggingPasswordResetCodeSender> logger)
        => _logger = logger;

    /// <inheritdoc />
    public Task<bool> SendAsync(string phone, string code, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[local-sms] 密码重置验证码 phone={Phone} code={Code}（生产环境必须替换短信通道）",
            phone,
            code);
        return Task.FromResult(true);
    }
}
