using Collaboration.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ProductService.Application.Services;
using ProductService.Domain.Entities;
using ProductService.Domain.IRepository;

namespace ProductService.Application.Features.Search;

/// <summary>后台查询同义词规则。</summary>
public record QuerySearchSynonymRulesCommand(
    string Keyword = "", int RuleType = 0, int ScopeType = 0, int Status = -1)
    : IRequest<ApiResponse<List<SearchSynonymRuleDto>>>;

/// <summary>新增或编辑同义词规则。</summary>
public record SaveSearchSynonymRuleCommand(
    long Id = 0,
    string Terms = "",
    int RuleType = SearchSynonymRuleTypes.Equivalent,
    int ScopeType = SearchSynonymScopeTypes.Global,
    long ScopeId = 0,
    int Priority = 100,
    int Status = SearchSynonymStatuses.Enabled,
    string Remark = "") : IRequest<ApiResponse<SearchSynonymRuleDto>>;

/// <summary>删除同义词规则。</summary>
public record DeleteSearchSynonymRuleCommand(long Id) : IRequest<ApiResponse>;

/// <summary>启停同义词规则。</summary>
public record SetSearchSynonymRuleStatusCommand(long Id, int Status) : IRequest<ApiResponse>;

/// <summary>预览关键词会被扩展成哪些查询。</summary>
public record PreviewSearchSynonymCommand(
    string Keyword,
    long PlatformId = 0,
    long CategoryId = 0,
    long BrandId = 0) : IRequest<ApiResponse<List<SearchSynonymPreviewDto>>>;

/// <summary>同义词规则后台 DTO。</summary>
public sealed record SearchSynonymRuleDto(
    string Id,
    string Terms,
    int RuleType,
    string RuleTypeName,
    int ScopeType,
    string ScopeTypeName,
    string ScopeId,
    int Priority,
    int Status,
    string Remark,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>关键词扩展预览。</summary>
public sealed record SearchSynonymPreviewDto(
    string Text,
    double Boost,
    int RuleType,
    string RuleTypeName,
    string SourceTerm);

/// <summary>查询规则。</summary>
public sealed class QuerySearchSynonymRulesHandler
    : IRequestHandler<QuerySearchSynonymRulesCommand, ApiResponse<List<SearchSynonymRuleDto>>>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public QuerySearchSynonymRulesHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse<List<SearchSynonymRuleDto>>> Handle(
        QuerySearchSynonymRulesCommand request, CancellationToken ct)
    {
        var keyword = (request.Keyword ?? string.Empty).Trim();
        var rows = await _synonyms.ListAsync(ct).ConfigureAwait(false);
        var result = rows
            .Where(a => keyword.Length == 0
                        || a.Terms.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || a.Remark.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Where(a => request.RuleType <= 0 || a.RuleType == request.RuleType)
            .Where(a => request.ScopeType <= 0 || a.ScopeType == request.ScopeType)
            .Where(a => request.Status < 0 || a.Status == request.Status)
            .Select(ToDto)
            .ToList();
        return ApiResults.Ok(result);
    }

    internal static SearchSynonymRuleDto ToDto(SearchSynonym rule) => new(
        rule.Id.ToString(),
        rule.Terms,
        rule.RuleType,
        SearchSynonymRuleTypes.NameOf(rule.RuleType),
        rule.ScopeType,
        SearchSynonymScopeTypes.NameOf(rule.ScopeType),
        rule.ScopeId.ToString(),
        rule.Priority,
        rule.Status,
        rule.Remark,
        rule.CreatedAt,
        rule.UpdatedAt);
}

/// <summary>保存规则。</summary>
public sealed class SaveSearchSynonymRuleHandler
    : IRequestHandler<SaveSearchSynonymRuleCommand, ApiResponse<SearchSynonymRuleDto>>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public SaveSearchSynonymRuleHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse<SearchSynonymRuleDto>> Handle(
        SaveSearchSynonymRuleCommand request, CancellationToken ct)
    {
        var terms = SearchSynonymText.Normalize(request.Terms);
        if (!SearchSynonymText.IsValid(terms))
        {
            return ApiResults.Fail<SearchSynonymRuleDto>(
                BaseApiResponseCode.BusinessError,
                $"每组至少 2 个词、最多 {SearchSynonymText.MaxTerms} 个词，长度不超过 {SearchSynonymText.MaxLength}");
        }

        var scopeId = request.ScopeType == SearchSynonymScopeTypes.Global ? 0 : request.ScopeId;
        if (request.ScopeType != SearchSynonymScopeTypes.Global && scopeId <= 0)
        {
            return ApiResults.Fail<SearchSynonymRuleDto>(
                BaseApiResponseCode.BusinessError, "平台 / 分类 / 品牌规则必须选择具体作用域");
        }

        if (await _synonyms.ExistsAsync(
                request.RuleType, request.ScopeType, scopeId, terms, request.Id, ct)
            .ConfigureAwait(false))
        {
            return ApiResults.Fail<SearchSynonymRuleDto>(
                BaseApiResponseCode.Conflict, "同一作用域下已存在相同的同义词规则");
        }

        var rule = request.Id > 0
            ? await _synonyms.GetByIdAsync(request.Id, ct).ConfigureAwait(false)
            : new SearchSynonym();
        if (rule is null)
        {
            return ApiResults.Fail<SearchSynonymRuleDto>(
                BaseApiResponseCode.NotFound, "同义词规则不存在");
        }

        rule.Terms = terms;
        rule.RuleType = request.RuleType;
        rule.ScopeType = request.ScopeType;
        rule.ScopeId = scopeId;
        rule.Priority = request.Priority;
        rule.Status = request.Status;
        rule.Remark = (request.Remark ?? string.Empty).Trim();

        if (request.Id > 0)
        {
            if (!await _synonyms.UpdateAsync(rule, ct).ConfigureAwait(false))
            {
                return ApiResults.Fail<SearchSynonymRuleDto>(
                    BaseApiResponseCode.NotFound, "同义词规则不存在");
            }
        }
        else
        {
            rule.Id = await _synonyms.InsertAsync(rule, ct).ConfigureAwait(false);
        }

        return ApiResults.Ok(
            QuerySearchSynonymRulesHandler.ToDto(rule),
            request.Id > 0 ? "规则已更新，查询时即时生效" : "规则已新增，查询时即时生效");
    }
}

