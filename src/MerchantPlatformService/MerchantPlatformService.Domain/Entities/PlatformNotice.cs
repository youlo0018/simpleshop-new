using Collaboration.Domain.Entities;
using FreeSql.DataAnnotations;

namespace MerchantPlatformService.Domain.Entities;

/// <summary>平台公告（小程序首页多条公告轮播）。</summary>
/// <remarks>
/// 继承 <see cref="AdminEntityBase"/>：公告按平台隔离，平台账号只能维护本平台公告。
/// 商户账号无权维护平台公告（Handler 显式拒绝，不依赖权限点是否被误授予）。
/// </remarks>
[Table(Name = "platform_notice")]
public class PlatformNotice : AdminEntityBase
{
    /// <summary>公告标题（列表展示）。</summary>
    [Column(Name = "title", StringLength = 128)]
    public string Title { get; set; } = string.Empty;

    /// <summary>公告内容（小程序轮播展示）。</summary>
    [Column(Name = "content", StringLength = 1000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>点击跳转类型，见 <see cref="NoticeLinkTypes"/>。</summary>
    [Column(Name = "link_type", StringLength = 32)]
    public string LinkType { get; set; } = NoticeLinkTypes.None;

    /// <summary>跳转参数（商品 Id / 分类 Id / 活动 Id / URL）。</summary>
    [Column(Name = "link_param", StringLength = 256)]
    public string LinkParam { get; set; } = string.Empty;

    /// <summary>排序，小的在前。</summary>
    [Column(Name = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>状态：1 启用 / 2 停用。</summary>
    [Column(Name = "status")]
    public int Status { get; set; } = PlatformNoticeStatuses.Enabled;
}

/// <summary>公告状态。</summary>
public static class PlatformNoticeStatuses
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

/// <summary>公告点击跳转类型。</summary>
public static class NoticeLinkTypes
{
    /// <summary>不跳转。</summary>
    public const string None = "none";

    /// <summary>商品详情（参数为 SPU Id）。</summary>
    public const string Product = "product";

    /// <summary>分类商品列表（参数为分类 Id）。</summary>
    public const string Category = "category";

    /// <summary>活动商品列表（参数为活动 Id）。</summary>
    public const string Activity = "activity";

    /// <summary>外部网页 / 小程序页面 URL。</summary>
    public const string Url = "url";

    /// <summary>全部允许的跳转类型。</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { None, Product, Category, Activity, Url };
}
