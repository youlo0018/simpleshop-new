using Collaboration.Domain.Common;
using MediatR;

namespace CustomerService.Application.Features.Customer.Login;

/// <summary>客户登录命令。CustomerName 3-64 字符，Password 为已通过校验的明文。</summary>
/// <remarks>
/// PlatformCode 必填（6 位字母）：客户账号是<b>平台内账号</b>，
/// 登录名只在平台内唯一，因此必须先在目标平台里找账号。
/// </remarks>
public record LoginCommand(string CustomerName, string Password, string PlatformCode = "")
    : IRequest<ApiResponse<LoginResult>>;

/// <summary>登录成功返回给前端的信息。</summary>
public record LoginResult(
    string CustomerId,
    string Token,
    string CustomerName,
    string NickName,
    string CustomerNo,
    string Avatar);

