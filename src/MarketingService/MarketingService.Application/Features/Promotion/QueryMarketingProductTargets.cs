using Collaboration.Domain.Common;
using FluentValidation;
using MarketingService.Domain.Entities;
using MarketingService.Domain.IRepository;
using MarketingService.Domain.Services;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MarketingService.Application.Features.Promotion;

/// <summary>查询活动或用户券的商品适用范围，供商品服务过滤列表。</summary>
/// <param name="ActivityId">活动 Id，与 <paramref name="CouponId"/> 二选一。</param>
/// <param name="CouponId">用户券 Id，与 <paramref name="ActivityId"/> 二选一。</param>
public record QueryMarketingProductTargetsCommand(long ActivityId = 0, long CouponId = 0)
    : IRequest<ApiResponse<MarketingProductTargetsDto>>;

/// <summary>营销对象的商品适用范围。</summary>
/// <param name="Exists">对象是否存在且可用。</param>
/// <param name="TargetType">1 全场 / 2 SPU / 3 SKU。</param>
/// <param name="SpuIds">指定 SPU Id。</param>
/// <param name="SkuIds">指定 SKU Id。</param>
public sealed record MarketingProductTargetsDto(
    bool Exists, int TargetType, IReadOnlyList<long> SpuIds, IReadOnlyList<long> SkuIds);

/// <summary>查询营销商品适用范围。</summary>
public sealed class QueryMarketingProductTargetsHandler
    : IRequestHandler<QueryMarketingProductTargetsCommand, ApiResponse<MarketingProductTargetsDto>>
{
    private readonly IPromotionRepository _promotions;
    private readonly ICouponRepository _coupons;

    /// <summary>构造处理器。</summary>
    /// <param name="promotions">活动仓储。</param>
    /// <param name="coupons">券仓储。</param>
    public QueryMarketingProductTargetsHandler(
        IPromotionRepository promotions, ICouponRepository coupons)
    {
        _promotions = promotions;
        _coupons = coupons;
    }

    /// <summary>执行查询。</summary>
    /// <param name="request">查询条件。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>适用商品范围；对象不存在时 Exists=false。</returns>
    public async Task<ApiResponse<MarketingProductTargetsDto>> Handle(
        QueryMarketingProductTargetsCommand request, CancellationToken ct)
    {
        int targetType;
        string targets;
        if (request.ActivityId > 0)
        {
            var activity = await _promotions.GetAsync(request.ActivityId, ct).ConfigureAwait(false);
            if (activity is null)
            {
                return ApiResults.Ok(new MarketingProductTargetsDto(false, 0, [], []));
            }

            targetType = activity.TargetType;
            targets = activity.Targets;
        }
        else
        {
            var coupon = await _coupons.GetCouponAsync(request.CouponId, ct).ConfigureAwait(false);
            if (coupon is null)
            {
                return ApiResults.Ok(new MarketingProductTargetsDto(false, 0, [], []));
            }

            targetType = coupon.TargetType;
            targets = coupon.Targets;
        }

        var ids = PromotionCalculator.ParseTargets(targets).OrderBy(a => a).ToArray();
        return ApiResults.Ok(new MarketingProductTargetsDto(
            true,
            targetType,
            targetType == TargetTypes.BySpu ? ids : [],
            targetType == TargetTypes.BySku ? ids : []));
    }
}

/// <summary>营销商品范围查询校验器注册。</summary>
public static class MarketingProductTargetValidators
{
    /// <summary>注册校验器。</summary>
    /// <param name="services">服务集合。</param>
    public static void AddMarketingProductTargetValidators(IServiceCollection services)
        => services.AddScoped<IValidator<QueryMarketingProductTargetsCommand>,
            QueryMarketingProductTargetsValidator>();

    /// <summary>查询条件校验。</summary>
    private sealed class QueryMarketingProductTargetsValidator
        : AbstractValidator<QueryMarketingProductTargetsCommand>
    {
        /// <summary>构造校验器。</summary>
        public QueryMarketingProductTargetsValidator()
        {
            RuleFor(x => x.ActivityId).GreaterThanOrEqualTo(0).WithMessage("活动 Id 不正确");
            RuleFor(x => x.CouponId).GreaterThanOrEqualTo(0).WithMessage("券 Id 不正确");
            RuleFor(x => x).Must(x => (x.ActivityId > 0) ^ (x.CouponId > 0))
                .WithMessage("活动和券必须且只能指定一个");
        }
    }
}
