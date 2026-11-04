using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Collaboration.Domain.Messaging;
using LogService.Application;
using LogService.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogService.Infrastructure;

/// <summary>日志索引配置。</summary>
public sealed class LogIndexOptions
{
    /// <summary>配置文件节名。</summary>
    public const string SectionName = "LogIndex";

    /// <summary>Elasticsearch 地址。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>页面访问日志索引名。</summary>
    public string PvIndex { get; set; } = "simpleshop_log_pv";

    /// <summary>写操作日志索引名。</summary>
    public string OperationIndex { get; set; } = "simpleshop_log_operation";

    /// <summary>异常日志索引名。</summary>
    public string ExceptionIndex { get; set; } = "simpleshop_log_exception";

    /// <summary>死信索引名。</summary>
    public string DeadLetterIndex { get; set; } = "simpleshop_log_deadletter";

    /// <summary>单次 <c>_bulk</c> 最多携带的文档数。</summary>
    /// <remarks>ES 默认 <c>http.max_content_length</c> 是 100MB，500 条日志远达不到；
    /// 这个值主要控制单次请求的失败影响面与内存占用。</remarks>
    public int BatchSize { get; set; } = 500;

    /// <summary>攒批窗口（毫秒）：窗口内到达的日志合并成一次 <c>_bulk</c>。</summary>
    /// <remarks>太大延迟高（日志查询要等更久才可见），太小则聚合效果差。
    /// 50ms 对「写后立即查」的日志回归基本无感，又能把并发写入合并成批。</remarks>
    public int FlushIntervalMs { get; set; } = 50;
}

