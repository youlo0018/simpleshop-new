using ProductService.Domain.Entities;

namespace ProductService.Application.Services;

/// <summary>同义词规则文本处理。</summary>
public static class SearchSynonymText
{
    /// <summary>单组最大字符数。</summary>
    public const int MaxLength = 512;

    /// <summary>单组最大词数。</summary>
    public const int MaxTerms = 20;

    /// <summary>规范化为英文逗号分隔并去重。</summary>
    public static string Normalize(string? raw)
        => String.Join(",",
            (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal));

    /// <summary>拆分为词条。</summary>
    public static IReadOnlyList<string> Split(string? terms)
        => (terms ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>检查格式是否有效。</summary>
    public static bool IsValid(string terms)
    {
        if (terms.Length < 3 || terms.Length > MaxLength) return false;
        var parts = Split(terms);
        return parts.Count >= 2 && parts.Count <= MaxTerms;
    }
}

/// <summary>把关键词按规则扩展成多个 ES 查询变体。</summary>
public static class SearchSynonymExpander
{
    private const int MaxVariants = 24;

    /// <summary>判断规则是否适用于当前搜索上下文。</summary>
    public static bool IsApplicable(
        SearchSynonym rule,
        long platformId,
        IReadOnlyCollection<long> categoryIds,
        IReadOnlyCollection<long> brandIds)
    {
        if (rule.Status != SearchSynonymStatuses.Enabled) return false;
        return rule.ScopeType switch
        {
            SearchSynonymScopeTypes.Global => true,
            SearchSynonymScopeTypes.Platform => platformId > 0 && rule.ScopeId == platformId,
            SearchSynonymScopeTypes.Category => categoryIds.Contains(rule.ScopeId),
            SearchSynonymScopeTypes.Brand => brandIds.Contains(rule.ScopeId),
            _ => false
        };
    }

    /// <summary>扩展关键词。</summary>
    /// <param name="keyword">原始关键词。</param>
    /// <param name="rules">已按当前平台 / 分类 / 品牌筛选过的规则。</param>
    /// <returns>按优先级和权重排序的关键词变体，原词永远排第一。</returns>
    public static IReadOnlyList<SearchKeywordVariant> Expand(
        string keyword, IEnumerable<SearchSynonym> rules)
    {
        var original = (keyword ?? string.Empty).Trim();
        if (original.Length == 0) return [];

        var variants = new Dictionary<string, SearchKeywordVariant>(StringComparer.OrdinalIgnoreCase)
        {
            [original] = new(original, 1.0, 0, string.Empty)
        };

        foreach (var rule in rules
                     .Where(a => a.Status == SearchSynonymStatuses.Enabled)
                     .OrderBy(a => a.Priority)
                     .ThenBy(a => a.Id))
        {
            var terms = SearchSynonymText.Split(rule.Terms);
            if (terms.Count < 2) continue;

            var snapshots = variants.Values
                .OrderByDescending(a => a.Boost)
                .ThenBy(a => a.Text)
                .ToArray();

            foreach (var current in snapshots)
            {
                if (rule.RuleType == SearchSynonymRuleTypes.Equivalent)
                {
                    var matched = terms
                        .Where(a => current.Text.Contains(a, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(a => a.Length)
                        .FirstOrDefault();
                    if (matched is null) continue;

                    foreach (var target in terms)
                    {
                        if (String.Equals(target, matched, StringComparison.OrdinalIgnoreCase)) continue;
                        AddVariant(
                            variants,
                            Replace(current.Text, matched, target),
                            WeightOf(rule.RuleType),
                            rule,
                            matched);
                    }
                }
                else
                {
                    var source = terms[0];
                    if (!current.Text.Contains(source, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var target in terms.Skip(1))
                    {
                        AddVariant(
                            variants,
                            Replace(current.Text, source, target),
                            WeightOf(rule.RuleType),
                            rule,
                            source);
                    }
                }

                if (variants.Count >= MaxVariants) break;
            }

            if (variants.Count >= MaxVariants) break;
        }

        return variants.Values
            .OrderByDescending(a => a.Boost)
            .ThenBy(a => a.Text, StringComparer.OrdinalIgnoreCase)
            .Take(MaxVariants)
            .ToArray();
    }

    private static void AddVariant(
        IDictionary<string, SearchKeywordVariant> variants,
        string text,
        double boost,
        SearchSynonym rule,
        string source)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0) return;

        if (variants.TryGetValue(normalized, out var existing))
        {
            if (boost > existing.Boost)
            {
                variants[normalized] = existing with
                {
                    Boost = boost,
                    RuleType = rule.RuleType,
                    SourceTerm = source
                };
            }
            return;
        }

        variants[normalized] = new SearchKeywordVariant(normalized, boost, rule.RuleType, source);
    }

    private static string Replace(string input, string source, string target)
        => input.Replace(source, target, StringComparison.OrdinalIgnoreCase);

    private static double WeightOf(int ruleType) => ruleType switch
    {
        SearchSynonymRuleTypes.Equivalent => 0.90,
        SearchSynonymRuleTypes.OneWay => 0.82,
        SearchSynonymRuleTypes.Correction => 1.0,
        SearchSynonymRuleTypes.Expansion => 0.35,
        _ => 0.50
    };
}
