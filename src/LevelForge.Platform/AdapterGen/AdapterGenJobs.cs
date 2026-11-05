using System.Collections.Concurrent;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.AdapterGen;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Gates;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Plugins;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.AdapterGen;

// 适配层生成任务服务（三轮 D9 解冻）：任务表 + 后台执行；快照口径与生成任务一致（轮询消费）
// 单进程内存表即可——本地单机工具，任务量级为个位数
public sealed class AdapterGenJobService
{
    private static long _nextJobId;
    private readonly ConcurrentDictionary<long, AdapterGenJob> _jobs = new();
    private readonly AdapterGenRunner _runner;

    public AdapterGenJobService(AdapterGenRunner runner)
    {
        _runner = runner;
    }

    // 发起：立即返回任务号，生成在后台跑
    public AdapterGenJob Start(string gameId)
    {
        var jobId = Interlocked.Increment(ref _nextJobId);
        var job = new AdapterGenJob(jobId, gameId);
        _jobs[jobId] = job;
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    private async Task RunAsync(AdapterGenJob job)
    {
        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(20)).Token;   // 单任务硬上限 20 分钟
        try
        {
            var report = await _runner.RunAsync(job, ct);
            job.Complete(report, report.Rounds);
        }
        catch (OperationCanceledException)
        {
            job.SetError("生成超时（20 分钟上限）——建议改用需求包+外部编程助手路径", job.Round);
        }
        catch (AdapterGenException ex)
        {
            job.SetError(ex.Message, job.Round);
        }
        catch (Exception ex)
        {
            // 堆栈首帧进入错误信息：轮询端直接可见定位信息（不冒充用户可操作提示）
            var frame = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("AdapterGen")) ?? ex.StackTrace ?? "";
            job.SetError($"生成异常：{ex.GetType().Name}: {ex.Message} @ {frame.Trim()}", job.Round);
        }
    }

    // 取任务快照；不存在返回 null
    public AdapterGenSnapshot? Get(long jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;
    }
}
