using Collaboration.Domain.Common;
using CustomerService.Domain.IRepository;
using MediatR;

namespace CustomerService.Application.Features.Customer.Internal;

/// <summary>存量客户平台回填处理器。</summary>
/// <remarks>
/// 链路位置：CustomerService <c>/internal/customers/backfill-platform</c>，
/// 只在服务网络内可达（/internal 由内部口令 fail-closed 校验）。
/// 幂等性：只更新 <c>platform_id = 0</c> 的行，重复执行影响 0 行。
/// </remarks>
public sealed class BackfillCustomerPlatformHandler
    : IRequestHandler<BackfillCustomerPlatformCommand, ApiResponse<int>>
{
    private readonly ICustomerRepository _customers;

    /// <summary>构造处理器。</summary>
    /// <param name="customers">客户仓储。</param>
    public BackfillCustomerPlatformHandler(ICustomerRepository customers) => _customers = customers;

    /// <summary>执行回填。</summary>
    /// <param name="request">回填命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>受影响行数。</returns>
    public async Task<ApiResponse<int>> Handle(
        BackfillCustomerPlatformCommand request, CancellationToken ct)
    {
        var affected = await _customers
            .BackfillPlatformAsync(request.PlatformId, ct)
            .ConfigureAwait(false);

        return ApiResults.Ok(affected, $"已回填 {affected} 个存量客户账号");
    }
}
