using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Domain.Entities;

namespace ProductService.Application.Features.Search;

/// <summary>新建热搜词。</summary>
/// <param name="Keyword">关键词，1-64 字符。</param>
/// <param name="SortOrder">排序，小的在前。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
/// <param name="PlatformId">平台 Id；平台账号忽略，超管必填。</param>
public record CreateHotKeywordCommand(
    string Keyword, int SortOrder = 0, int Status = SearchHotKeywordStatuses.Enabled, long PlatformId = 0)
    : IRequest<ApiResponse<long>>;

/// <summary>编辑热搜词。</summary>
/// <param name="KeywordId">热搜词 Id。</param>
/// <param name="Keyword">关键词。</param>
/// <param name="SortOrder">排序。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
public record UpdateHotKeywordCommand(
    long KeywordId, string Keyword, int SortOrder = 0,
    int Status = SearchHotKeywordStatuses.Enabled) : IRequest<ApiResponse>;

/// <summary>删除热搜词（软删）。</summary>
/// <param name="KeywordId">热搜词 Id。</param>
public record DeleteHotKeywordCommand(long KeywordId) : IRequest<ApiResponse>;

/// <summary>启停热搜词。</summary>
/// <param name="KeywordId">热搜词 Id。</param>
/// <param name="Status">1 启用 / 2 停用。</param>
public record SetHotKeywordStatusCommand(long KeywordId, int Status) : IRequest<ApiResponse>;

/// <summary>后台热搜词分页。</summary>
/// <param name="Status">状态，0 表示不限。</param>
/// <param name="Keyword">按关键词模糊匹配。</param>
/// <param name="Page">页码。</param>
/// <param name="PageSize">每页条数。</param>
public record QueryHotKeywordsAdminCommand(
    int Status = 0, string Keyword = "", int Page = 1, int PageSize = 20)
    : IRequest<ApiResponse<PagedResult<HotKeywordListItemDto>>>;

/// <summary>小程序热搜词（无需登录）。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="Limit">最多返回条数。</param>
public record QueryHotKeywordsCommand(long PlatformId, int Limit = 10)
    : IRequest<ApiResponse<List<string>>>;

/// <summary>小程序搜索联想（无需登录）。</summary>
/// <param name="PlatformId">平台 Id。</param>
/// <param name="Keyword">已输入的关键词。</param>
/// <param name="Limit">最多返回条数。</param>
public record QuerySearchSuggestCommand(long PlatformId, string Keyword = "", int Limit = 10)
    : IRequest<ApiResponse<List<string>>>;

/// <summary>后台热搜词列表项。</summary>
public sealed record HotKeywordListItemDto(
    string KeywordId, string Keyword, int SortOrder, int Status, string StatusName,
    long PlatformId, string CreatedAt);

/// <summary>热搜词命令校验器注册。</summary>
public static class HotKeywordValidators
{
    /// <summary>注册全部热搜词校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddHotKeywordValidators(IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateHotKeywordCommand>, CreateHotKeywordValidator>();
        services.AddScoped<IValidator<UpdateHotKeywordCommand>, UpdateHotKeywordValidator>();
        services.AddScoped<IValidator<DeleteHotKeywordCommand>, DeleteHotKeywordValidator>();
        services.AddScoped<IValidator<SetHotKeywordStatusCommand>, SetHotKeywordStatusValidator>();
        services.AddScoped<IValidator<QueryHotKeywordsAdminCommand>, QueryHotKeywordsAdminValidator>();
        services.AddScoped<IValidator<QueryHotKeywordsCommand>, QueryHotKeywordsValidator>();
        services.AddScoped<IValidator<QuerySearchSuggestCommand>, QuerySearchSuggestValidator>();
    }

    /// <summary>新建热搜词校验。</summary>
    private sealed class CreateHotKeywordValidator : AbstractValidator<CreateHotKeywordCommand>
    {
        /// <summary>构造校验器。</summary>
        public CreateHotKeywordValidator()
        {
            RuleFor(x => x.Keyword).NotEmpty().Length(1, 64).WithMessage("关键词必须为 1-64 个字符");
            RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("排序不能为负数");
            RuleFor(x => x.Status).Must(s => s is SearchHotKeywordStatuses.Enabled or SearchHotKeywordStatuses.Disabled)
                .WithMessage("热搜词状态不正确");
        }
    }

    /// <summary>编辑热搜词校验。</summary>
    private sealed class UpdateHotKeywordValidator : AbstractValidator<UpdateHotKeywordCommand>
    {
        /// <summary>构造校验器。</summary>
        public UpdateHotKeywordValidator()
        {
            RuleFor(x => x.KeywordId).GreaterThan(0).WithMessage("热搜词信息不正确");
            RuleFor(x => x.Keyword).NotEmpty().Length(1, 64).WithMessage("关键词必须为 1-64 个字符");
            RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("排序不能为负数");
            RuleFor(x => x.Status).Must(s => s is SearchHotKeywordStatuses.Enabled or SearchHotKeywordStatuses.Disabled)
                .WithMessage("热搜词状态不正确");
        }
    }

    /// <summary>删除热搜词校验。</summary>
    private sealed class DeleteHotKeywordValidator : AbstractValidator<DeleteHotKeywordCommand>
    {
        /// <summary>构造校验器。</summary>
        public DeleteHotKeywordValidator()
            => RuleFor(x => x.KeywordId).GreaterThan(0).WithMessage("热搜词信息不正确");
    }

    /// <summary>启停热搜词校验。</summary>
    private sealed class SetHotKeywordStatusValidator : AbstractValidator<SetHotKeywordStatusCommand>
    {
        /// <summary>构造校验器。</summary>
        public SetHotKeywordStatusValidator()
        {
            RuleFor(x => x.KeywordId).GreaterThan(0).WithMessage("热搜词信息不正确");
            RuleFor(x => x.Status).Must(s => s is SearchHotKeywordStatuses.Enabled or SearchHotKeywordStatuses.Disabled)
                .WithMessage("热搜词状态不正确");
        }
    }

    /// <summary>后台热搜词分页校验。</summary>
    private sealed class QueryHotKeywordsAdminValidator : AbstractValidator<QueryHotKeywordsAdminCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryHotKeywordsAdminValidator()
        {
            RuleFor(x => x.Status).InclusiveBetween(0, 2).WithMessage("热搜词状态不正确");
            RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage("页码不正确");
            RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("每页条数不正确");
            RuleFor(x => x.Keyword).MaximumLength(64).WithMessage("关键词过长");
        }
    }

    /// <summary>小程序热搜词校验。</summary>
    private sealed class QueryHotKeywordsValidator : AbstractValidator<QueryHotKeywordsCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryHotKeywordsValidator()
        {
            RuleFor(x => x.PlatformId).GreaterThan(0).WithMessage("平台信息不正确");
            RuleFor(x => x.Limit).InclusiveBetween(1, 50).WithMessage("返回条数必须在 1 ~ 50 之间");
        }
    }

    /// <summary>小程序搜索联想校验。</summary>
    private sealed class QuerySearchSuggestValidator : AbstractValidator<QuerySearchSuggestCommand>
    {
        /// <summary>构造校验器。</summary>
        public QuerySearchSuggestValidator()
        {
            RuleFor(x => x.PlatformId).GreaterThan(0).WithMessage("平台信息不正确");
            RuleFor(x => x.Keyword).MaximumLength(64).WithMessage("关键词过长");
            RuleFor(x => x.Limit).InclusiveBetween(1, 20).WithMessage("返回条数必须在 1 ~ 20 之间");
        }
    }
}
