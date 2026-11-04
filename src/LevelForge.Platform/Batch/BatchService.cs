using System.Collections.Concurrent;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Generation;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Batch;

// 批量任务请求：编号段 + 难度配比 + 统一要求 + 并发 + 高级选项
public sealed record BatchRequest(
    string GameId,                                    // 玩法标识
    int FromLevel,                                    // 起始编号
    int ToLevel,                                      // 结束编号
    int Difficulty1Count,                             // 难度1数量（轻松）
    int Difficulty2Count,                             // 难度2数量（标准）
    int Difficulty3Count,                             // 难度3数量（烧脑）
    string? SpecialRequirements = null,               // 统一特殊要求
    int? Concurrency = null,                          // 并发数（null 用设置页默认 4）
    int? CandidateLimit = null,                       // 候选上限（null 用设置页默认 3）
    long? SolveTimeoutMs = null);                     // 求解超时（null 用设置页默认 30s）

// 单关行（逐关状态机的对外形态）
public sealed record BatchItemDto(
    int LevelNumber,                                  // 关卡编号
    string Status,                                    // pending|running|solved|failed|skipped
    int? DifficultyBand,                              // 难度评估（有解时）
    int? SolutionLength,                              // 解长（有解时）
    long? GenerationRunId,                            // 关联生成运行 id（预览跳转用）
    string? FailureSummary,                           // 失败摘要
    long ElapsedMs);                                  // 耗时

// 批量任务状态
public sealed record BatchStatusDto(
    long TaskId,                                      // 任务编号
    string GameId,                                    // 玩法
    string Status,                                    // running|completed|cancelled
    int Total,                                        // 总关数
    int Completed,                                    // 已完成（含成功+失败+跳过）
    int Succeeded,                                    // 成功数
    int Failed,                                       // 失败数
    int Skipped,                                      // 跳过数（编号已入库）
    long TotalElapsedMs,                              // 总耗时
    IReadOnlyList<BatchItemDto> Items);               // 逐关行

// 批量生产服务：同一条生成流水线的调度层（表现层_批量生产 §1）
// 任务队列后台执行、页面可关（单机进程在任务就在）；固定并发窗口、失败自愈不阻塞他关
public sealed class BatchService
{
    #region 运行时状态

    private readonly GenerationPipeline _pipeline;    // 单关流水线（复用，零新写）
    private readonly GameRegistry _registry;          // 玩法注册表（找门面入库）
    private readonly GameStore _games;                // 关卡库（跳过已入库编号+批量入库）
    private readonly SettingsStore _settings;         // 设置（并发默认值等）
    private readonly LevelFileStore _persist;         // 安全落盘（批量入库共用）
    private readonly ConcurrentDictionary<long, BatchTask> _tasks = new();   // 任务表

    private static long _nextTaskId;                  // 自增任务号（线程安全用 Interlocked）

    #endregion

    #region 构造

    public BatchService(GenerationPipeline pipeline, GameRegistry registry,
        GameStore games, SettingsStore settings, LevelFileStore persist)
    {
        _pipeline = pipeline;
        _registry = registry;
        _games = games;
        _settings = settings;
        _persist = persist;
    }

    #endregion

    #region 任务发起与查询

