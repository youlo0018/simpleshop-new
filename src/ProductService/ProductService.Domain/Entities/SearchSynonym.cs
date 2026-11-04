using Collaboration.Domain.Entities;
using FreeSql.DataAnnotations;

namespace ProductService.Domain.Entities;

/// <summary>商品搜索同义词规则。</summary>
[Table(Name = "search_synonym")]
public class SearchSynonym : EntityBase
{
    /// <summary>
    /// 逗号分隔的词组。等价词表示整组互通；
    /// 其他规则约定第一个词为来源，其余词为目标。
    /// </summary>
    [Column(Name = "terms", StringLength = 512)]
    public string Terms { get; set; } = string.Empty;

    /// <summary>规则类型，见 <see cref="SearchSynonymRuleTypes"/>。</summary>
    [Column(Name = "rule_type")]
    public int RuleType { get; set; } = SearchSynonymRuleTypes.Equivalent;

    /// <summary>作用域类型，见 <see cref="SearchSynonymScopeTypes"/>。</summary>
    [Column(Name = "scope_type")]
    public int ScopeType { get; set; } = SearchSynonymScopeTypes.Global;

    /// <summary>作用域 Id；全局为 0。</summary>
    [Column(Name = "scope_id")]
    public long ScopeId { get; set; }

    /// <summary>优先级，小的先执行。</summary>
    [Column(Name = "priority")]
    public int Priority { get; set; } = 100;

    /// <summary>状态，见 <see cref="SearchSynonymStatuses"/>。</summary>
    [Column(Name = "status")]
    public int Status { get; set; } = SearchSynonymStatuses.Enabled;

    /// <summary>备注。</summary>
    [Column(Name = "remark", StringLength = 256)]
    public string Remark { get; set; } = string.Empty;

    /// <summary>兼容旧版配置页的排序字段。</summary>
    [Column(Name = "sort_order")]
    public int SortOrder { get; set; }
}

/// <summary>同义词规则类型。</summary>
public static class SearchSynonymRuleTypes
{
    /// <summary>等价词，双向扩展。</summary>
    public const int Equivalent = 1;

    /// <summary>单向映射，只从第一个词扩展到其余词。</summary>
    public const int OneWay = 2;

    /// <summary>错别字 / 俗称纠正。</summary>
    public const int Correction = 3;

    /// <summary>类目扩展词，只做低权重召回。</summary>
    public const int Expansion = 4;

    /// <summary>全部类型。</summary>
    public static readonly int[] All = [Equivalent, OneWay, Correction, Expansion];

    /// <summary>取中文名称。</summary>
    public static string NameOf(int value) => value switch
    {
        Equivalent => "等价词",
        OneWay => "单向映射",
        Correction => "错别字纠正",
        Expansion => "类目扩展",
        _ => "未知"
    };
}

/// <summary>同义词作用域。</summary>
public static class SearchSynonymScopeTypes
{
    /// <summary>全局。</summary>
    public const int Global = 1;

    /// <summary>平台。</summary>
    public const int Platform = 2;

    /// <summary>分类。</summary>
    public const int Category = 3;

    /// <summary>品牌。</summary>
    public const int Brand = 4;

    /// <summary>全部作用域。</summary>
    public static readonly int[] All = [Global, Platform, Category, Brand];

    /// <summary>取中文名称。</summary>
    public static string NameOf(int value) => value switch
    {
        Global => "全局",
        Platform => "平台",
        Category => "分类",
        Brand => "品牌",
        _ => "未知"
    };
}

/// <summary>同义词状态。</summary>
public static class SearchSynonymStatuses
{
    /// <summary>停用。</summary>
    public const int Disabled = 0;

    /// <summary>启用。</summary>
    public const int Enabled = 1;
}
