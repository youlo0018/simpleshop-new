using System.Security.Cryptography;
using System.Text;
using Collaboration.Domain.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Collaboration.Web;

/// <summary>
/// 从网关注入的 X-Claim-* 请求头构造 <see cref="TenantContext"/>，供 AOP 过滤与审计字段使用。
/// </summary>
/// <remarks>
/// 踩过的坑：之前根本没有这个中间件，网关注入了 X-Claim-* 也没人读，
/// TenantContextHolder.Current 永远是匿名上下文。后果是
/// SuperAdminBehavior 一律 403（超管做不了权限点/角色的增删改），审计字段也永远是空的。
///
/// 安全边界：这些头<b>只在带对内部口令时</b>才采信。
/// 服务端口可能被绕过网关直接访问，无条件信任头等于开了一个后门。
/// 口令没配时一律按匿名处理并告警——宁可「没人是超管」，也不能「人人都是超管」。
///
/// <c>/internal/**</c> 同样要口令：它没有 X-Claim-* 可伪造，但内部身份会跳过
/// 公开可见性过滤（内部接口要看到下架 / 待审核数据），等于把「服务间能力」
/// 暴露给任何能访问服务端口的人。所以这里对 /internal 做 fail-closed 校验，
/// 口令不对直接 401，而不是退化成匿名继续执行。
/// </remarks>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[]? _expectedToken;
    private readonly ILogger<TenantContextMiddleware> _logger;

    /// <summary>构造中间件。</summary>
    /// <param name="next">下一段管道。</param>
    /// <param name="configuration">应用配置，用于读 Tenancy 节。</param>
    /// <param name="logger">日志器，口令缺失或校验失败都要留痕。</param>
    public TenantContextMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var token = configuration.GetSection(TenancyOptions.SectionName)[nameof(TenancyOptions.InternalToken)];
        _expectedToken = string.IsNullOrWhiteSpace(token)
            ? null
            : Encoding.UTF8.GetBytes(token.Trim());

        if (_expectedToken is null)
        {
            _logger.LogWarning(
                "Tenancy:InternalToken 未配置，所有 X-Claim-* 请求头将被忽略，所有请求按匿名处理。" +
                "请在 AgileConfig 补上该配置，否则超管相关接口会一律 403。");
        }
    }

    /// <summary>执行：构造上下文 → 后续管道 → 清理上下文。</summary>
    /// <param name="context">当前请求。</param>
    /// <returns>管道结果。</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // /health 等探活请求不参与租户判定，避免依赖上下文
        if (IsProbe(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // /internal/** 是服务间接口：必须带对内部口令才放行。
        // 口令没配置（null）也拒绝：那是部署事故，不能让全部内部接口裸奔。
        if (context.Request.Path.StartsWithSegments("/internal"))
        {
            if (_expectedToken is null || !TokenMatches(context))
            {
                _logger.LogWarning(
                    "拒绝未携带内部口令的 /internal 请求：{Method} {Path}",
                    context.Request.Method, context.Request.Path);
                await RejectInternalAsync(context);
                return;
            }

            await RunAsync(context, new TenantContext { Access = AccessContext.Internal });
            return;
        }

        await RunAsync(context, BuildContext(context));
    }

    /// <summary>设置租户上下文执行后续管道，结束后必须清理。</summary>
    /// <param name="context">当前请求。</param>
    /// <param name="tenant">本次请求的租户上下文。</param>
    /// <returns>管道结果。</returns>
    private async Task RunAsync(HttpContext context, TenantContext tenant)
    {
        TenantContextHolder.Set(tenant);
        try
        {
            await _next(context);
        }
        finally
        {
            // 必须清理：AsyncLocal 挂在当前执行上下文上，线程池复用会把上一个请求的租户带过来
            TenantContextHolder.Clear();
        }
    }

    /// <summary>拒绝没有内部口令的 /internal 请求。</summary>
    /// <param name="context">当前请求。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>响应体沿用 ApiResponse 形状，内部调用方解析失败时能留下可读的错误。</remarks>
    private static async Task RejectInternalAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            code = StatusCodes.Status401Unauthorized,
            message = "内部接口未授权"
        });
    }

    /// <summary>判断是否为探活路径（/health、/ready 不参与租户判定）。</summary>
    /// <param name="path">请求路径。</param>
    /// <returns>是探活路径返回 true。</returns>
    private static bool IsProbe(PathString path)
        => path.StartsWithSegments("/health") || path.StartsWithSegments("/ready");

    /// <summary>按请求头构造租户上下文。</summary>
    /// <param name="http">当前请求。</param>
    /// <returns>租户上下文。</returns>
    private TenantContext BuildContext(HttpContext http)
    {
        var anonymous = new TenantContext { Access = AccessContext.Anonymous };

        if (_expectedToken is null) return anonymous;

        if (!TokenMatches(http)) return anonymous;

        var userId = ReadLong(http, TenancyOptions.Headers.UserId);
        var tenantType = (int)ReadLong(http, TenancyOptions.Headers.TenantType);

        if (userId <= 0 || tenantType is not (1 or 2 or 3)) return anonymous;

        var access = tenantType == 3 ? AccessContext.Customer : AccessContext.Admin;

        return new TenantContext
        {
            Access = access,
            UserId = userId,
            UserName = ReadString(http, TenancyOptions.Headers.UserName),
            CustomerNo = ReadString(http, TenancyOptions.Headers.CustomerNo),
            SessionId = ReadString(http, TenancyOptions.Headers.SessionId),
            TenantType = tenantType,
            PlatformId = ReadLong(http, TenancyOptions.Headers.PlatformId),
            MerchantId = ReadLong(http, TenancyOptions.Headers.MerchantId),
            Permissions = ReadList(http, TenancyOptions.Headers.Permissions)
        };
    }

    /// <summary>固定时间比较口令，避免按响应时间逐字节猜。</summary>
    private bool TokenMatches(HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue(TenancyOptions.Headers.InternalToken, out var provided))
            return false;

        var text = provided.ToString();
        if (string.IsNullOrEmpty(text)) return false;

        var bytes = Encoding.UTF8.GetBytes(text);
        return CryptographicOperations.FixedTimeEquals(bytes, _expectedToken);
    }

    /// <summary>读长整型请求头：缺失或解析失败一律返回 0。</summary>
    /// <param name="http">当前请求。</param>
    /// <param name="name">请求头名。</param>
    /// <returns>请求头里的数值；没有或非法时为 0。</returns>
    private static long ReadLong(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out var v)) return 0;
        return long.TryParse(v.ToString(), out var result) ? result : 0;
    }

    /// <summary>读字符串请求头并 trim；缺失返回空串。</summary>
    /// <param name="http">当前请求。</param>
    /// <param name="name">请求头名。</param>
    /// <returns>请求头值；没有时为空串。</returns>
    private static string ReadString(HttpContext http, string name)
        => http.Request.Headers.TryGetValue(name, out var v) ? v.ToString().Trim() : string.Empty;

    /// <summary>读集合型请求头：既支持重复头，也支持单值内逗号分隔。</summary>
    private static IReadOnlyList<string> ReadList(HttpContext http, string name)
    {
        if (!http.Request.Headers.TryGetValue(name, out var values)) return Array.Empty<string>();

        return values
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>租户上下文中间件的注册入口，16 个服务共用。</summary>
public static class TenantContextMiddlewareExtensions
{
    /// <summary>把租户上下文中间件插到管道最前面。</summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>原对象，便于链式调用。</returns>
    /// <remarks>
    /// 必须早于异常中间件与控制器：租户判定失败要能在最外层看到，
    /// 而 AOP 在更内层读上下文。
    /// </remarks>
    public static IApplicationBuilder UseAppTenantContext(this IApplicationBuilder app)
        => app.UseMiddleware<TenantContextMiddleware>();
}