    // 发起批量任务：展开编号段+难度配比 → 后台异步执行整批
    public BatchStatusDto Start(BatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GameId) || !_registry.TryGet(request.GameId, out var facade))
        {
            throw new InvalidOperationException($"未知玩法：{request.GameId}");
        }
        // 编号段合法性
        if (request.FromLevel < 1 || request.ToLevel < request.FromLevel || request.ToLevel - request.FromLevel + 1 > 500)
        {
            throw new InvalidOperationException($"非法编号段 {request.FromLevel}~{request.ToLevel}（须 1≤起≤止 且跨度≤500）");
        }
        var totalLevels = request.ToLevel - request.FromLevel + 1;
        // 难度配比总数须等于编号段长度（配比是精确分配而非比例）
        var totalRatios = request.Difficulty1Count + request.Difficulty2Count + request.Difficulty3Count;
        if (totalRatios != totalLevels)
        {
            throw new InvalidOperationException($"难度配比总数 {totalRatios} ≠ 编号段长度 {totalLevels}");
        }
        var taskId = Interlocked.Increment(ref _nextTaskId);
        var task = new BatchTask(taskId, request.GameId, facade, request, totalLevels);
        _tasks[taskId] = task;
        // 后台启动（不阻塞 API 响应；单机进程在任务就在）
        _ = Task.Run(() => ExecuteBatchAsync(task));
        return ToDto(task);
    }

    // 查询任务状态（前端轮询）
    public BatchStatusDto? GetStatus(long taskId)
    {
        return _tasks.TryGetValue(taskId, out var task) ? ToDto(task) : null;
    }

    // 取消任务：排队关不再执行、进行中的做完当前关
    public bool Cancel(long taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return false;
        task.Cancel();
        return true;
    }

    // 批量入库：把指定编号段的已生成关写入游戏项目目录（复用安全落盘）
    public IReadOnlyList<(int LevelNumber, bool Ok, string? Error)> AdoptRange(
        long taskId, int fromLevel, int toLevel, bool overwrite = false)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return [];
        var results = new List<(int, bool, string?)>();
        foreach (var item in task.Items.Where(i =>
            i.LevelNumber >= fromLevel && i.LevelNumber <= toLevel && i.Status == "solved").ToList())
        {
            // 从关联生成运行取最优候选内容
            var runId = item.GenerationRunId;
            if (runId is null) { results.Add((item.LevelNumber, false, "无关联生成运行")); continue; }
            var content = task.GetAdoptedContent(item.LevelNumber);
            if (content is null) { results.Add((item.LevelNumber, false, "无候选内容")); continue; }
            var outcome = _persist.Persist(task.Facade, content, overwrite, null);
            results.Add((item.LevelNumber, outcome.Ok, outcome.Error));
        }
        return results;
    }

    // 导出全部：成功的关打包为 JSON 列表（前端组 zip 或直接下载）
    public IReadOnlyList<(int LevelNumber, string ContentJson)> ExportSolved(long taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return [];
        return task.Items
            .Where(i => i.Status == "solved")
            .Select(i => (i.LevelNumber, task.GetAdoptedContent(i.LevelNumber) ?? ""))
            .ToList();
    }

    // 重试失败关：把失败项重置为 pending 再启动一轮（仅跑失败关）
    public BatchStatusDto? RetryFailed(long taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var task)) return null;
        var failedItems = task.Items.Where(i => i.Status == "failed").ToList();
        if (failedItems.Count == 0) return ToDto(task);
        foreach (var item in failedItems)
        {
            task.ResetItem(item.LevelNumber);
        }
        task.Status = "running";
        _ = Task.Run(() => ExecuteBatchAsync(task));
        return ToDto(task);
    }

    #endregion

    #region 批量执行

    // 批量执行主体：固定并发窗口 + 逐关状态机
    private async Task ExecuteBatchAsync(BatchTask task)
    {
        var concurrency = task.Request.Concurrency
            ?? Math.Max(1, _settings.Get("gen.batchConcurrency", 4));
        var throttle = new SemaphoreSlim(concurrency, concurrency);
        var pending = task.Items.Where(i => i.Status == "pending" || i.Status == "running").ToList();
        var running = new List<Task>();
        foreach (var item in pending)
        {
            if (task.IsCancelled) break;
            await throttle.WaitAsync();
            var levelNumber = item.LevelNumber;
            var difficulty = task.DifficultyOf(levelNumber);
            running.Add(Task.Run(async () =>
            {
                try
                {
                    await RunSingleLevelAsync(task, levelNumber, difficulty);
                }
                finally
                {
                    throttle.Release();
                }
            }));
        }
        await Task.WhenAll(running);
        // 全部收口
        task.Status = task.IsCancelled ? "cancelled" : "completed";
        task.TotalElapsedMs = (long)(DateTime.UtcNow - task.StartedAt).TotalMilliseconds;
    }

    // 单关执行：生成 → 验证 → 记录状态（失败自愈=流水线内部的重生循环已处理）
    private async Task RunSingleLevelAsync(BatchTask task, int levelNumber, int difficulty)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        task.UpdateItem(levelNumber, "running", null, null, null, null, 0);
        try
        {
            var summary = await _pipeline.RunAsync(new GenerationRequest(
                task.GameId, difficulty, levelNumber.ToString(),
                task.Request.SpecialRequirements,
                task.Request.CandidateLimit,
                task.Request.SolveTimeoutMs));
            task.SetRunId(levelNumber, summary.RunId);
            task.SetContent(levelNumber, summary.Status == "succeeded"
                ? summary.Candidates.LastOrDefault(c => c.FailureClass == "none")?.ContentJson
                : null);
            task.UpdateItem(levelNumber,
                summary.Status == "succeeded" ? "solved" : "failed",
                summary.Candidates.LastOrDefault(c => c.DifficultyBand is not null)?.DifficultyBand,
                summary.Candidates.LastOrDefault(c => c.DifficultyBand is not null)?.SolveRunId,
                summary.RunId,
                summary.Status == "succeeded" ? null : summary.FailureSummary,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            task.UpdateItem(levelNumber, "failed", null, null, null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    // BatchTask → DTO
    private static BatchStatusDto ToDto(BatchTask task)
    {
        return new BatchStatusDto(
            task.TaskId, task.GameId, task.Status,
            task.Total, task.CompletedCount, task.SucceededCount, task.FailedCount, task.SkippedCount,
            task.TotalElapsedMs,
            task.Items.Select(i => new BatchItemDto(
                i.LevelNumber, i.Status, i.DifficultyBand, i.SolutionLength,
                i.GenerationRunId, i.FailureSummary, i.ElapsedMs)).ToList());
    }

    #endregion
}

// 内部任务对象：逐关状态 + 难度映射 + 取消标记 + 内容缓存
internal sealed class BatchTask
{
    public long TaskId { get; }                           // 任务编号
    public string GameId { get; }                         // 玩法
    public IGameFacade Facade { get; }                    // 门面（入库用）
    public BatchRequest Request { get; }                  // 原始请求
    public int Total { get; }                             // 总关数
    public string Status { get; set; } = "running";       // running|completed|cancelled
    public DateTime StartedAt { get; } = DateTime.UtcNow; // 启动时间
    public long TotalElapsedMs { get; set; }              // 总耗时
    private volatile bool _cancelled;                     // 取消标记
    public bool IsCancelled => _cancelled;                // 取消查询

    public List<BatchItem> Items { get; } = [];           // 逐关行
    private readonly Dictionary<int, int> _difficultyMap = new();   // 编号 → 难度
    private readonly Dictionary<int, string?> _contents = new();    // 编号 → 最优候选内容

    public BatchTask(long taskId, string gameId, IGameFacade facade, BatchRequest request, int total)
    {
        TaskId = taskId;
        GameId = gameId;
        Facade = facade;
        Request = request;
        Total = total;
        // 展开难度配比到编号段（按编号顺序铺开：难度1前 N1、难度2中 N2、难度3后 N3）
        var index = 0;
        for (var d = 1; d <= 3; d++)
        {
            var count = d switch { 1 => request.Difficulty1Count, 2 => request.Difficulty2Count, _ => request.Difficulty3Count };
            for (var j = 0; j < count; j++)
            {
                _difficultyMap[request.FromLevel + index] = d;
                Items.Add(new BatchItem(request.FromLevel + index));
                index++;
            }
        }
    }

    // 取消
    public void Cancel() => _cancelled = true;

    // 编号 → 难度
    public int DifficultyOf(int levelNumber) => _difficultyMap.GetValueOrDefault(levelNumber, 2);

    // 更新行状态
    public void UpdateItem(int levelNumber, string status, int? difficultyBand, long? solutionRunId,
        long? generationRunId, string? failureSummary, long elapsedMs)
    {
        var item = Items.FirstOrDefault(i => i.LevelNumber == levelNumber);
        if (item is null) return;
        item.Status = status;
        item.DifficultyBand = difficultyBand;
        item.GenerationRunId = generationRunId;
        item.FailureSummary = failureSummary;
        item.ElapsedMs = elapsedMs;
    }

    // 记录关联生成运行 id
    public void SetRunId(int levelNumber, long runId)
    {
        var item = Items.FirstOrDefault(i => i.LevelNumber == levelNumber);
        if (item is not null) item.GenerationRunId = runId;
    }

    // 缓存候选内容
    public void SetContent(int levelNumber, string? content)
    {
        _contents[levelNumber] = content;
    }

    // 取候选内容
    public string? GetAdoptedContent(int levelNumber) => _contents.GetValueOrDefault(levelNumber);

    // 重置为 pending（重试失败用）
    public void ResetItem(int levelNumber)
    {
        var item = Items.FirstOrDefault(i => i.LevelNumber == levelNumber);
        if (item is null) return;
        item.Status = "pending";
        item.FailureSummary = null;
    }

    // 统计属性
    public int CompletedCount => Items.Count(i => i.Status is "solved" or "failed" or "skipped");
    public int SucceededCount => Items.Count(i => i.Status == "solved");
    public int FailedCount => Items.Count(i => i.Status == "failed");
    public int SkippedCount => Items.Count(i => i.Status == "skipped");
}

// 内部行对象
internal sealed class BatchItem
{
    public int LevelNumber { get; }                       // 编号
    public string Status { get; set; } = "pending";       // pending|running|solved|failed|skipped
    public int? DifficultyBand { get; set; }              // 难度评估
    public long? GenerationRunId { get; set; }            // 关联运行
    public string? FailureSummary { get; set; }           // 失败摘要
    public long ElapsedMs { get; set; }                   // 耗时
    public int? SolutionLength { get; set; }              // 解长

    public BatchItem(int levelNumber)
    {
        LevelNumber = levelNumber;
    }
}
