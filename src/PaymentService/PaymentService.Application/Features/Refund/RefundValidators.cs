using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Domain.Entities;

namespace PaymentService.Application.Features.Refund;

/// <summary>退款命令的校验器注册。</summary>
public static class RefundValidators
{
    /// <summary>注册全部退款校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddRefundValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<ApplyRefundCommand>, ApplyRefundValidator>();
        services.AddScoped<IValidator<ApproveRefundCommand>, ApproveRefundValidator>();
        services.AddScoped<IValidator<RejectRefundCommand>, RejectRefundValidator>();
        services.AddScoped<IValidator<QueryRefundsCommand>, QueryRefundsValidator>();
        services.AddScoped<IValidator<QueryMyRefundsCommand>, QueryMyRefundsValidator>();
        services.AddScoped<IValidator<QueryMyRefundDetailCommand>, QueryMyRefundDetailValidator>();
        services.AddScoped<IValidator<CancelMyRefundCommand>, CancelMyRefundValidator>();
    }

    /// <summary>退款申请校验。</summary>
    private sealed class ApplyRefundValidator : AbstractValidator<ApplyRefundCommand>
    {
        /// <summary>构造校验器。</summary>
        public ApplyRefundValidator()
        {
            RuleFor(x => x.OrderNo).NotEmpty().MaximumLength(64).WithMessage("缺少订单号");
            RuleFor(x => x.Reason).NotEmpty().WithMessage("请填写退款原因")
                .Must(r => r.Trim().Length is >= 2 and <= 200).WithMessage("退款原因需为 2 ~ 200 个字符");
            RuleFor(x => x.Images).Must(l => l is null || l.Count <= 9).WithMessage("最多上传 9 张凭证图片");
            RuleForEach(x => x.Images).MaximumLength(512).WithMessage("凭证图片地址过长");
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(a => a.OrderItemId).GreaterThan(0).WithMessage("订单行信息不正确");
                item.RuleFor(a => a.Amount).GreaterThan(0m).WithMessage("退款金额必须大于 0")
                    .Must(a => a == decimal.Round(a, 2)).WithMessage("退款金额最多两位小数");
                item.RuleFor(a => a.Quantity).GreaterThanOrEqualTo(0).WithMessage("退款件数不能为负数");
            });
        }
    }

    /// <summary>退款审批通过校验。</summary>
    private sealed class ApproveRefundValidator : AbstractValidator<ApproveRefundCommand>
    {
        /// <summary>构造校验器。</summary>
        public ApproveRefundValidator()
        {
            RuleFor(x => x.RefundId).GreaterThan(0).WithMessage("退款单信息不正确");
            // 不再校验 ApproverId：审批人由 Handler 从令牌租户上下文取，
            // 未登录时由 Handler 统一返回「登录状态已失效」，不在这里重复校验一个不存在的字段。
        }
    }

    /// <summary>退款拒绝校验。</summary>
    private sealed class RejectRefundValidator : AbstractValidator<RejectRefundCommand>
    {
        /// <summary>构造校验器。</summary>
        public RejectRefundValidator()
        {
            RuleFor(x => x.RefundId).GreaterThan(0).WithMessage("退款单信息不正确");
            RuleFor(x => x.RejectReason).NotEmpty().WithMessage("请填写拒绝原因")
                .Must(r => r.Trim().Length is >= 2 and <= 200).WithMessage("拒绝原因需为 2 ~ 200 个字符");
        }
    }

    /// <summary>退款单查询校验。</summary>
    private sealed class QueryRefundsValidator : AbstractValidator<QueryRefundsCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryRefundsValidator()
        {
            RuleFor(x => x.Status)
                .Must(s => s is 0 or RefundStatuses.PendingApproval or RefundStatuses.Refunded
                    or RefundStatuses.Rejected or RefundStatuses.Withdrawn)
                .WithMessage("退款单状态不正确");
            RuleFor(x => x.OrderNo).MaximumLength(64).WithMessage("订单号不正确");
            RuleFor(x => x.Keyword).MaximumLength(64).WithMessage("搜索关键词过长");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码不正确");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("每页条数不正确");
        }
    }

    /// <summary>客户退款列表校验。</summary>
    private sealed class QueryMyRefundsValidator : AbstractValidator<QueryMyRefundsCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryMyRefundsValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("请先登录");
            RuleFor(x => x.Status)
                .Must(s => s is 0 or RefundStatuses.PendingApproval or RefundStatuses.Refunded
                    or RefundStatuses.Rejected or RefundStatuses.Withdrawn)
                .WithMessage("退款单状态不正确");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码不正确");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithMessage("每页条数不正确");
        }
    }

    /// <summary>客户退款详情校验。</summary>
    private sealed class QueryMyRefundDetailValidator : AbstractValidator<QueryMyRefundDetailCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryMyRefundDetailValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("请先登录");
            RuleFor(x => x.RefundId).GreaterThan(0).WithMessage("退款单信息不正确");
        }
    }

    /// <summary>客户撤销退款校验。</summary>
    private sealed class CancelMyRefundValidator : AbstractValidator<CancelMyRefundCommand>
    {
        /// <summary>构造校验器。</summary>
        public CancelMyRefundValidator()
        {
            RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("请先登录");
            RuleFor(x => x.RefundId).GreaterThan(0).WithMessage("退款单信息不正确");
        }
    }
}
