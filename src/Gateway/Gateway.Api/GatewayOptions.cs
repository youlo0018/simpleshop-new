namespace Gateway.Api;

/// <summary>网关配置，对应 AgileConfig 的 Gateway 节。</summary>
public sealed class GatewayOptions
{
    /// <summary>配置文件节名。</summary>
    public const string SectionName = "Gateway";

    /// <summary>与各下游服务共用的内部共享口令。转成 X-Internal-Token 注入下游。</summary>
    /// <remarks>
    /// 下游的 TenantContextMiddleware **只认这个口令**才采信 X-Claim-*。
    /// 网关是唯一合法的写入方——因此入站请求里同名的头必须先被剥掉（见 GatewaySecurityMiddleware）。
    /// </remarks>
    public string InternalToken { get; set; } = string.Empty;

    /// <summary>后台令牌（RS256）配置。</summary>
    public AdminTokenOptions AdminToken { get; set; } = new();

    /// <summary>客户令牌（HS256）配置。</summary>
    public CustomerTokenOptions CustomerToken { get; set; } = new();

    /// <summary>RBAC 配置。</summary>
    public RbacOptions Rbac { get; set; } = new();

    /// <summary>无需登录与权限校验的路径前缀（精确路径列表）。</summary>
    /// <remarks>
    /// 登记 / 登录 / 换令牌 / 图片回源。**只列白名单，不列黑名单**——
    /// 写黑名单意味着「忘了加的接口会默认放行」，那等于没做鉴权。
    /// </remarks>
    public string[] AnonymousPaths { get; set; } =
    [
        "/gateway/customers/Register",
        "/gateway/customers/Login",
        "/gateway/customers/SendPasswordResetCode",
        "/gateway/customers/ResetPassword",
        "/gateway/auth/Token",
        "/gateway/shop/products/List",
        "/gateway/shop/products/Detail",
        // 搜索同样是游客可用的浏览能力（规格 1.2：游客能浏览商品）。
        // 之前漏在这里，于是游客搜商品直接 401，而游客看列表却是 200 ——
        // 症状是「能逛不能搜」，很容易被误当成搜索功能坏了。
        "/gateway/shop/products/Search",
        "/gateway/shop/catalog/CategoryTree",
        "/gateway/shop/catalog/Brands",
        "/gateway/shop/catalog/MerchantCategories",
        // 搜索页的热搜词与联想词：游客可浏览（P05）
        "/gateway/shop/search/HotWords",
        "/gateway/shop/search/Suggest",
        "/gateway/design/Store",
        "/gateway/design/PlatformStore",
        // 平台公开信息：小程序运行期切换平台、渲染商城名 / Logo / 主题色 / 协议页都要用，
        // 切换发生在登录之前，因此必须匿名可调。
        "/gateway/platforms/Public",
        "/gateway/platforms/PublicDetail",
        // 小程序首页公告轮播（多条，匿名可读）
        "/gateway/platforms/notices/Public",
        "/gateway/regions/Public",
        "/gateway/evaluates/List",
        "/gateway/evaluates/Merchant",
        "/gateway/marketing/seckill/sessions/Public",
        // 活动专区 / 商城页活动 Tab：游客可浏览进行中的普通活动
        "/gateway/marketing/activities/Public",
        // 场次曝光埋点：游客也能浏览秒杀频道，埋点必须匿名可用。
        // 少了它前端 PV 埋点会 401（游客直接进不去秒杀页的埋点），
        // 表现为「有曝光但报表 PV 恒为 0」。
        "/gateway/marketing/seckill/sessions/View",
        "/gateway/coupons/Available"
    ];

    /// <summary>无需登录的路径前缀（登录后也能访问，只看公共数据）。</summary>
    public string[] AnonymousPathPrefixes { get; set; } =
    [
        "/gateway/files/Content/",

        // 店铺浏览走**前缀**而不是精确路径：列表是 /merchants/Shop，
        // 详情是 /merchants/Shop/{id}，两条都得放行匿名。
        // 只登记精确路径的话详情会 401 —— 而症状是「店铺列表能看、点进去 401」，
        // 很容易被当成前端路由问题，根本查不到网关这一层。
        // 可见性（只返回审核通过 + 已启用的店铺，VIS-001 / VIS-002）
        // 由商户服务写死筛选，这里只负责放行匿名 —— 两件事分开，各管一头。
        "/gateway/merchants/Shop",
    ];

    /// <summary>
    /// 客户令牌可访问、但接口本身又有后台权限映射的路径。
    /// </summary>
    /// <remarks>
    /// <para>用途只有一个：小程序复用了某个后台能力，但客户令牌没有权限点，
    /// 直接走 RBAC 会被 403。当前只有统一文件上传（发表评价要传图）。</para>
    ///
    /// <para><b>不要把它当成匿名白名单</b>：这里仍然要求先通过客户令牌验签，
    /// 只是跳过「后台权限点」这一步；接口内部仍要自己校验归属（例如上传只写文件元数据，
    /// 不涉及别人的数据）。</para>
    /// </remarks>
    public string[] CustomerAllowedPaths { get; set; } =
    [
        "/gateway/files/Upload",
        "/gateway/refunds/Apply"
    ];

