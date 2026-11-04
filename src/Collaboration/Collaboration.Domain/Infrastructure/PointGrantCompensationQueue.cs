using System.Text.Json;
using StackExchange.Redis;

namespace Collaboration.Domain.Infrastructure;

/// <summary>积分发放类型。</summary>
public static class PointGrantKinds
{
    /// <summary>注册赠送。</summary>
    public const string Register = "register";

    /// <summary>发表首评赠送。</summary>
    public const string Evaluate = "evaluate";

    /// <summary>订单完成按实付发放。</summary>
    public const string Order = "order";
}

/// <summary>一条待补发的积分发放。</summary>
/// <param name="Kind">发放类型，见 <see cref="PointGrantKinds"/>。</param>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="BizNo">业务单号（订单号 / 评价 Id / 注册客户 Id 的字符串形式）。</param>
/// <param name="PaidAmount">订单实付金额，只有订单完成发放用得上。</param>
/// <param name="Attempts">已重试次数，超过上限转人工。</param>
/// <remarks>
/// 金额不在这里：发放数额由积分规则决定（<c>register_gift</c> / <c>first_evaluate_gift</c> /
/// 实付换算），补发必须走与首次发放同一个服务端接口，不能把当时算出来的数字抄一份进来——
/// 规则改了之后补发的口径会与首次发放漂移。
/// </remarks>
public sealed record PointGrantCompensation(
    string Kind, long CustomerId, string BizNo, decimal PaidAmount = 0m, int Attempts = 0);

/// <summary>一条已租出的补偿记录。</summary>
/// <param name="Payload">原始 JSON 文本，用于完成后从「处理中」列表精确移除。</param>
/// <param name="Item">解析后的记录。</param>
public sealed record PointGrantCompensationLease(string Payload, PointGrantCompensation Item);

/// <summary>积分发放失败的补偿队列。</summary>
/// <remarks>
/// <para><b>为什么用 Redis 而不是各服务建表</b>：注册赠分（CustomerService）、
/// 首评赠分（EvaluateService）、订单完成赠分（OrderService）是三个不同服务发起的，
/// 而补发只有一处（ScheduledService）。各建一张补偿表意味着三套表 + 三个内部接口 + 三段重试，
/// 而队列只需要一个共享键。</para>
///
/// <para><b>键必须放共享库</b>（<c>Redis:SharedDatabase</c>）：写入方是三个业务服务、
/// 读取方是 ScheduledService，各用各的库号就永远读不到，而且失败是静默的。
/// 与后台会话吊销是同一个约定。</para>
///
/// <para>补发接口按 bizNo 幂等，重复消费不会多发积分。</para>
/// </remarks>
public static class PointGrantCompensationQueue
{
    /// <summary>队列键。</summary>
    public const string Key = "points:grant-compensate";

    /// <summary>处理中列表键。</summary>
    /// <remarks>
    /// 取任务用 <c>RPOPLPUSH</c> 原子地挪到这里，而不是「弹出 → 处理 → 入队」：
    /// 后者在两步之间进程崩掉，那条补偿就**永久消失**（日志里没有任何痕迹）。
    /// 挪到处理中列表后，崩了也只是留在列表里，下一轮由 <see cref="RecoverAsync"/> 收回。
    /// </remarks>
    public const string ProcessingKey = "points:grant-compensate:processing";

    /// <summary>入队一条待补发记录。</summary>
    /// <param name="redis">共享库的 Redis 句柄。</param>
    /// <param name="item">待补发记录。</param>
    /// <returns>成功返回 true；Redis 不可用返回 false（调用方要留日志转人工）。</returns>
    public static async Task<bool> EnqueueAsync(IDatabase redis, PointGrantCompensation item)
    {
        try
        {
            await redis.ListLeftPushAsync(Key, JsonSerializer.Serialize(item)).ConfigureAwait(false);
            return true;
        }
        catch (RedisException)
        {
            return false;
        }
    }

    /// <summary>取一条待补发记录（先进先出，原子挪到「处理中」列表）。</summary>
    /// <param name="redis">共享库的 Redis 句柄。</param>
    /// <returns>记录；队列为空或 Redis 不可用时返回 null。</returns>
    public static async Task<PointGrantCompensationLease?> DequeueAsync(IDatabase redis)
    {
        try
        {
            var value = await redis.ListRightPopLeftPushAsync(Key, ProcessingKey).ConfigureAwait(false);
            if (!value.HasValue) return null;

            var payload = value.ToString();
            var item = JsonSerializer.Deserialize<PointGrantCompensation>(payload);
            if (item is null)
            {
                // 坏数据：从处理中列表移除，别堵住后面的补偿
                await redis.ListRemoveAsync(ProcessingKey, payload, 1).ConfigureAwait(false);
                return null;
            }

            return new PointGrantCompensationLease(payload, item);
        }
        catch (RedisException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>补发成功：把记录从「处理中」列表移除。</summary>
    /// <param name="redis">共享库的 Redis 句柄。</param>
    /// <param name="lease">租约。</param>
    /// <returns>异步任务。</returns>
    public static async Task CompleteAsync(IDatabase redis, PointGrantCompensationLease lease)
        => await redis.ListRemoveAsync(ProcessingKey, lease.Payload, 1).ConfigureAwait(false);

    /// <summary>把补发失败的记录放回队尾（先移出处理中列表）。</summary>
    /// <param name="redis">共享库的 Redis 句柄。</param>
    /// <param name="lease">租约（用于移除原记录）。</param>
    /// <param name="item">重试记录（Attempts 已加一）。</param>
    /// <returns>异步任务。</returns>
    public static async Task RequeueAsync(
        IDatabase redis, PointGrantCompensationLease lease, PointGrantCompensation item)
    {
        await redis.ListRemoveAsync(ProcessingKey, lease.Payload, 1).ConfigureAwait(false);
        await redis.ListLeftPushAsync(Key, JsonSerializer.Serialize(item)).ConfigureAwait(false);
    }

    /// <summary>回收上一轮崩溃时留在「处理中」列表里的记录。</summary>
    /// <param name="redis">共享库的 Redis 句柄。</param>
    /// <param name="maxItems">单轮最多回收多少条（防御性上限）。</param>
    /// <returns>回收条数。</returns>
    public static async Task<int> RecoverAsync(IDatabase redis, int maxItems = 1000)
    {
        var recovered = 0;

        try
        {
            while (recovered < maxItems)
            {
                var moved = await redis.ListRightPopLeftPushAsync(ProcessingKey, Key).ConfigureAwait(false);
                if (!moved.HasValue) break;
                recovered++;
            }
        }
        catch (RedisException)
        {
            // Redis 不可用时什么都不做：记录仍在处理中列表里，下一轮再回收
        }

        return recovered;
    }
}
