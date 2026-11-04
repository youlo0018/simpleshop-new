using CartService.Application.Features.Cart;
using CartService.Application.Services;
using CartService.Infrastructure;
using Collaboration.Domain.Infrastructure;
using Collaboration.Domain.MediatR;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CartService.Api;

/// <summary>CartService 的应用层注册入口。</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>注册 MediatR、校验器、管道与基础设施。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置，用于读商品服务地址。</param>
    /// <returns>原集合，便于链式调用。</returns>
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInternalTokenPropagation();

        var appAssembly = typeof(AddToCartCommand).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(appAssembly));
        services.AddValidatorsFromAssembly(appAssembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        CartValidators.AddCartValidators(services);

        // 加购要刷新 SKU 快照（商品名 / 规格 / 价格 / 图片），所以必须能查到商品服务。
        var productUrl = configuration["Services:ProductServiceBaseUrl"];
        if (string.IsNullOrWhiteSpace(productUrl))
        {
            throw new InvalidOperationException(
                "缺少配置 Services:ProductServiceBaseUrl。购物车加购需要刷新 SKU 快照，没有它加购一定失败。");
        }

        services.AddHttpClient<IProductClient, HttpProductClient>(client =>
        {
            client.BaseAddress = new Uri(productUrl!.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5);
        }).AddHttpMessageHandler<InternalTokenHandler>();

        // 店铺名是展示字段：没配地址时用降级实现，购物车仍可用（前端显示「店铺」）。
        var merchantUrl = configuration["Services:MerchantPlatformServiceBaseUrl"];
        if (!string.IsNullOrWhiteSpace(merchantUrl))
        {
            services.AddHttpClient<IMerchantNameClient, HttpMerchantNameClient>(client =>
            {
                client.BaseAddress = new Uri(merchantUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(5);
            }).AddHttpMessageHandler<InternalTokenHandler>();
        }
        else
        {
            services.AddSingleton<IMerchantNameClient, UnavailableMerchantNameClient>();
        }

        services.AddInfrastructure();
        return services;
    }
}
