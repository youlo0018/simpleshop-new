using Collaboration.Domain.Common;
using MediatR;
using Microsoft.Extensions.Options;
using ProductService.Application.Services;
using ProductService.Domain.IRepository;

namespace ProductService.Application.Features.Search;

/// <summary>读取商品搜索同义词。</summary>
public record QuerySearchSynonymsQuery : IRequest<ApiResponse<SearchSynonymsDto>>;

/// <summary>兼容旧版配置页：整批替换全局等价词。</summary>
public record SaveSearchSynonymsCommand(IReadOnlyList<string> Synonyms)
    : IRequest<ApiResponse<SearchSynonymsDto>>;

/// <summary>同义词配置。</summary>
public sealed record SearchSynonymsDto(IReadOnlyList<string> Synonyms);

/// <summary>读取同义词处理器。</summary>
public sealed class QuerySearchSynonymsHandler
    : IRequestHandler<QuerySearchSynonymsQuery, ApiResponse<SearchSynonymsDto>>
{
    private readonly ISearchSynonymRepository _synonyms;
    private readonly ProductSearchOptions _options;

    /// <summary>构造处理器。</summary>
    /// <param name="synonyms">同义词仓储。</param>
    /// <param name="options">搜索默认配置。</param>
    public QuerySearchSynonymsHandler(
        ISearchSynonymRepository synonyms, IOptions<ProductSearchOptions> options)
    {
        _synonyms = synonyms;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ApiResponse<SearchSynonymsDto>> Handle(
        QuerySearchSynonymsQuery request, CancellationToken ct)
    {
        var rows = await _synonyms.ListAsync(ct).ConfigureAwait(false);
        var terms = rows.Count > 0
            ? rows.Select(a => a.Terms).Distinct(StringComparer.Ordinal).ToArray()
            : _options.Synonyms;
        return ApiResults.Ok(new SearchSynonymsDto(terms));
    }
}

/// <summary>保存同义词；查询时读取，保存后立即生效。</summary>
public sealed class SaveSearchSynonymsHandler
    : IRequestHandler<SaveSearchSynonymsCommand, ApiResponse<SearchSynonymsDto>>
{
    private readonly ISearchSynonymRepository _synonyms;

    /// <summary>构造处理器。</summary>
    public SaveSearchSynonymsHandler(ISearchSynonymRepository synonyms) => _synonyms = synonyms;

    /// <inheritdoc />
    public async Task<ApiResponse<SearchSynonymsDto>> Handle(
        SaveSearchSynonymsCommand request, CancellationToken ct)
    {
        var normalized = Normalize(request.Synonyms);
        if (normalized.Count == 0)
        {
            return ApiResults.Fail<SearchSynonymsDto>(
                BaseApiResponseCode.BusinessError, "至少配置一组商品搜索同义词");
        }
        await _synonyms.ReplaceAllAsync(normalized, ct).ConfigureAwait(false);

        return ApiResults.Ok(
            new SearchSynonymsDto(normalized),
            $"已保存 {normalized.Count} 组同义词，查询时即时生效");
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? source)
    {
        if (source is null || source.Count == 0) return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in source)
        {
            var terms = SearchSynonymText.Normalize(raw);
            if (!SearchSynonymText.IsValid(terms)) continue;
            if (seen.Add(terms)) result.Add(terms);
        }
        return result;
    }
}
