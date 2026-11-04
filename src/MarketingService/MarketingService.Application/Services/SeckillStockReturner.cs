using MarketingService.Domain.Entities;
using MarketingService.Domain.IRepository;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MarketingService.Application.Services;

/// <summary>把某个场次剩余的秒杀库存回补到常规池。</summary>
/// <remarks>
/// <para>抽成独立服务而不是各写一遍：<b>「手动中止」与「到点自动结束」是同一条库存动作</b>，
/// 两个入口必须走同一段代码。复制一份的后果是修一处漏一处——
/// 比如只给手动路径加了幂等单号，自动路径忘了，回补就可能被记成两次操作。</para>
///
/// <para><b>回补完成后才清标志</b>：中途挂掉时标志还在，重跑会继续回补。
/// 库存侧按 <c>bizNo</c> 幂等，所以重跑不会把货还两遍。</para>
/// </remarks>
public sealed class SeckillStockReturner
{
    private readonly ISeckillRepository _seckill;
    private readonly IInventoryPort _inventory;
    private readonly IDatabase _redis;
    private readonly ILogger<SeckillStockReturner> _logger;

    /// <summary>构造回补器。</summary>
    /// <param name="seckill">秒杀仓储。</param>
    /// <param name="inventory">库存端口。</param>
    /// <param name="redis">Redis 数据库，用于清秒杀余量键。</param>
    /// <param name="logger">日志器。</param>
    public SeckillStockReturner(
        ISeckillRepository seckill,
        IInventoryPort inventory,
        IDatabase redis,
        ILogger<SeckillStockReturner> logger)
    {
        _seckill = seckill;
        _inventory = inventory;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>回补一个场次的剩余库存。</summary>
    /// <param name="session">场次。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>回补件数与失败清单。</returns>
    public async Task<SeckillReturnOutcome> ReturnAsync(SeckillSession session, CancellationToken ct = default)
    {
        if (!session.StockTransferred)
        {
            return new SeckillReturnOutcome(0, Array.Empty<string>(), false);
        }

        var items = await _seckill.ListItemsAsync(session.Id, ct).ConfigureAwait(false);
        var failed = new List<string>();
        var released = 0;

        foreach (var item in items)
        {
            // 🔴 先确认这批货**真的划过**：seckill_release 是「可用 += qty」的无条件加法，
            // 对从未划出过库存的 SKU 释放会凭空多出一批货。
            // 「发布时部分划出、随后中止」的场次正是这种形状（有的划过、有的没划过），
            // 而 StockTransferred 只表达「有货在外面」，分不出是哪一个 SKU。
            var reserveBizNo = SeckillBizNos.Reserve(session.Id, item.SkuId);
            try
            {
                if (!await _inventory
                        .HasFlowAsync(item.SkuId, reserveBizNo, IInventoryPort.ReserveAction, ct)
                        .ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "场次 {SessionId} 的 SKU {SkuId} 没有划出流水，跳过回补（避免凭空多货）",
                        session.Id, item.SkuId);
                    continue;
                }
            }
            catch (Exception ex)
            {
                // 查不到就不知道该不该还。宁可留着标志下轮重试，也不能猜。
                _logger.LogError(ex, "场次 {SessionId} 回补前查划出流水失败：SKU {SkuId}", session.Id, item.SkuId);
                failed.Add($"{item.SkuId}（划出流水查询失败）");
                continue;
            }

            // 🔴 回补量取「库里的未售数」与「Redis 余量」的**较小者**。
            //
            // sold_count 是在**下单成功之后**才 +1 的，所以一个「预扣成功、还在下单」
            // 的请求此刻既没算进 sold_count、又已经占走了一件。只按
            // seckill_stock − sold_count 回补的话，这一件会被当成没卖出去而还给常规池，
            // 而那个请求随后照样把单落成 —— 结果是**回补多了一件、又卖出一件**，超卖。
            //
            // Redis 余量是「划出总数 − 已预扣」，天然把「在飞的那几件」排除在外，
            // 所以它才是真正可以还回去的数量。取较小者是双保险：
            // 万一 Redis 余量因为重试被抬高，也不会超过库里算出来的未售数。
            var dbRemaining = Math.Max(0, item.SeckillStock - item.SoldCount);
            var remaining = dbRemaining;

            var redisLeft = await _redis
                .StringGetAsync($"{SeckillStockKeys.Stock}{item.Id}").ConfigureAwait(false);
            if (redisLeft.HasValue
                && long.TryParse(redisLeft.ToString(), out var left)
                && left >= 0
                && left < remaining
                && left <= int.MaxValue)
            {
                remaining = (int)left;
            }

            if (remaining <= 0) continue;

            // 业务单号带 release 后缀：与划出时不同，
            // 所以库存侧不会把「回补」当成「又一次划出」而直接判成重复。
            var bizNo = SeckillBizNos.Release(session.Id, item.SkuId);
            try
            {
                var ok = await _inventory.ReleaseAsync(item.SkuId, remaining, bizNo, ct)
                    .ConfigureAwait(false);

                if (!ok)
                {
                    failed.Add($"{item.SkuId}（回补失败）");
                    continue;
                }

                released += remaining;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "场次 {SessionId} 回补 SKU {SkuId} 失败", session.Id, item.SkuId);
                failed.Add($"{item.SkuId}（库存服务异常）");
            }
        }

        if (failed.Count > 0)
        {
            // 🔴 有失败就**不能**清余量键、也不能清 StockTransferred：
            // 两者都是重试的依据 —— 余量键决定「还能还多少」（min(库里未售数, Redis 余量)），
            // 标志位是定时任务找到这张场次的唯一线索。原实现无条件清掉，
            // 而结束状态已先落库、再调结束又直接返回「已是目标状态」，
            // 一次库存服务抖动 = 剩余秒杀库存永久消失。
            // 保留后由 ScheduledService 的「回补重试」每轮继续处理，库存侧按 bizNo 幂等，不会还两遍。
            _logger.LogError(
                "场次 {SessionId} 有 {Count} 个商品回补失败，保留标志等待重试：{Failed}",
                session.Id, failed.Count, string.Join("、", failed));

            return new SeckillReturnOutcome(released, failed, true);
        }

        // 清掉余量键：场次已经结束，这个数字不能再被谁读到。
        // 不清的话，下次重建同名场次时 KeyExists 会判定「已初始化」，
        // 余量就停留在上一场的旧值上——表现为「新场次一开始就说抢完了」。
        foreach (var item in items)
        {
            await _redis.KeyDeleteAsync($"{SeckillStockKeys.Stock}{item.Id}").ConfigureAwait(false);
        }

        await _seckill.SetStockTransferredAsync(session.Id, false, ct).ConfigureAwait(false);

        return new SeckillReturnOutcome(released, failed, true);
    }
}

/// <summary>回补结果。</summary>
/// <param name="Released">回补件数。</param>
/// <param name="Failed">失败清单（SKU 与原因）。</param>
/// <param name="HadStock">该场次是否划出过库存。没划过时其余两项都没有意义。</param>
public sealed record SeckillReturnOutcome(int Released, IReadOnlyList<string> Failed, bool HadStock);