/// <summary>用原生 REST 写 Elasticsearch 的实现。</summary>
/// <remarks>
/// <para>
/// <b>刻意不引 Elastic 客户端包</b>：与 ProductService 的搜索索引保持同一套方式
/// （HttpClient + REST）。引客户端要多一个依赖，而日志写入只是「POST 文档 / _bulk」，
/// 用不到客户端的聚合、批量、连接池那套能力。
/// </para>
///
/// <para>
/// <b>短窗口攒批 + _bulk</b>：逐条 POST <c>{index}/_doc</c> 时吞吐 = 1 ÷ ES 写入延迟，
/// 一个请求在网关与下游各产生一条 pv，量一大就积压。这里把窗口（默认 50ms）内
/// 到达的日志合并成一次 <c>_bulk</c>，每条日志仍各自等待自己的结果：
/// 单项失败只让那一条抛异常（重试 / 死信），不会连坐同批的其它日志。
/// 因此本类必须注册成 <b>Singleton</b>（Scoped 的话每条消息一个新实例，攒批永远只有一条）。
/// </para>
/// </remarks>
public sealed class ElasticsearchLogIndexer : ILogIndexer, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly LogIndexOptions _opt;
    private readonly ILogger<ElasticsearchLogIndexer> _logger;
    private readonly SemaphoreSlim _ensureOnce = new(1, 1);

    /// <summary>待写队列：写入方入队后等各自的完成源，后台循环负责攒批发送。</summary>
    private readonly ConcurrentQueue<PendingWrite> _pending = new();

    /// <summary>「已攒够一批，立即醒来」的信号；容量 1，只表示状态不计数。</summary>
    private readonly SemaphoreSlim _flushNow = new(0, 1);

    /// <summary>关闭信号：让后台循环退出并在退出前把剩余文档写完。</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>后台攒批循环。</summary>
    private readonly Task _flusher;

    /// <summary>当前待写条数，用于判断是否达到批大小。</summary>
    private int _pendingCount;

    /// <summary>是否已释放。</summary>
    private volatile bool _disposed;

    /// <summary>已经确保过映射的索引名。</summary>
    /// <remarks>
    /// 必须**按索引**记账：以前只有一个 bool，第一个索引建好后其余索引全部跳过，
    /// 后来新增的字段就没人补映射了 —— 症状是「日志写进去了，按新字段过滤/聚合却查不到」。
    /// </remarks>
    private readonly ConcurrentDictionary<string, byte> _ensured = new(StringComparer.Ordinal);

    /// <summary>构造索引器。</summary>
    /// <param name="http">指向 ES 的 HttpClient。</param>
    /// <param name="options">索引配置。</param>
    /// <param name="logger">日志器。</param>
    public ElasticsearchLogIndexer(
        HttpClient http,
        IOptions<LogIndexOptions> options,
        ILogger<ElasticsearchLogIndexer> logger)
    {
        _http = http;
        _opt = options.Value;
        _logger = logger;
        _flusher = Task.Run(() => FlushLoopAsync(_shutdown.Token));
    }

    /// <inheritdoc />
    public Task<bool> IndexPvAsync(PvLogEntry entry, CancellationToken ct = default)
        => WriteAsync(_opt.PvIndex, entry, ct);

    /// <inheritdoc />
    public Task<bool> IndexOperationAsync(OperationLogEntry entry, CancellationToken ct = default)
        => WriteAsync(_opt.OperationIndex, entry, ct);

    /// <inheritdoc />
    public Task<bool> IndexExceptionAsync(ExceptionLogEntry entry, CancellationToken ct = default)
        => WriteAsync(_opt.ExceptionIndex, entry, ct);

    /// <summary>把一条文档交给攒批队列，并等待它自己的写入结果。</summary>
    /// <param name="index">索引名。</param>
    /// <param name="doc">文档内容。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功返回 true；失败抛异常（消费循环据此重试 / 死信）。</returns>
    private async Task<bool> WriteAsync<T>(string index, T doc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.Url)) return false;
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pending = new PendingWrite(index, doc!);
        _pending.Enqueue(pending);

        if (Interlocked.Increment(ref _pendingCount) >= Math.Max(1, _opt.BatchSize))
        {
            SignalFlush();
        }

        // 调用方取消时，这一条也要立刻结束等待，而不是等到批次发送完。
        using var registration = ct.Register(
            () => pending.Completion.TrySetCanceled(ct));

        return await pending.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>后台攒批循环：每过一个窗口（或攒满一批）就把队列里的文档写成 _bulk。</summary>
    /// <param name="ct">关闭令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FlushLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(_opt.FlushIntervalMs, 5, 5000));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 收到「攒够一批」的信号立即醒来；否则等满一个窗口。
                await _flushNow.WaitAsync(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await DrainAsync(ct).ConfigureAwait(false);
        }

        // 关闭前把剩余文档写完：用 None 而不是已取消的 ct，
        // 否则进程退出时队列里已经 ack 的日志会被直接丢掉。
        await DrainAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>把队列里的文档按批大小分片，逐批发送。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// 循环 drain 直到队列为空：一次窗口内到达的文档可能远超单批上限，
    /// 剩余部分要立刻接着发，而不是再等一个窗口（那会让积压成倍放大）。
    /// </remarks>
    private async Task DrainAsync(CancellationToken ct)
    {
        var batchSize = Math.Clamp(_opt.BatchSize, 1, 10_000);

        while (true)
        {
            var batch = new List<PendingWrite>(Math.Min(batchSize, 1024));
            while (batch.Count < batchSize && _pending.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _pendingCount);
                batch.Add(item);
            }

            if (batch.Count == 0) return;

            await FlushBatchAsync(batch, ct).ConfigureAwait(false);
        }
    }

    /// <summary>把一批文档写成一次 _bulk，并把逐项结果回填给各自的等待者。</summary>
    /// <param name="batch">待写文档。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// 本方法**不向外抛异常**：批内每项的失败都通过完成源传递，
    /// 否则一次失败会让攒批循环退出，整个日志管道停摆。
    /// </remarks>
    private async Task FlushBatchAsync(IReadOnlyList<PendingWrite> batch, CancellationToken ct)
    {
        try
        {
            // 索引可能还没建（首次写入）：_bulk 不会自动建映射齐全的索引，
            // 仍然要先确保每个目标索引存在且字段映射正确。
            foreach (var index in batch.Select(a => a.Index).Distinct(StringComparer.Ordinal))
            {
                await EnsureIndexAsync(index, ct).ConfigureAwait(false);
            }

            var payload = BuildBulkPayload(batch);
            using var content = new StringContent(payload, Encoding.UTF8, "application/x-ndjson");
            using var response = await _http.PostAsync("_bulk", content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "批量写日志失败：HTTP {Code} {Body}", (int)response.StatusCode, body);
                FailAll(batch, new InvalidOperationException(
                    $"批量写日志失败：HTTP {(int)response.StatusCode}"));
                return;
            }

            ApplyBulkResult(body, batch);
        }
        catch (OperationCanceledException ex)
        {
            FailAll(batch, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量写日志出现未预期异常");
            FailAll(batch, ex);
        }
    }

    /// <summary>解析 _bulk 响应，按 items 顺序把成功 / 失败回填给对应文档。</summary>
    /// <param name="body">响应体。</param>
    /// <param name="batch">请求里的文档，顺序与 items 一一对应。</param>
    /// <remarks>
    /// 🔴 HTTP 200 只代表「_bulk 请求被处理了」，**不代表每条都写成功**：
    /// 单条映射冲突 / 字段类型不符时 ES 仍然回 200，错误在 items[i].index.error 里。
    /// 只看 HTTP 状态码会把失败当成功 ack 掉，日志永久丢失且没人知道。
    /// </remarks>
    private void ApplyBulkResult(string body, IReadOnlyList<PendingWrite> batch)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            FailAll(batch, new InvalidOperationException("_bulk 响应不是合法 JSON", ex));
            return;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() != batch.Count)
            {
                FailAll(batch, new InvalidOperationException(
                    "_bulk 响应的 items 数量与请求文档数量不一致"));
                return;
            }

            for (var i = 0; i < batch.Count; i++)
            {
                // 每项形如 {"index":{"_index":"...","status":201}}；操作类型固定是 index
                var action = items[i].EnumerateObject().FirstOrDefault().Value;
                var status = action.ValueKind == JsonValueKind.Object
                             && action.TryGetProperty("status", out var s)
                             && s.TryGetInt32(out var code)
                    ? code
                    : 0;

                if (status is >= 200 and < 300)
                {
                    batch[i].Completion.TrySetResult(true);
                    continue;
                }

                var error = action.ValueKind == JsonValueKind.Object
                            && action.TryGetProperty("error", out var e)
                    ? e.ToString()
                    : string.Empty;

                _logger.LogError(
                    "写日志索引 {Index} 失败：HTTP {Code} {Error}", batch[i].Index, status, error);
                batch[i].Completion.TrySetException(new InvalidOperationException(
                    $"写日志索引 {batch[i].Index} 失败：HTTP {status}"));
            }
        }
    }

    /// <summary>构造 _bulk 的 NDJSON 载荷（action 行 + 文档行）。</summary>
    /// <param name="batch">待写文档。</param>
    /// <returns>NDJSON 文本。</returns>
    private static string BuildBulkPayload(IReadOnlyList<PendingWrite> batch)
    {
        var payload = new StringBuilder(batch.Count * 256);

        foreach (var item in batch)
        {
            // 索引名用 JSON 序列化生成带引号的字符串，配置里带特殊字符也不会拼出坏 JSON
            payload.Append("{\"index\":{\"_index\":")
                .Append(JsonSerializer.Serialize(item.Index))
                .Append("}}\n");

            // 显式传运行时类型：文档字段是 object 时，泛型重载会按编译期类型序列化，
            // 结果只写个 {} —— 日志"写成功"了但内容全是空的。
            payload.Append(JsonSerializer.Serialize(item.Document, item.Document.GetType(), EventJson.Options))
                .Append('\n');
        }

        return payload.ToString();
    }

    /// <summary>把同一个异常回填给一批文档的所有等待者。</summary>
    /// <param name="batch">待写文档。</param>
    /// <param name="exception">失败原因。</param>
    private static void FailAll(IReadOnlyList<PendingWrite> batch, Exception exception)
    {
        foreach (var item in batch)
        {
            item.Completion.TrySetException(exception);
        }
    }

    /// <summary>通知攒批循环立刻醒来（已有信号时忽略）。</summary>
    private void SignalFlush()
    {
        try
        {
            if (_flushNow.CurrentCount == 0) _flushNow.Release();
        }
        catch (SemaphoreFullException)
        {
            // 并发写入同时到达批大小：信号已经在了，忽略即可
        }
    }

    /// <summary>停止攒批循环，并把队列里剩余的文档写完。</summary>
    /// <returns>异步任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _shutdown.CancelAsync().ConfigureAwait(false);

        try
        {
            await _flusher.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭路径，忽略
        }

        _shutdown.Dispose();
        _flushNow.Dispose();
        _ensureOnce.Dispose();
    }

    /// <summary>待写文档：内容 + 自己的完成源。</summary>
    private sealed class PendingWrite
    {
        /// <summary>构造待写项。</summary>
        /// <param name="index">目标索引。</param>
        /// <param name="document">文档内容。</param>
        public PendingWrite(string index, object document)
        {
            Index = index;
            Document = document;
            Completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>目标索引。</summary>
        public string Index { get; }

        /// <summary>文档内容。</summary>
        public object Document { get; }

        /// <summary>本条文档的完成源（成功 / 失败 / 取消）。</summary>
        public TaskCompletionSource<bool> Completion { get; }
    }

    /// <summary>确保索引存在且映射齐全（幂等，每个索引只在首次执行）。</summary>
    /// <param name="index">索引名。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task EnsureIndexAsync(string index, CancellationToken ct)
    {
        if (_ensured.ContainsKey(index)) return;

        await _ensureOnce.WaitAsync(ct);
        try
        {
            if (_ensured.ContainsKey(index)) return;

            var properties = BuildMappingProperties();

            var head = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, index), ct).ConfigureAwait(false);

            if (!head.IsSuccessStatusCode)
            {
                var body = new { mappings = new { properties } };

                var created = await _http
                    .PutAsJsonAsync(index, body, ct).ConfigureAwait(false);

                if (!created.IsSuccessStatusCode)
                {
                    var text = await created.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("创建日志索引 {Index} 失败：{Body}", index, text);
                }
            }
            else
            {
                // 已有索引也要补一次映射：索引是一次性创建的，而后来的版本会新增字段
                // （例如秒杀 PV 归因用的 sessionId）。不补的话动态映射会把它当成 text，
                // term 过滤 / 聚合全部失效 —— 症状是「日志有记录，报表 PV 恒为 0」。
                // 重复声明已存在的同类型字段是幂等的；类型冲突时只告警不抛，
                // 不能因为一个旧索引的映射冲突让整个日志消费停下来。
                var updated = await _http
                    .PutAsJsonAsync($"{index}/_mapping", new { properties }, ct).ConfigureAwait(false);

                if (!updated.IsSuccessStatusCode)
                {
                    var text = await updated.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    _logger.LogWarning("补写日志索引 {Index} 映射失败：{Body}", index, text);
                }
            }

            _ensured.TryAdd(index, 0);
        }
        finally
        {
            _ensureOnce.Release();
        }
    }

    /// <summary>构造日志索引的显式字段映射。</summary>
    /// <returns>字段名 → 映射定义。</returns>
    private static Dictionary<string, object> BuildMappingProperties() => new()
    {
        ["occurredAt"] = new { type = "date" },
        ["requestId"] = new { type = "keyword" },
        ["path"] = new { type = "keyword" },
        ["method"] = new { type = "keyword" },
        ["statusCode"] = new { type = "integer" },

        // 🔴 service / operatorName / clientIp / userAgent **必须显式映射**。
        // 不写的话 ES 动态映射成 text + .keyword 子字段，
        // 而 `{"term":{"service":"LogService"}}` 打在 text 字段上
        // 永远匹配不到——症状是「日志确实写进去了，但按服务名一条都查不出来」。
        // 排查时很容易误判成「日志没写进去」，方向从一开始就错了。
        ["service"] = new { type = "keyword" },
        ["operatorName"] = new { type = "keyword" },
        ["clientIp"] = new { type = "keyword" },
        ["userAgent"] = new { type = "keyword" },

        // 秒杀场次 PV 归因字段（BUSINESS.md 17）：只有埋点请求带，其余文档为 null。
        ["sessionId"] = new { type = "keyword" },

        // message / stackTrace / exceptionType 保持动态映射（text）：
        // 它们要全文模糊搜，映射成 keyword 就搜不动了。
    };
}
