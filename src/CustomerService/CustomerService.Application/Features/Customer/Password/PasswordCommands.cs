using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using Collaboration.Domain.Validation;
using FluentValidation;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.IRepository;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Collaboration.Domain.Security;

namespace CustomerService.Application.Features.Customer.Password;

/// <summary>发送密码重置短信验证码。</summary>
/// <param name="PlatformCode">当前平台编码。</param>
/// <param name="Phone">注册手机号。</param>
public record SendPasswordResetCodeCommand(string PlatformCode, string Phone)
    : IRequest<ApiResponse<PasswordResetCodeSentDto>>;

/// <summary>验证码发送结果。</summary>
/// <param name="ExpiresInSeconds">验证码有效期。</param>
/// <param name="DebugCode">本地调试固定验证码；生产配置关闭时为空。</param>
public sealed record PasswordResetCodeSentDto(int ExpiresInSeconds, string DebugCode = "");

/// <summary>忘记密码：手机号 + 验证码 + 新密码。</summary>
/// <param name="PlatformCode">当前平台编码。</param>
/// <param name="Phone">注册手机号。</param>
/// <param name="Code">短信验证码。</param>
/// <param name="NewPassword">新密码。</param>
public record ResetPasswordCommand(
    string PlatformCode, string Phone, string Code, string NewPassword)
    : IRequest<ApiResponse>;

/// <summary>登录态修改密码。</summary>
/// <param name="CustomerId">客户 Id；经网关时以令牌客户为准。</param>
/// <param name="OldPassword">旧密码。</param>
/// <param name="NewPassword">新密码。</param>
public record ChangeCustomerPasswordCommand(
    long CustomerId, string OldPassword, string NewPassword)
    : IRequest<ApiResponse>;

/// <summary>发送验证码处理器。</summary>
public sealed class SendPasswordResetCodeHandler
    : IRequestHandler<SendPasswordResetCodeCommand, ApiResponse<PasswordResetCodeSentDto>>
{
    private readonly IPlatformDirectoryClient _platforms;
    private readonly ICustomerRepository _customers;
    private readonly IPasswordResetCodeStore _store;
    private readonly IPasswordResetCodeSender _sender;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<SendPasswordResetCodeHandler> _logger;

    /// <summary>构造处理器。</summary>
    /// <param name="platforms">平台目录。</param>
    /// <param name="customers">客户仓储。</param>
    /// <param name="store">验证码存储。</param>
    /// <param name="sender">短信发送器。</param>
    /// <param name="options">密码重置配置。</param>
    /// <param name="logger">日志器。</param>
    public SendPasswordResetCodeHandler(
        IPlatformDirectoryClient platforms,
        ICustomerRepository customers,
        IPasswordResetCodeStore store,
        IPasswordResetCodeSender sender,
        IOptions<PasswordResetOptions> options,
        ILogger<SendPasswordResetCodeHandler> logger)
    {
        _platforms = platforms;
        _customers = customers;
        _store = store;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>执行发送。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>发送结果。</returns>
    public async Task<ApiResponse<PasswordResetCodeSentDto>> Handle(
        SendPasswordResetCodeCommand request, CancellationToken ct)
    {
        var phone = request.Phone.Trim();
        var platform = await _platforms.ResolveAsync(request.PlatformCode.Trim(), ct)
            .ConfigureAwait(false);
        if (platform is null || !platform.IsEnabled)
        {
            return ApiResults.Fail<PasswordResetCodeSentDto>(
                BaseApiResponseCode.BadRequest, "平台不存在或已停用");
        }

        if (!await _store.CanSendAsync(platform.PlatformId, phone, ct).ConfigureAwait(false))
        {
            return ApiResults.Fail<PasswordResetCodeSentDto>(
                BaseApiResponseCode.BusinessError, "验证码发送过于频繁，请稍后再试");
        }

        var customer = await _customers.GetByPhoneAsync(platform.PlatformId, phone, ct)
            .ConfigureAwait(false);
        var expiresIn = Math.Clamp(_options.ExpireMinutes, 1, 30) * 60;

        // 无论手机号是否存在都返回同样的成功文案，避免账号枚举。
        if (customer is not null && customer.Status == CustomerStatuses.Enabled)
        {
            var code = string.IsNullOrWhiteSpace(_options.FixedCode)
                ? Random.Shared.Next(100000, 999999).ToString()
                : _options.FixedCode;
            await _store.SaveAsync(
                platform.PlatformId,
                phone,
                code,
                DateTime.UtcNow.AddSeconds(expiresIn),
                TimeSpan.FromSeconds(Math.Clamp(_options.CooldownSeconds, 10, 300)),
                ct).ConfigureAwait(false);
            await _sender.SendAsync(phone, code, ct).ConfigureAwait(false);
            _logger.LogInformation("密码重置验证码已发送：平台 {PlatformId}", platform.PlatformId);
        }

        var debugCode = _options.ExposeCodeInResponse && customer is not null
            ? _options.FixedCode
            : string.Empty;
        return ApiResults.Ok(
            new PasswordResetCodeSentDto(expiresIn, debugCode),
            "如果该手机号已注册，验证码将发送到手机");
    }
}

/// <summary>忘记密码重置处理器。</summary>
public sealed class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, ApiResponse>
{
    private readonly IPlatformDirectoryClient _platforms;
    private readonly ICustomerRepository _customers;
    private readonly IPasswordResetCodeStore _store;
    private readonly PasswordResetOptions _options;

    /// <summary>构造处理器。</summary>
    /// <param name="platforms">平台目录。</param>
    /// <param name="customers">客户仓储。</param>
    /// <param name="store">验证码存储。</param>
    /// <param name="options">密码重置配置。</param>
    public ResetPasswordHandler(
        IPlatformDirectoryClient platforms,
        ICustomerRepository customers,
        IPasswordResetCodeStore store,
        IOptions<PasswordResetOptions> options)
    {
        _platforms = platforms;
        _customers = customers;
        _store = store;
        _options = options.Value;
    }

    /// <summary>执行重置。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        var platform = await _platforms.ResolveAsync(request.PlatformCode.Trim(), ct)
            .ConfigureAwait(false);
        if (platform is null || !platform.IsEnabled)
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.BadRequest, "平台不存在或已停用");
        }

        var phone = request.Phone.Trim();
        var error = string.Empty;
        var valid = await _store.VerifyAndConsumeAsync(
            platform.PlatformId,
            phone,
            request.Code.Trim(),
            Math.Clamp(_options.MaxAttempts, 1, 10),
            value => error = value,
            ct).ConfigureAwait(false);
        if (!valid)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.BadRequest, error);
        }

        var customer = await _customers.GetByPhoneAsync(platform.PlatformId, phone, ct)
            .ConfigureAwait(false);
        if (customer is null || customer.Status != CustomerStatuses.Enabled)
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.BadRequest, "账号不存在或已停用");
        }

        await _customers.UpdatePasswordAsync(
            customer.Id,
            PasswordHasher.Hash(request.NewPassword),
            ct).ConfigureAwait(false);
        return ApiResponseFactory.Ok("密码已重置，请使用新密码登录");
    }
}