/// <summary>删除规则。</summary>
public sealed class DeleteSearchSynonymRuleHandler
    : IRequestHandler<DeleteSearchSynonymRuleCommand, ApiResponse>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public DeleteSearchSynonymRuleHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse> Handle(
        DeleteSearchSynonymRuleCommand request, CancellationToken ct)
        => await _synonyms.DeleteAsync(request.Id, ct).ConfigureAwait(false)
            ? ApiResponseFactory.Ok("规则已删除，查询时即时生效")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "同义词规则不存在");
}

/// <summary>修改规则状态。</summary>
public sealed class SetSearchSynonymRuleStatusHandler
    : IRequestHandler<SetSearchSynonymRuleStatusCommand, ApiResponse>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public SetSearchSynonymRuleStatusHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse> Handle(
        SetSearchSynonymRuleStatusCommand request, CancellationToken ct)
        => await _synonyms.SetStatusAsync(request.Id, request.Status, ct).ConfigureAwait(false)
            ? ApiResponseFactory.Ok(request.Status == SearchSynonymStatuses.Enabled
                ? "规则已启用，查询时即时生效"
                : "规则已停用，查询时即时生效")
            : ApiResponseFactory.Fail(BaseApiResponseCode.NotFound, "同义词规则不存在");
}

/// <summary>预览关键词扩展。</summary>
public sealed class PreviewSearchSynonymHandler
    : IRequestHandler<PreviewSearchSynonymCommand, ApiResponse<List<SearchSynonymPreviewDto>>>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public PreviewSearchSynonymHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse<List<SearchSynonymPreviewDto>>> Handle(
        PreviewSearchSynonymCommand request, CancellationToken ct)
    {
        var rules = await _synonyms.ListAsync(ct).ConfigureAwait(false);
        if (rules.Count == 0)
        {
            rules = ProductSearchSynonymDefaults.All
                .Select((terms, index) => new SearchSynonym
                {
                    Terms = terms,
                    RuleType = SearchSynonymRuleTypes.Equivalent,
                    ScopeType = SearchSynonymScopeTypes.Global,
                    Priority = index,
                    Status = SearchSynonymStatuses.Enabled
                })
                .ToList();
        }

        var categoryIds = request.CategoryId > 0 ? [request.CategoryId] : Array.Empty<long>();
        var brandIds = request.BrandId > 0 ? [request.BrandId] : Array.Empty<long>();
        var applicable = rules
            .Where(a => SearchSynonymExpander.IsApplicable(
                a, request.PlatformId, categoryIds, brandIds))
            .ToList();

        var variants = SearchSynonymExpander.Expand(request.Keyword, applicable)
            .Select(a => new SearchSynonymPreviewDto(
                a.Text,
                Math.Round(a.Boost, 2),
                a.RuleType,
                a.RuleType == 0 ? "原词" : SearchSynonymRuleTypes.NameOf(a.RuleType),
                a.SourceTerm))
            .ToList();
        return ApiResults.Ok(variants);
    }
}

/// <summary>同义词规则校验器注册。</summary>
public static class SearchSynonymRuleValidators
{
    /// <summary>注册校验器。</summary>
    public static void AddSearchSynonymRuleValidators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<SaveSearchSynonymRuleCommand>, SaveSearchSynonymRuleValidator>();
        services.AddScoped<IValidator<PreviewSearchSynonymCommand>, PreviewSearchSynonymValidator>();
    }

    private sealed class SaveSearchSynonymRuleValidator : AbstractValidator<SaveSearchSynonymRuleCommand>
    {
        public SaveSearchSynonymRuleValidator()
        {
            RuleFor(x => x.RuleType).Must(v => SearchSynonymRuleTypes.All.Contains(v))
                .WithMessage("同义词规则类型无效");
            RuleFor(x => x.ScopeType).Must(v => SearchSynonymScopeTypes.All.Contains(v))
                .WithMessage("同义词作用域无效");
            RuleFor(x => x.Priority).InclusiveBetween(0, 9999)
                .WithMessage("优先级在 0 ~ 9999 之间");
            RuleFor(x => x.Status)
                .Must(v => v is SearchSynonymStatuses.Enabled or SearchSynonymStatuses.Disabled)
                .WithMessage("同义词状态无效");
            RuleFor(x => x.Remark).MaximumLength(256)
                .WithMessage("备注最多 256 个字符");
        }
    }

    private sealed class PreviewSearchSynonymValidator : AbstractValidator<PreviewSearchSynonymCommand>
    {
        public PreviewSearchSynonymValidator()
        {
            RuleFor(x => x.Keyword).NotEmpty().MaximumLength(100)
                .WithMessage("请输入 1 ~ 100 个字符的关键词");
        }
    }
}
