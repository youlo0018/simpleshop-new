using Collaboration.Domain.Common;
using Collaboration.Domain.Services;
using MarketingService.Domain.IRepository;
using MarketingService.Domain.Services;
using MediatR;

namespace MarketingService.Application.Features.Reports;

/// <summary>查秒杀效果报表（BUSINESS.md 17）。</summary>
/// <param name="Range">时间范围档位：1 今日 / 2 昨日 / 3 近 7 天 / 4 近 30 天。</param>
/// <param name="SessionId">只看某个场次，0 表示全部。</param>
/// <param name="MerchantId">商户 Id，0 表示不限。</param>
/// <param name="PlatformId">平台 Id，0 表示不限。</param>
public record QuerySeckillReportCommand(int Range = 3, long SessionId = 0, long MerchantId = 0, long PlatformId = 0)
    : IRequest<ApiResponse<SeckillReport>>;

/// <summary>秒杀效果报表处理器。</summary>
public sealed class SeckillReportHandler
    : IRequestHandler<QuerySeckillReportCommand, ApiResponse<SeckillReport>>
{
    private readonly ISeckillRepository _seckill;
    private readonly IOrderAmountPort _gmv;
    private readonly IViewCountPort _views;

    /// <summary>构造处理器。</summary>
    /// <param name="seckill">秒杀仓储。</param>
    /// <param name="gmv">成交额端口（订单服务）。</param>
    /// <param name="views">场次 PV 端口（日志服务）。</param>
    public SeckillReportHandler(ISeckillRepository seckill, IOrderAmountPort gmv, IViewCountPort views)
    {
        _seckill = seckill;
        _gmv = gmv;
        _views = views;
    }

    /// <inheritdoc />
    public async Task<ApiResponse<SeckillReport>> Handle(
        QuerySeckillReportCommand request, CancellationToken ct)
    {
        // 场次按「开始时间」落在区间内筛。和工作台 GMV 按支付时间筛不同：
        // 秒杀报表问的是「这段时间开的场次表现如何」，而不是「这段时间成交了多少钱」
        // ——后者的钱已经算进工作台 GMV 了。
        var (from, to) = ReportRanges.Resolve(request.Range, DateTime.UtcNow);

        var sessions = await _seckill.AggregateSessionsAsync(
            from, to, request.SessionId, request.MerchantId, request.PlatformId,
            limit: 200, ct).ConfigureAwait(false);

        // 一次性把所有成功订单号交给订单服务换**逐单**金额，而不是每个场次调一次。
        // 拿到逐单金额后按场次求和：总额与逐场金额来自同一份数据，
        // 不会出现「逐场加起来不等于总额」那种对不上账的报表。
        var allOrderNos = sessions.SelectMany(a => a.OrderNos).Distinct().ToList();
        var payableByOrder = await _gmv.GetPayableByOrderAsync(allOrderNos, ct).ConfigureAwait(false);

        var withGmv = sessions
            .Select(a => a with
            {
                Gmv = Math.Round(
                    a.OrderNos.Sum(no => payableByOrder.GetValueOrDefault(no)),
                    2, MidpointRounding.AwayFromZero)
            })
            .ToList();

        // PV 同样一次性按场次批量取，不是每场调一次日志服务。
        // 日志服务挂了 / ES 没起来时端口返回空字典，报表 PV 显示 0 而不是整页 500。
        var pvBySession = await _views
            .CountBySessionAsync(withGmv.Select(a => a.SessionId).ToList(), from, to, ct)
            .ConfigureAwait(false);

        var withPv = withGmv
            .Select(a => a with { Pv = pvBySession.GetValueOrDefault(a.SessionId) })
            .ToList();

        var totalParticipants = withPv.Sum(a => a.ParticipantCount);
        var totalSuccess = withPv.Sum(a => a.GrabSuccessCount);
        var totalPv = withPv.Sum(a => a.Pv);
        var totalGmv = Math.Round(withPv.Sum(a => a.Gmv), 2, MidpointRounding.AwayFromZero);

        return ApiResults.Ok(new SeckillReport(
            withPv, totalParticipants, totalSuccess, totalPv, totalGmv));
    }
}
