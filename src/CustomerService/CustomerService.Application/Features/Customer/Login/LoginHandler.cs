using Collaboration.Domain.Common;
using Collaboration.Domain.Security;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.IRepository;
using MediatR;

namespace CustomerService.Application.Features.Customer.Login;

/// <summary>客户登录处理器。</summary>
/// <remarks>
/// 链路位置：CustomerService /customers/Login，签发 HS256 客户令牌（DATA_SPEC 4）。
/// 失败语义：登录名不存在与密码错误**返回同一条消息**，避免账号枚举。
/// 幂等性：重复登录各自签发令牌，不影响彼此。
/// </remarks>
public sealed class LoginHandler : IRequestHandler<LoginCommand, ApiResponse<LoginResult>>
{
    private const string RejectMessage = "登录名或密码不正确";

    private readonly ICustomerRepository _customers;
    private readonly CustomerTokenService _tokens;
    private readonly ICustomerSessionStore _sessions;
    private readonly IPlatformDirectoryClient _platforms;

    /// <summary>构造处理器。</summary>
    /// <param name="customers">客户仓储。</param>
    /// <param name="tokens">客户令牌签发器。</param>
    /// <param name="sessions">客户会话存储（共享 Redis，网关读同一把键）。</param>
    /// <param name="platforms">平台目录客户端（按 platformCode 解析平台）。</param>
    public LoginHandler(
        ICustomerRepository customers, CustomerTokenService tokens, ICustomerSessionStore sessions,
        IPlatformDirectoryClient platforms)
    {
        _customers = customers;
        _tokens = tokens;
        _sessions = sessions;
        _platforms = platforms;
    }

    /// <summary>执行登录。</summary>
    /// <param name="request">登录命令，格式已由 LoginValidator 校验。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回令牌与资料；失败返回 400 与统一拒绝消息。</returns>
    public async Task<ApiResponse<LoginResult>> Handle(LoginCommand request, CancellationToken ct)
    {
        // 平台内账号：先在指定平台里找账号。平台不存在 / 停用时直接拒绝，
        // 与「账号不存在」用同一类 400，不泄露该平台上是否存在这个登录名。
        var platform = await _platforms.ResolveAsync(request.PlatformCode, ct);
        if (platform is null || !platform.IsEnabled)
        {
            return ApiResults.Fail<LoginResult>(BaseApiResponseCode.BadRequest, RejectMessage);
        }

        var customer = await _customers.GetByNameAsync(
            platform.PlatformId, request.CustomerName.Trim(), ct);
        if (customer is null || !PasswordHasher.Verify(request.Password, customer.PasswordHash))
        {
            return ApiResults.Fail<LoginResult>(BaseApiResponseCode.BadRequest, RejectMessage);
        }

        // 🔴 停用检查必须在**密码校验之后**，不能提到最前面。
        // 提到最前面的话，「账号不存在」与「账号已停用」会给出不同文案，
        // 等于公开了哪些登录名真实存在（可被用来枚举账号）。
        // 放在密码校验之后：只有**已经证明自己知道密码**的人才会看到这句提示，
        // 不泄露任何账号存在性；而用户也能明确知道「不是密码错，是被停用了」，
        // 而不是对着一条「登录名或密码不正确」反复重试。
        if (customer.Status == CustomerStatuses.Disabled)
        {
            return ApiResults.Fail<LoginResult>(
                BaseApiResponseCode.BadRequest, "账号已停用，请联系客服");
        }

        customer.LastLoginAt = DateTime.UtcNow;
        await _customers.UpdateAsync(customer, ct);

        var issued = _tokens.Issue(customer.Id, customer.CustomerNo, platform.PlatformId);

        // 🔴 会话键必须先写成功再把令牌发出去：网关会按它校验每个客户请求，
        // 写失败还发令牌等于给用户一个「拿在手里但每次请求都 401」的死令牌。
        if (!await _sessions.SaveAsync(customer.Id, issued.SessionId, issued.ExpiresAt, ct))
        {
            return ApiResults.Fail<LoginResult>(
                BaseApiResponseCode.InternalError, "登录服务繁忙，请稍后重试");
        }

        var result = new LoginResult(
            customer.Id.ToString(),
            issued.Token,
            customer.CustomerName,
            customer.NickName,
            customer.CustomerNo,
            customer.Avatar);

        return ApiResults.Ok(result, "登录成功");
    }
}

