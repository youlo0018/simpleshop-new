using Collaboration.Domain.Common;
using MediatR;

namespace CustomerService.Application.Features.Customer.Internal;

/// <summary>把尚未归属平台的存量客户回填到指定平台（一次性运维动作，幂等）。</summary>
/// <param name="PlatformId">目标平台 Id，必须大于 0。</param>
/// <remarks>
/// 账号改为「平台内账号」前落库的客户 <c>platform_id = 0</c>，按平台查账号的登录会找不到它们。
/// 本命令只更新 <c>platform_id = 0</c> 的行，重复执行影响 0 行。
/// </remarks>
public record BackfillCustomerPlatformCommand(long PlatformId) : IRequest<ApiResponse<int>>;
