using FluentValidation;

namespace CustomerService.Application.Features.Customer.Internal;

/// <summary>存量客户平台回填校验器。</summary>
/// <remarks>覆盖 PlatformId：必须是正数，避免把存量账号回填到「平台 0」这种不存在的归属。</remarks>
public class BackfillCustomerPlatformValidator : AbstractValidator<BackfillCustomerPlatformCommand>
{
    /// <summary>构造校验规则。</summary>
    public BackfillCustomerPlatformValidator()
    {
        RuleFor(x => x.PlatformId).GreaterThan(0).WithMessage("平台信息不正确");
    }
}
