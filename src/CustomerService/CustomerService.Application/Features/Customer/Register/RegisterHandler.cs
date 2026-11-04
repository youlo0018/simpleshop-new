using Collaboration.Domain.Common;
using Collaboration.Domain.Infrastructure;
using Collaboration.Domain.Security;
using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Domain.IRepository;
using MediatR;
// 命名空间段 Features.Customer 会遮蔽同名实体 Customer（CS0118），按
// CODING_STANDARD.md 陷阱 1 的规定用别名绕开。
using CustomerEntity = CustomerService.Domain.Entities.Customer;

namespace CustomerService.Application.Features.Customer.Register;

/// <summary>客户注册处理器。</summary>
/// <remarks>
/// 链路位置：CustomerService /customers/Register。
/// 失败语义：登录名或手机号已占用返回 400；跨服务发放积分失败不阻断注册，只在返回值里标记。
/// 幂等性：非幂等。重复提交同一登录名会因唯一约束失败，由数据库兜住。
/// </remarks>
public sealed class RegisterHandler
    : IRequestHandler<RegisterCommand, ApiResponse<RegisterResult>>
{
    private readonly ICustomerRepository _customers;
    private readonly CustomerTokenService _tokens;
    private readonly IPointGrantClient _points;
    private readonly ICustomerSessionStore _sessions;
    private readonly IPlatformDirectoryClient _platforms;

    /// <summary>构造处理器。</summary>
    /// <param name="customers">客户仓储。</param>
    /// <param name="tokens">客户令牌签发器。</param>
    /// <param name="points">积分发放客户端。</param>
    /// <param name="sessions">客户会话存储（共享 Redis，网关读同一把键）。</param>
    /// <param name="platforms">平台目录客户端（按 platformCode 解析平台）。</param>
    public RegisterHandler(
        ICustomerRepository customers,
        CustomerTokenService tokens,
        IPointGrantClient points,
        ICustomerSessionStore sessions,
        IPlatformDirectoryClient platforms)
    {
        _customers = customers;
        _tokens = tokens;
        _points = points;
        _sessions = sessions;
        _platforms = platforms;
    }

    /// <summary>执行注册。</summary>
    /// <param name="request">注册命令，参数已由 RegisterValidator 校验。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回客户信息与令牌；冲突返回 400。</returns>
    public async Task<ApiResponse<RegisterResult>> Handle(RegisterCommand request, CancellationToken ct)
    {
        // 客户账号是平台内账号：先把 platformCode 换成 platformId，再去查重。
        // 查重必须带平台 —— 不带的话，A 平台已用的登录名会把 B 平台的注册也挡住。
        var platform = await _platforms.ResolveAsync(request.PlatformCode, ct);
        if (platform is null)
        {
            return ApiResults.Fail<RegisterResult>(BaseApiResponseCode.BadRequest, "平台不存在");
        }

        if (!platform.IsEnabled)
        {
            return ApiResults.Fail<RegisterResult>(BaseApiResponseCode.BadRequest, "该平台已停用，不能注册");
        }

        if (await _customers.ExistsByNameAsync(platform.PlatformId, request.CustomerName, ct))
        {
            return ApiResults.Fail<RegisterResult>(BaseApiResponseCode.BadRequest, "该登录名已被注册");
        }

        if (await _customers.ExistsByPhoneAsync(platform.PlatformId, request.Phone, ct))
        {
            return ApiResults.Fail<RegisterResult>(BaseApiResponseCode.BadRequest, "该手机号已被注册");
        }

        var nickName = string.IsNullOrWhiteSpace(request.NickName) ? request.CustomerName : request.NickName.Trim();
        var entity = new CustomerEntity
        {
            Id = SnowflakeId.NewId(),
            PlatformId = platform.PlatformId,
            CustomerName = request.CustomerName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            Phone = request.Phone.Trim(),
            NickName = nickName
        };
        entity.CustomerNo = $"C{entity.Id}";

        var id = await _customers.InsertAsync(entity, ct);
        var pointGranted = await _points.TryGrantRegistrationBonusAsync(id, ct);

        var issued = _tokens.Issue(id, entity.CustomerNo, platform.PlatformId);

        // 会话键写失败时**不回滚注册**（账号已经建好了），但也不能发一个用不了的令牌：
        // 明确告诉用户「注册成功、请用刚注册的账号登录」。
        if (!await _sessions.SaveAsync(id, issued.SessionId, issued.ExpiresAt, ct))
        {
            return ApiResults.Fail<RegisterResult>(
                BaseApiResponseCode.InternalError, "注册成功，但登录会话初始化失败，请用刚注册的账号登录");
        }

        var result = new RegisterResult(
            id.ToString(),
            issued.Token,
            entity.CustomerName,
            entity.NickName,
            entity.CustomerNo,
            pointGranted);

        return ApiResults.Ok(result, "注册成功");
    }
}