    /// <summary>
    /// 客户令牌可访问的 C 端路径（精确路径）。
    /// </summary>
    /// <remarks>
    /// <para>这些接口<b>刻意不绑后台权限点</b>（绑了会把小程序自己挡掉），
    /// 所以在 RBAC 映射里查不到。网关的判定曾经是「查不到映射 → 放行」，
    /// 于是任何漏配 api_path 的后台接口都会被客户令牌穿透——
    /// <c>/gateway/payments/Simulate</c> 就是这么出的事（顾客把自己的订单标成已支付）。</para>
    ///
    /// <para>现在改成 <b>fail-closed</b>：客户令牌走到未映射路径时，只有命中这张
    /// 显式清单才放行。新增 C 端接口必须在这里登记，漏登记的症状是 403 ——
    /// 一个立刻能发现、且不会泄露数据的失败方式，比默认放行安全得多。</para>
    /// </remarks>
    public string[] CustomerPaths { get; set; } =
    [
        // 客户资料与地址 / 收藏（注册 / 登录是匿名白名单，不在这里判）
        "/gateway/customers/Profile",
        "/gateway/customers/UpdateProfile",
        "/gateway/customers/ChangePassword",
        // 令牌刷新与登出：必须带有效客户令牌（登出后旧令牌立刻失效）
        "/gateway/customers/Refresh",
        "/gateway/customers/Logout",

        // 购物车
        "/gateway/carts/Add",
        "/gateway/carts/List",
        "/gateway/carts/SetQuantity",
        "/gateway/carts/Remove",
        "/gateway/carts/SetChecked",
        "/gateway/carts/Clear",

        // 订单（后台订单走 /gateway/admin/orders/*，不在这张表里）
        "/gateway/orders/Create",
        "/gateway/orders/Preview",
        "/gateway/orders/List",
        "/gateway/orders/Detail",
        "/gateway/orders/Cancel",
        "/gateway/orders/ConfirmReceipt",

        // 支付：创建 / 确认 / 查询。Simulate 是后台接口且有权限映射，不走这张表。
        "/gateway/payments/Create",
        "/gateway/payments/Confirm",
        "/gateway/payments/Query",

        // 积分
        "/gateway/points/SignIn",
        "/gateway/points/Balance",
        "/gateway/points/Records",

        // 评价
        "/gateway/evaluates/My",
        "/gateway/evaluates/Evaluable",
        "/gateway/evaluates/Publish",
        "/gateway/evaluates/Append",

        // 券：结算试算 / 占券 / 核销 / 回退都要经网关给小程序用
        // （Available 是匿名白名单，不在这里判）
        "/gateway/coupons/Claim",
        "/gateway/coupons/My",
        "/gateway/coupons/Settle",
        "/gateway/coupons/Occupy",
        "/gateway/coupons/Consume",
        "/gateway/coupons/Release",

        // 营销：秒杀抢购与结果轮询、商品到手价
        "/gateway/marketing/seckill/grab",
        "/gateway/marketing/activities/FinalPrice",
        "/gateway/marketing/activities/FinalPriceBatch",

        // 客户退款进度：列表 / 详情 / 撤销（纯 C 端，不绑后台权限点）
        "/gateway/refunds/My",
        "/gateway/refunds/MyDetail",
        "/gateway/refunds/CancelMy"
    ];

    /// <summary>
    /// 客户令牌可访问的 C 端路径前缀（带路径参数的接口）。
    /// </summary>
    /// <remarks>与 <see cref="CustomerPaths"/> 是同一套 fail-closed 清单，只是按前缀登记。
    /// 前缀必须写得足够窄：<c>/gateway/customers/</c> 这种会把同前缀下的后台接口一起放进来。</remarks>
    public string[] CustomerPathPrefixes { get; set; } =
    [
        "/gateway/customers/addresses/",
        "/gateway/customers/favorites/",
        "/gateway/marketing/seckill/grab/"
    ];
}

/// <summary>后台令牌（RS256）配置。</summary>
public sealed class AdminTokenOptions
{
    /// <summary>必须匹配的签发方。</summary>
    public string Issuer { get; set; } = "https://simpleshop.local/";

    /// <summary>RS256 签名证书路径。<b>必须与 AuthService 指向同一份文件</b>，网关取其公钥验签。</summary>
    public string SigningCertificatePath { get; set; } = string.Empty;
}

/// <summary>客户令牌（HS256）配置。</summary>
public sealed class CustomerTokenOptions
{
    /// <summary>必须匹配的签发方。</summary>
    public string Issuer { get; set; } = "simpleshop-customer";

    /// <summary>HS256 共享密钥。与 CustomerService 签发时用的是同一个值。</summary>
    /// <remarks>至少 32 字节；短密钥在 HS256 下等于形同虚设，会被离线爆破。</remarks>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// 客户会话的滑动续期时长（小时），应与 CustomerService 的 <c>Jwt:ExpireHours</c> 一致。
    /// </summary>
    /// <remarks>
    /// 网关按它给会话键续期：用户只要还在用，会话就不会因为「静止」过期；
    /// 但**令牌本身**仍按签发时的有效期到期，客户端要用 /customers/Refresh 换新令牌。
    /// </remarks>
    public int SessionHours { get; set; } = 12;
}

/// <summary>网关 RBAC 配置。</summary>
public sealed class RbacOptions
{
    /// <summary>权限中心地址，用于拉「路径 → 权限点」映射。</summary>
    public string PermissionServiceUrl { get; set; } = string.Empty;

    /// <summary>映射缓存秒数。默认 30 秒，与 BUSINESS.md 5.3 一致。</summary>
    public int CacheSeconds { get; set; } = 30;

    /// <summary>
    /// 拉不到映射时的行为。
    /// </summary>
    /// <remarks>
    /// true = 全部放行（不可用，等于没有鉴权）；false = 全部拒绝（可用性换安全）。
    /// 默认 false：宁可服务不可用，也不能让鉴权静默失效——
    /// 那会让任何人在权限中心挂掉时拿到全站权限。
    /// </remarks>
    public bool AllowAllWhenUnavailable { get; set; }
}
