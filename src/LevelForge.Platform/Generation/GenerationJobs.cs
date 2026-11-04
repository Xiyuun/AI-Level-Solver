using System.Collections.Concurrent;
using System.Threading.Channels;

namespace LevelForge.Platform.Generation;

// 生成任务的一条事件（SSE 推送 + 快照回放共用同一份事实）
public sealed record GenerationEvent(long Seq, long ElapsedMs, string Stage, string Message, int? AttemptIndex, string? FailureClass);

// 生成任务终态：结论 + 失败摘要（成功时 Run 带完整运行记录）
public sealed record GenerationJobSnapshot(
    long JobId,
    string Status,                       // running | succeeded | failed | cancelled
    long ElapsedMs,
    IReadOnlyList<GenerationEvent> Events,
    GenerationRunSummary? Run,           // 终态才有；运行中为 null
    string? Error);                      // 请求校验失败等（未产生运行记录时的原因）

// 单个后台生成任务：事件缓冲 + 多订阅者扇出 + 取消
public sealed class GenerationJob
{
    private readonly object _lock = new();                       // 保护 _events 与 _subscribers
    private readonly List<GenerationEvent> _events = [];         // 已发生事件（新订阅者先回放，避免错过开头）
    private readonly List<Channel<GenerationEvent>> _subscribers = [];   // 在线订阅者（SSE 连接）
    private readonly CancellationTokenSource _cts = new();        // 取消信号源
    private long _seq;                                            // 事件序号（前端去重/排序用）

    public GenerationJob(long jobId, GenerationRequest request, System.Diagnostics.Stopwatch clock)
    {
        JobId = jobId;
        Request = request;
        Clock = clock;
    }

    public long JobId { get; }
    public GenerationRequest Request { get; }
    public System.Diagnostics.Stopwatch Clock { get; }
    public string Status { get; private set; } = "running";
    public GenerationRunSummary? Run { get; private set; }
    public string? Error { get; private set; }
    public CancellationToken Token => _cts.Token;
    public object SyncRoot => _lock;

    // 收口那一刻的耗时；未收口为 null（快照据此冻结时长——否则已完成的任务耗时仍会随时间增长）
    private long? _finishedElapsedMs;

    // 对外可见的耗时：运行中取实时时钟，收口后取冻结值
    private long VisibleElapsedMs => _finishedElapsedMs ?? Clock.ElapsedMilliseconds;

    // 推一条事件：先入缓冲，再扇出给在线订阅者
    public void Emit(GenerationProgress progress)
    {
        GenerationEvent evt;
        lock (_lock)
        {
            evt = new GenerationEvent(++_seq, Clock.ElapsedMilliseconds, progress.Stage, progress.Message,
                progress.AttemptIndex, progress.FailureClass);
            _events.Add(evt);
            foreach (var sub in _subscribers)
            {
                sub.Writer.TryWrite(evt);
            }
        }
    }

    // 收口：冻结耗时、写终态、关闭所有订阅流
    public void Complete(string status, GenerationRunSummary? run, string? error)
    {
        List<Channel<GenerationEvent>> subs;
        lock (_lock)
        {
            Status = status;
            Run = run;
            Error = error;
            _finishedElapsedMs ??= Clock.ElapsedMilliseconds;   // 幂等：重复收口不覆盖首次
            subs = [.. _subscribers];
            _subscribers.Clear();
        }
        foreach (var sub in subs)
        {
            sub.Writer.TryComplete();
        }
    }

    // 请求取消（幂等：重复取消不抛）
    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务已收口并释放：取消是空操作
        }
    }

    // 订阅：先回放历史事件，再持续接收后续事件；流在收口时自然结束
    public async IAsyncEnumerable<GenerationEvent> Subscribe(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<GenerationEvent>();
        List<GenerationEvent> backlog;
        var finished = false;
        lock (_lock)
        {
            backlog = [.. _events];
            if (Status == "running")
            {
                _subscribers.Add(channel);
            }
            else
            {
                finished = true;
            }
        }
        foreach (var evt in backlog)
        {
            yield return evt;
        }
        if (finished)
        {
            yield break;
        }
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    // 快照：事件列表 + 当前结论（轮询/断线重连用）
    public GenerationJobSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new GenerationJobSnapshot(JobId, Status, VisibleElapsedMs, [.. _events], Run, Error);
        }
    }
}

// 生成任务服务：把同步流水线变成可观察的后台任务（前后端全流程优化 §8.4）
// 单进程内存表即可——本地单机工具，任务量级为个位数；进程重启后历史运行仍可从 generation_runs 回查
public sealed class GenerationJobService
{
    private static long _nextJobId;                                        // 自增任务号（Interlocked）
    private readonly ConcurrentDictionary<long, GenerationJob> _jobs = new();
    private readonly GenerationPipeline _pipeline;
    private readonly int _retention;                                       // 保留的已完成任务数（防内存堆积）

    public GenerationJobService(GenerationPipeline pipeline)
    {
        _pipeline = pipeline;
        _retention = 50;
    }

    // 发起任务：立即返回句柄，流水线在后台跑；请求校验失败也转为终态而不是抛给调用方
    public GenerationJob Start(GenerationRequest request)
    {
        var jobId = Interlocked.Increment(ref _nextJobId);
        var job = new GenerationJob(jobId, request, System.Diagnostics.Stopwatch.StartNew());
        _jobs[jobId] = job;
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    // 后台执行：进度事件 → 终态收口（异常也落成 failed，绝不让任务停在 running）
    private async Task RunAsync(GenerationJob job)
    {
        try
        {
            var summary = await _pipeline.RunAsync(job.Request, job.Token, p => job.Emit(p));
            job.Complete(summary.Status, summary, null);
        }
        catch (OperationCanceledException)
        {
            job.Complete("cancelled", null, "生成被取消");
        }
        catch (GenerationException ex)
        {
            // 请求校验失败（未知玩法/未配置 LLM）：没有运行记录，只给原因
            job.Complete("failed", null, ex.Message);
        }
        catch (Exception ex)
        {
            job.Complete("failed", null, $"生成异常：{ex.Message}");
        }
        finally
        {
            TrimFinished();
        }
    }

    // 取任务句柄
    public GenerationJob? Get(long jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    // 清理：只保留最近 N 个已收口任务（运行中的永不清理）
    private void TrimFinished()
    {
        var finished = _jobs.Values
            .Where(j => j.Status != "running")
            .OrderByDescending(j => j.JobId)
            .Skip(_retention)
            .ToList();
        foreach (var job in finished)
        {
            _jobs.TryRemove(job.JobId, out _);
        }
    }
}
