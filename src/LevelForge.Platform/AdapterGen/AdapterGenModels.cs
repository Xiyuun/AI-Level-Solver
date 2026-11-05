using LevelForge.Platform.Storage;

namespace LevelForge.Platform.AdapterGen;

// 一条适配层生成进度事件（轮询快照用）
public sealed record AdapterGenEvent(long Seq, long ElapsedMs, string Stage, string Message);

// 生成任务快照：轮询消费（Status=running|succeeded|failed|cancelled）
public sealed record AdapterGenSnapshot(
    long JobId,
    string Status,
    string Stage,
    int Round,
    long ElapsedMs,
    string? Error,
    AdapterGenReport? Report,
    IReadOnlyList<AdapterGenEvent> Events);

// 生成报告：编译/门1/轮次等最终事实
public sealed record AdapterGenReport(
    bool Success,
    int Rounds,
    bool Compiled,
    bool Gate1Passed,
    string? Gate1Message,
    string? Error,
    string WorkspaceDir,
    string PluginsDir,
    string Summary);

// 适配层生成失败（用户可操作）
public sealed class AdapterGenException(string message) : Exception(message);

// 生成任务的进度记录点：由 Runner 写入 Job
public sealed record AdapterGenProgress(string Stage, string Message, int Round);

// 一次生成任务的运行态：事件缓冲 + 状态 + 报告（线程安全；快照口径与生成任务一致）
public sealed class AdapterGenJob
{
    private readonly object _lock = new();
    private readonly List<AdapterGenEvent> _events = [];
    private long _seq;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public AdapterGenJob(long jobId, string gameId)
    {
        JobId = jobId;
        GameId = gameId;
    }

    public long JobId { get; }
    public string GameId { get; }
    public string Status { get; private set; } = "running";
    public string Stage { get; private set; } = "准备";
    public int Round { get; private set; }
    public string? Error { get; private set; }
    public AdapterGenReport? Report { get; private set; }
    private long? _finishedElapsedMs;
    private long FinishedElapsed => _finishedElapsedMs ?? _clock.ElapsedMilliseconds;

    // Runner 写事件（调用方持有锁边界）
    public void Append(string stage, string message, int round)
    {
        lock (_lock)
        {
            _events.Add(new AdapterGenEvent(++_seq, _clock.ElapsedMilliseconds, stage, message));
            Stage = stage;
            Round = round;
        }
    }

    public void SetError(string message, int round)
    {
        lock (_lock)
        {
            Error = message;
            Status = "failed";
            Stage = "失败";
            Round = round;
            _finishedElapsedMs ??= _clock.ElapsedMilliseconds;
        }
    }

    public void Complete(AdapterGenReport report, int round)
    {
        lock (_lock)
        {
            Report = report;
            Status = report.Success ? "succeeded" : "failed";
            Stage = report.Success ? "完成" : "失败";
            Round = round;
            if (report.Error is not null) Error = report.Error;
            _finishedElapsedMs ??= _clock.ElapsedMilliseconds;
        }
    }

    public AdapterGenSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new AdapterGenSnapshot(
                JobId, Status, Stage, Round, FinishedElapsed, Error, Report, [.. _events]);
        }
    }
}
