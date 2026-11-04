using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Collaboration.Domain.Infrastructure;

/// <summary>服务间 HttpClient 的内部口令传播处理器。</summary>
/// <remarks>
/// <para>下游的 TenantContextMiddleware 只认 <c>X-Internal-Token</c>：
/// <c>/internal/**</c> 没有它直接 401；带对了才把请求标成 Internal 上下文
/// （内部调用没有平台 / 商户声明，不标 Internal 就会被套上租户与公开可见性过滤，
/// 内部接口反而查不到数据）。</para>
///
/// <para>这里统一从配置读同一个口令并加到出站请求上，而不是让每个调用点手写头：
/// 手写意味着新增一个内部客户端时很容易漏，而漏掉的症状是运行期 401 ——
/// 在回归里表现为某个跨服务链路整段失败，排查要一路从业务错误追到中间件。</para>
/// </remarks>
public sealed class InternalTokenHandler : DelegatingHandler
{
    /// <summary>内部口令请求头名。</summary>
    public const string HeaderName = "X-Internal-Token";

    /// <summary>内部口令配置路径。</summary>
    public const string ConfigurationPath = "Tenancy:InternalToken";

    private readonly string _token;

    /// <summary>构造处理器。</summary>
    /// <param name="configuration">应用配置，读取 Tenancy:InternalToken。</param>
    public InternalTokenHandler(IConfiguration configuration)
        => _token = configuration[ConfigurationPath]?.Trim() ?? string.Empty;

    /// <summary>发送请求前补上内部口令头。</summary>
    /// <param name="request">出站请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应任务。</returns>
    /// <remarks>调用方已经显式设置该头时不覆盖：便于测试伪造口令与个别场景的覆盖。</remarks>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_token) && !request.Headers.Contains(HeaderName))
        {
            request.Headers.TryAddWithoutValidation(HeaderName, _token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>内部口令传播的注册扩展。</summary>
/// <remarks>
/// <c>AddHttpMessageHandler&lt;T&gt;</c> 只负责把处理器挂进管道，**不会**注册 T 本身
/// （实测：漏注册时网关在启动阶段解析 RoutePermissionCache 的 HttpClient 直接抛
/// "No service for type ... has been registered"，进程起不来）。
/// 每个用到它的服务在注册 HttpClient 之前调一次本方法。
/// </remarks>
public static class InternalTokenHandlerExtensions
{
    /// <summary>注册内部口令传播处理器。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>原集合，便于链式调用。</returns>
    public static IServiceCollection AddInternalTokenPropagation(this IServiceCollection services)
    {
        services.AddTransient<InternalTokenHandler>();
        return services;
    }
}
