using Collaboration.Domain.Common;
using Collaboration.Domain.Validation;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MerchantPlatformService.Domain.Entities;

namespace MerchantPlatformService.Application.Features.Notice;

/// <summary>新建平台公告。</summary>
/// <param name="Title">标题，1-128 字符。</param>
/// <param name="Content">内容，1-1000 字符。</param>
/// <param name="LinkType">跳转类型，见 <see cref="NoticeLinkTypes"/>。</param>
/// <param name="LinkParam">跳转参数（商品 / 分类 / 活动 Id 或 URL）。</param>
/// <param name="SortOrder">排序，小的在前。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
/// <param name="PlatformId">平台 Id；平台账号忽略，超管必填。</param>
public record CreateNoticeCommand(
    string Title,
    string Content,
    string LinkType = NoticeLinkTypes.None,
    string LinkParam = "",
    int SortOrder = 0,
    int Status = PlatformNoticeStatuses.Enabled,
    long PlatformId = 0) : IRequest<ApiResponse<long>>;

/// <summary>编辑平台公告。</summary>
/// <param name="NoticeId">公告 Id。</param>
/// <param name="Title">标题。</param>
/// <param name="Content">内容。</param>
/// <param name="LinkType">跳转类型。</param>
/// <param name="LinkParam">跳转参数。</param>
/// <param name="SortOrder">排序。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
public record UpdateNoticeCommand(
    long NoticeId,
    string Title,
    string Content,
    string LinkType = NoticeLinkTypes.None,
    string LinkParam = "",
    int SortOrder = 0,
    int Status = PlatformNoticeStatuses.Enabled) : IRequest<ApiResponse>;

/// <summary>删除公告（软删）。</summary>
/// <param name="NoticeId">公告 Id。</param>
public record DeleteNoticeCommand(long NoticeId) : IRequest<ApiResponse>;

/// <summary>启停公告。</summary>
/// <param name="NoticeId">公告 Id。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
public record SetNoticeStatusCommand(long NoticeId, int Status) : IRequest<ApiResponse>;

/// <summary>后台公告分页。</summary>
/// <param name="Status">状态，0 表示不限。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">每页条数。</param>
public record QueryNoticesCommand(int Status = 0, int Page = 1, int PageSize = 20)
    : IRequest<ApiResponse<PagedResult<NoticeListItemDto>>>;

/// <summary>小程序读取某平台启用中的公告（无需登录）。</summary>
/// <param name="PlatformCode">平台编码，6 位字母。</param>
/// <param name="Limit">最多返回条数，1-50。</param>
public record QueryPublicNoticesCommand(string PlatformCode = "", int Limit = 10)
    : IRequest<ApiResponse<List<PublicNoticeDto>>>;

/// <summary>后台公告列表项。</summary>
public sealed record NoticeListItemDto(
    string NoticeId, string Title, string Content, string LinkType, string LinkParam,
    int SortOrder, int Status, string StatusName, long PlatformId, string CreatedAt);

/// <summary>小程序公告项。</summary>
public sealed record PublicNoticeDto(
    string NoticeId, string Title, string Content, string LinkType, string LinkParam);

/// <summary>公告命令校验器注册。</summary>
public static class NoticeValidators
{
    /// <summary>注册全部公告校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddNoticeValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateNoticeCommand>, CreateNoticeValidator>();
        services.AddScoped<IValidator<UpdateNoticeCommand>, UpdateNoticeValidator>();
        services.AddScoped<IValidator<DeleteNoticeCommand>, DeleteNoticeValidator>();
        services.AddScoped<IValidator<SetNoticeStatusCommand>, SetNoticeStatusValidator>();
        services.AddScoped<IValidator<QueryNoticesCommand>, QueryNoticesValidator>();
        services.AddScoped<IValidator<QueryPublicNoticesCommand>, QueryPublicNoticesValidator>();
    }

    /// <summary>新建公告校验。</summary>
    private sealed class CreateNoticeValidator : AbstractValidator<CreateNoticeCommand>
    {
        /// <summary>构造校验器。</summary>
        public CreateNoticeValidator()
        {
            RuleFor(x => x.Title).NotEmpty().Length(1, 128).WithMessage("公告标题必须为 1-128 个字符");
            RuleFor(x => x.Content).NotEmpty().Length(1, 1000).WithMessage("公告内容必须为 1-1000 个字符");
            RuleFor(x => x.LinkType).Must(t => NoticeLinkTypes.All.Contains(t)).WithMessage("跳转类型不正确");
            RuleFor(x => x.LinkParam).MaximumLength(256).WithMessage("跳转参数最多 256 个字符");
            RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("排序不能为负数");
            RuleFor(x => x.Status).Must(s => s is PlatformNoticeStatuses.Enabled or PlatformNoticeStatuses.Disabled)
                .WithMessage("公告状态不正确");
        }
    }

    /// <summary>编辑公告校验。</summary>
    private sealed class UpdateNoticeValidator : AbstractValidator<UpdateNoticeCommand>
    {
        /// <summary>构造校验器。</summary>
        public UpdateNoticeValidator()
        {
            RuleFor(x => x.NoticeId).GreaterThan(0).WithMessage("公告信息不正确");
            RuleFor(x => x.Title).NotEmpty().Length(1, 128).WithMessage("公告标题必须为 1-128 个字符");
            RuleFor(x => x.Content).NotEmpty().Length(1, 1000).WithMessage("公告内容必须为 1-1000 个字符");
            RuleFor(x => x.LinkType).Must(t => NoticeLinkTypes.All.Contains(t)).WithMessage("跳转类型不正确");
            RuleFor(x => x.LinkParam).MaximumLength(256).WithMessage("跳转参数最多 256 个字符");
            RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("排序不能为负数");
            RuleFor(x => x.Status).Must(s => s is PlatformNoticeStatuses.Enabled or PlatformNoticeStatuses.Disabled)
                .WithMessage("公告状态不正确");
        }
    }

    /// <summary>删除公告校验。</summary>
    private sealed class DeleteNoticeValidator : AbstractValidator<DeleteNoticeCommand>
    {
        /// <summary>构造校验器。</summary>
        public DeleteNoticeValidator()
            => RuleFor(x => x.NoticeId).GreaterThan(0).WithMessage("公告信息不正确");
    }

    /// <summary>启停公告校验。</summary>
    private sealed class SetNoticeStatusValidator : AbstractValidator<SetNoticeStatusCommand>
    {
        /// <summary>构造校验器。</summary>
        public SetNoticeStatusValidator()
        {
            RuleFor(x => x.NoticeId).GreaterThan(0).WithMessage("公告信息不正确");
            RuleFor(x => x.Status).Must(s => s is PlatformNoticeStatuses.Enabled or PlatformNoticeStatuses.Disabled)
                .WithMessage("公告状态不正确");
        }
    }

    /// <summary>后台公告分页校验。</summary>
    private sealed class QueryNoticesValidator : AbstractValidator<QueryNoticesCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryNoticesValidator()
        {
            RuleFor(x => x.Status).InclusiveBetween(0, 2).WithMessage("公告状态不正确");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码不正确");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("每页条数不正确");
        }
    }

    /// <summary>小程序公告校验。</summary>
    private sealed class QueryPublicNoticesValidator : AbstractValidator<QueryPublicNoticesCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryPublicNoticesValidator()
        {
            RuleFor(x => x.PlatformCode).Matches(ValidationPatterns.platformCodePattern)
                .WithMessage("平台编码不正确");
            RuleFor(x => x.Limit).InclusiveBetween(1, 50).WithMessage("返回条数必须在 1 ~ 50 之间");
        }
    }
}
