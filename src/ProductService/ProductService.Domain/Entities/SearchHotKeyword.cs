using Collaboration.Domain.Entities;
using FreeSql.DataAnnotations;

namespace ProductService.Domain.Entities;

/// <summary>搜索热搜词（按平台维护，小程序搜索页展示）。</summary>
/// <remarks>
/// 继承 <see cref="AdminEntityBase"/>：热搜词是平台级内容，平台账号只能维护本平台；
/// 商户账号由 Handler 显式拒绝（即使权限点被误授予）。
/// </remarks>
[Table(Name = "search_hot_keyword")]
public class SearchHotKeyword : AdminEntityBase
{
    /// <summary>关键词，1-64 字符。</summary>
    [Column(Name = "keyword", StringLength = 64)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>排序，小的在前。</summary>
    [Column(Name = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>状态：1 启用 / 2 停用。</summary>
    [Column(Name = "status")]
    public int Status { get; set; } = SearchHotKeywordStatuses.Enabled;
}

/// <summary>热搜词状态。</summary>
public static class SearchHotKeywordStatuses
{
    /// <summary>启用，小程序可见。</summary>
    public const int Enabled = 1;

    /// <summary>停用，小程序不可见。</summary>
    public const int Disabled = 2;

    /// <summary>取中文名。</summary>
    /// <param name="status">状态值。</param>
    /// <returns>中文名。</returns>
    public static string NameOf(int status) => status == Disabled ? "停用" : "启用";
}
