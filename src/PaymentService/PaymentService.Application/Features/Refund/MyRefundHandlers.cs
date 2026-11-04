using Collaboration.Domain.Common;
using Collaboration.Domain.Context;
using MediatR;
using PaymentService.Application.Services;
using PaymentService.Domain.Entities;
using PaymentService.Domain.IRepository;

namespace PaymentService.Application.Features.Refund;

/// <summary>客户查自己的退款列表。</summary>
public sealed class QueryMyRefundsHandler
    : IRequestHandler<QueryMyRefundsCommand, ApiResponse<PagedRefundDtos>>
{
    private readonly IRefundRepository _refunds;
    private readonly IPlatformNameClient _names;

    /// <summary>构造处理器。</summary>
    /// <param name="refunds">退款仓储。</param>
    /// <param name="names">平台 / 商户名称客户端。</param>
    public QueryMyRefundsHandler(IRefundRepository refunds, IPlatformNameClient names)
    {
        _refunds = refunds;
        _names = names;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>只含当前客户的退款分页。</returns>
    public async Task<ApiResponse<PagedRefundDtos>> Handle(
        QueryMyRefundsCommand request, CancellationToken ct)
    {
        var customerId = CustomerScope.Require(request.CustomerId);
        var page = await _refunds
            .PageByCustomerAsync(customerId, request.Status, request.Page, request.PageSize, ct)
            .ConfigureAwait(false);

        if (page.Items.Count == 0)
        {
            return ApiResults.Ok(new PagedRefundDtos([], page.Total, page.Page, page.PageSize));
        }

        var names = await _names.GetNamesAsync(
            page.Items.Where(a => a.PlatformId > 0).Select(a => a.PlatformId).Distinct().ToArray(),
            page.Items.Where(a => a.MerchantId > 0).Select(a => a.MerchantId).Distinct().ToArray(),
            ct).ConfigureAwait(false);

        var items = new List<RefundDto>(page.Items.Count);
        foreach (var refund in page.Items)
        {
            items.Add(await RefundAssembler
                .BuildAsync(_refunds, names, refund, ct)
                .ConfigureAwait(false));
        }

        return ApiResults.Ok(new PagedRefundDtos(items, page.Total, page.Page, page.PageSize));
    }
}

/// <summary>客户查自己的退款详情。</summary>
public sealed class QueryMyRefundDetailHandler
    : IRequestHandler<QueryMyRefundDetailCommand, ApiResponse<RefundDto>>
{
    private readonly IRefundRepository _refunds;
    private readonly IPlatformNameClient _names;

    /// <summary>构造处理器。</summary>
    /// <param name="refunds">退款仓储。</param>
    /// <param name="names">平台 / 商户名称客户端。</param>
    public QueryMyRefundDetailHandler(IRefundRepository refunds, IPlatformNameClient names)
    {
        _refunds = refunds;
        _names = names;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>退款详情；不存在或非本人返回 404。</returns>
    public async Task<ApiResponse<RefundDto>> Handle(
        QueryMyRefundDetailCommand request, CancellationToken ct)
    {
        var customerId = CustomerScope.Require(request.CustomerId);
        var refund = await _refunds.GetByIdAsync(request.RefundId, ct).ConfigureAwait(false);
        if (refund is null || refund.CustomerId != customerId)
        {
            return ApiResults.Fail<RefundDto>(BaseApiResponseCode.NotFound, "退款单不存在");
        }

        var names = await _names.GetNamesAsync(
            refund.PlatformId > 0 ? [refund.PlatformId] : [],
            refund.MerchantId > 0 ? [refund.MerchantId] : [],
            ct).ConfigureAwait(false);

        return ApiResults.Ok(await RefundAssembler
            .BuildAsync(_refunds, names, refund, ct)
            .ConfigureAwait(false));
    }
}

/// <summary>客户撤销退款申请。</summary>
public sealed class CancelMyRefundHandler
    : IRequestHandler<CancelMyRefundCommand, ApiResponse>
{
    private readonly IRefundRepository _refunds;

    /// <summary>构造处理器。</summary>
    /// <param name="refunds">退款仓储。</param>
    public CancelMyRefundHandler(IRefundRepository refunds) => _refunds = refunds;

    /// <summary>执行撤销。</summary>
    /// <param name="request">命令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回空响应；非本人 404；非待审批返回状态错误。</returns>
    public async Task<ApiResponse> Handle(CancelMyRefundCommand request, CancellationToken ct)
    {
        var customerId = CustomerScope.Require(request.CustomerId);

        // 归属与状态都进条件更新：并发下另一个审批先完成时，本次影响 0 行
        var affected = await _refunds
            .TryCancelAsync(request.RefundId, customerId, ct)
            .ConfigureAwait(false);

        if (affected > 0)
        {
            return ApiResponseFactory.Ok("退款申请已撤销");
        }

        var refund = await _refunds.GetByIdAsync(request.RefundId, ct).ConfigureAwait(false);
        if (refund is null || refund.CustomerId != customerId)
        {
            return ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "退款单不存在");
        }

        // 幂等：已经撤销过再点一次返回成功
        if (refund.Status == RefundStatuses.Withdrawn)
        {
            return ApiResponseFactory.Ok("退款申请已撤销");
        }

        return ApiResponseFactory.Fail(
            BaseApiResponseCode.OrderStateInvalid, "当前退款单状态不可撤销");
    }
}