/// <summary>登录态修改密码处理器。</summary>
public sealed class ChangeCustomerPasswordHandler
    : IRequestHandler<ChangeCustomerPasswordCommand, ApiResponse>
{
    private readonly ICustomerRepository _customers;

    /// <summary>构造处理器。</summary>
    /// <param name="customers">客户仓储。</param>
    public ChangeCustomerPasswordHandler(ICustomerRepository customers)
        => _customers = customers;

    /// <summary>执行修改。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应。</returns>
    public async Task<ApiResponse> Handle(
        ChangeCustomerPasswordCommand request, CancellationToken ct)
    {
        var customerId = CustomerScope.Require(request.CustomerId);
        var customer = await _customers.GetByIdAsync(customerId, ct).ConfigureAwait(false);
        if (customer is null
            || !PasswordHasher.Verify(request.OldPassword, customer.PasswordHash))
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.BadRequest, "旧密码不正确");
        }

        if (PasswordHasher.Verify(request.NewPassword, customer.PasswordHash))
        {
            return ApiResponseFactory.Fail(
                BaseApiResponseCode.BadRequest, "新密码不能与旧密码相同");
        }

        await _customers.UpdatePasswordAsync(
            customerId,
            PasswordHasher.Hash(request.NewPassword),
            ct).ConfigureAwait(false);
        return ApiResponseFactory.Ok("密码修改成功");
    }
}

/// <summary>密码命令校验器注册。</summary>
public static class PasswordValidators
{
    /// <summary>注册全部校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddPasswordValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<SendPasswordResetCodeCommand>, SendPasswordResetCodeValidator>();
        services.AddScoped<IValidator<ResetPasswordCommand>, ResetPasswordValidator>();
        services.AddScoped<IValidator<ChangeCustomerPasswordCommand>, ChangeCustomerPasswordValidator>();
    }

    /// <summary>手机号规则。</summary>
    private static IRuleBuilderOptions<T, string> PhoneRule<T>(
        IRuleBuilder<T, string> rule)
        => rule.NotEmpty().MaximumLength(20)
            .Matches(ValidationPatterns.phonePattern).WithMessage("请填写正确的手机号");

    /// <summary>密码规则。</summary>
    private static IRuleBuilderOptions<T, string> PasswordRule<T>(
        IRuleBuilder<T, string> rule)
        => rule.NotEmpty().MinimumLength(ValidationPatterns.passwordMinLength)
            .MaximumLength(64)
            .Must(password => password.Any(char.IsLetter) && password.Any(char.IsDigit))
            .WithMessage("密码至少 8 位，且必须同时包含字母和数字");

    /// <summary>发送验证码校验。</summary>
    private sealed class SendPasswordResetCodeValidator
        : AbstractValidator<SendPasswordResetCodeCommand>
    {
        /// <summary>构造校验器。</summary>
        public SendPasswordResetCodeValidator()
        {
            RuleFor(x => x.PlatformCode).NotEmpty().MaximumLength(32).WithMessage("平台编码不正确");
            PhoneRule(RuleFor(x => x.Phone));
        }
    }

    /// <summary>忘记密码校验。</summary>
    private sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
    {
        /// <summary>构造校验器。</summary>
        public ResetPasswordValidator()
        {
            RuleFor(x => x.PlatformCode).NotEmpty().MaximumLength(32).WithMessage("平台编码不正确");
            PhoneRule(RuleFor(x => x.Phone));
            RuleFor(x => x.Code).NotEmpty().Length(4, 8).WithMessage("验证码格式不正确");
            PasswordRule(RuleFor(x => x.NewPassword));
        }
    }

    /// <summary>修改密码校验。</summary>
    private sealed class ChangeCustomerPasswordValidator
        : AbstractValidator<ChangeCustomerPasswordCommand>
    {
        /// <summary>构造校验器。</summary>
        public ChangeCustomerPasswordValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThanOrEqualTo(0).WithMessage("客户信息不正确");
            RuleFor(x => x.OldPassword).NotEmpty().MaximumLength(64).WithMessage("旧密码必填");
            PasswordRule(RuleFor(x => x.NewPassword));
        }
    }
}
