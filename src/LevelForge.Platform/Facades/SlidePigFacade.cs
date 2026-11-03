using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;

namespace LevelForge.Platform.Facades;

// 滑动猪门面：包装 SlidePig 适配层（内部参考实现；规则矩阵见 SlidePigArtifacts）
// 求解策略用 A*（适配层声明 IHeuristicAdapter：剩余非笼实体数；BFS 实测稠密关组合爆炸，见 PROGRESS 坑表）
// 关卡数据不入仓库：内置关卡为空，110 关经向导/导入 API 从游戏项目目录原生导入（滑动猪仅内部，方案书 §8 脱敏要求）
public sealed class SlidePigFacade : IGameFacade
{
    public const string AdapterVersionValue = "0.1.0";   // 适配层版本（v1 核心子集：75/110 关）

    // 纯逻辑无状态适配器：单例复用
    private static readonly SlidePigAdapter AdapterInstance = new();

    // 适配层包：门面内唯一组装点
    private static readonly AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> Bundle = new(
        "slide-pig", AdapterVersionValue, AdapterInstance, AdapterInstance);

    // 求解策略：按关卡规模选择（求解内核 §2 策略选择——适配层能力+状态空间规模）
    // 普查实测（110 关）：除 1 号关（6 猪）外全部 61~122 实体；机动性+腾挪预算阶梯把可解覆盖从 1/75 提到 31/75
    private const int DensePigThreshold = 40;
    private static readonly AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove> OptimalStrategy = new();

    // 稠密关求解（实例方法：收尾要用 Registration.AdapterHash）：机动性排序 + 腾挪预算阶梯（8→16→32，预算平分）
    private PlatformSolve SolveDense(
        AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> bundle, SlidePigLevel level, SolveCommand command, string? runConfigHash)
    {
        var totalMs = (long)(command.TimeBudget?.TotalMilliseconds ?? 30_000);
        var tiers = new[] { 8, 16, 32 };
        var tierMs = Math.Max(1000, totalMs / tiers.Length);
        var budget = command.NodeBudget;
        SolveResult best = new SolveResult(
            SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
            TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "稠密关求解未运行");
        foreach (var shuffle in tiers)
        {
            var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
                new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(
                    MobilityTiebreak: true, ShuffleBudget: shuffle));
            var r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(
                level, TimeSpan.FromMilliseconds(tierMs), budget, command.MemoryBudgetBytes, command.FullEnumeration));
            if (r.Status == SolveStatus.Solved) { best = r; break; }
            if (best.Status != SolveStatus.Solved) best = r;
        }
        return FinishSolve(bundle, level, command, best, runConfigHash);
    }

    // 求解结果收尾：制带（有解时）+ 难度评估（两策略共用；Registration 属实例成员故为实例方法）
    private PlatformSolve FinishSolve(
        AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> bundle, SlidePigLevel level, SolveCommand command,
        SolveResult result, string? runConfigHash)
    {
        SolutionTape? tape = null;
        if (result.Status == SolveStatus.Solved)
        {
            tape = new ReplayEngine().RecordTape(bundle, level, command.LevelFileBytes, result,
                new TapeMetadata(command.LevelRef, Registration.AdapterHash, runConfigHash));
        }
        var features = FeatureExtractor.Extract(bundle, result);
        var difficulty = DifficultyModel.Evaluate(features, result.Status, SlidePigArtifacts.DefaultDifficultyWeights);
        return new PlatformSolve(result, tape, difficulty, runConfigHash);
    }

    public GameRegistration Registration { get; } = new(
        "slide-pig",
        "滑动猪（PiggyFarm 主线）",
        GameStatus.Trial,   // 试用态先行（接入向导④后）：门2/门3 未跑，结论带"未经对拍"水印；转正后改 Production
        AdapterVersionValue,
        AdapterFingerprint.Of(typeof(SlidePigAdapter).Assembly),
        TapeConstants.KernelVersion,
        TapeConstants.ContractVersion,
        "levels/slide-pig",
        true,   // 有真实游戏本体：门2 真机对拍适用（Unity batchmode 驱动，harness 见 harness-templates）
        SlidePigArtifacts.VisualMapping,
        SlidePigArtifacts.MechanicsTable,
        SlidePigArtifacts.GameBrief,
        SlidePigArtifacts.FormatSpec,
        SlidePigArtifacts.RuleMatrix,
        JsonSerializer.Serialize(SlidePigArtifacts.DefaultDifficultyWeights, TapeJson.Options));

    // 关卡不随工具仓库分发（公司数据）：导入时从游戏项目 Levels 目录读取原生 JSON
    public IReadOnlyList<string> BuiltinLevels => Array.Empty<string>();

    // 关卡编号：原生格式 levelId 字段（数字转字符串统一）
    public string? TryGetLevelId(string levelJson)
    {
        try
        {
            return JsonNode.Parse(levelJson)?["levelId"]?.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 静态格式校验：直用接入产物校验器（含 v1 支持子集判定——Duck/Rope/Scissors 关明确报不支持）
    public IReadOnlyList<string> FormatErrors(string levelJson)
    {
        return SlidePigValidator.ValidateJson(levelJson).Errors;
    }

    // 求解 + 制带 + 难度评估（A* 策略；未知不冒充无解）
    public PlatformSolve Solve(SolveCommand command)
    {
        var runConfigHash = RunConfig.HashOf(command);
        SlidePigLevel? level;
        try
        {
            level = SlidePigLevel.FromJson(command.LevelJson);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException or NullReferenceException)
        {
            level = null;   // 坏 JSON 统一走 Invalid，禁止静默修复
        }
        if (level is null)
        {
            return InvalidOutcome(runConfigHash, "关卡 JSON 解析失败：缺少 levelId/mapWidth/mapHeight/pigs 等必要字段");
        }
        // 稀疏关 A*（最优解）；稠密关机动性+预算阶梯（任一解，NotProven 如实标注）
        if (level.Pigs.Count >= DensePigThreshold)
        {
            return SolveDense(Bundle, level, command, runConfigHash);
        }
        var request = new SolveRequest<SlidePigLevel>(
            level, command.TimeBudget, command.NodeBudget, command.MemoryBudgetBytes, command.FullEnumeration);
        var result = new SolveExecutor().Solve(Bundle, request, OptimalStrategy);
        return FinishSolve(Bundle, level, command, result, runConfigHash);
    }

    // 门1五项合规检查：样本组装（维度对/纯函数样本/确定性代表关）后交 AdapterHosting
    // 确定性代表关取首个导入关由调用侧覆盖不可行（门面无状态）——固定用 1 号关 JSON 内联样本
    public ComplianceReport RunGate1Checks()
    {
        var seedJson = """
            {"levelId":1,"levelName":"gate1-sample","mapWidth":6,"mapHeight":6,"pigCount":3,"pigs":[
            {"id":0,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":0,"y":1},{"x":1,"y":1}]},
            {"id":1,"facing":"Up","pigType":"Boom","specialInfo":"2","occupiedCells":[{"x":3,"y":1},{"x":3,"y":2}]},
            {"id":2,"facing":"Down","pigType":"Lock","specialInfo":"1","occupiedCells":[{"x":5,"y":4},{"x":5,"y":5}]}]}
            """;
        var level = SlidePigLevel.FromJson(seedJson);
        var bytes = System.Text.Encoding.UTF8.GetBytes(seedJson);
        var initial = AdapterInstance.LoadLevel(level);
        var firstMove = AdapterInstance.GetLegalMoves(initial)[0];
        // 键判别维度对：猪位置（滑动）/ 爆炸计数 / 解锁计数 / 失败标志（规则矩阵声明的全部可变维度）
        var keySamples = new StatePairSample<SlidePigState>[]
        {
            new("猪位置", initial, AdapterInstance.ApplyMove(initial, firstMove)),
            new("爆炸计数", initial,
                initial with { Pigs = initial.Pigs.Select(p => p.Id == 1 ? p with { Counter = p.Counter + 1 } : p).ToList() }),
            new("解锁计数", initial,
                initial with { Pigs = initial.Pigs.Select(p => p.Id == 2 ? p with { Counter = p.Counter - 1 } : p).ToList() }),
            new("失败标志", initial, initial with { Failed = true })
        };
        var input = new HostingCheckInput<SlidePigLevel, SlidePigState, SlidePigMove>(
            Bundle, typeof(SlidePigAdapter).Assembly,
            keySamples,
            (initial, firstMove),
            level, bytes);
        return new AdapterHosting().RunAll(input);
    }

    // 试玩步进：与求解执行同一份适配层；无状态全序列重放（渲染规范 §6）
    public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
    {
        SlidePigLevel? level;
        try
        {
            level = SlidePigLevel.FromJson(levelJson);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException or InvalidOperationException or NullReferenceException)
        {
            level = null;
        }
        if (level is null)
        {
            throw new PlayException("关卡 JSON 非法：缺少必要字段");
        }
        SlidePigState state;
        try
        {
            state = AdapterInstance.LoadLevel(level);
        }
        catch (ArgumentException ex)
        {
            throw new PlayException($"关卡未通过静态校验：{ex.Message}");
        }
        var index = 0;
        foreach (var node in moves)
        {
            SlidePigMove move;
            try
            {
                move = AdapterInstance.DecodeMove(node);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new PlayException($"第 {index + 1} 步动作编码非法：{ex.Message}");
            }
            if (!AdapterInstance.GetLegalMoves(state).Contains(move))
            {
                throw new PlayException($"第 {index + 1} 步动作不合法：{AdapterInstance.DescribeMove(state, move)}");
            }
            state = AdapterInstance.ApplyMove(state, move);
            index++;
        }
        var legal = AdapterInstance.GetLegalMoves(state)
            .Select(m => new PlayMove(AdapterInstance.EncodeMove(m), AdapterInstance.DescribeMove(state, m)))
            .ToList();
        return new PlayState(AdapterInstance.Snapshot(state), legal, AdapterInstance.IsGoal(state), index);
    }

    #region 内部工具

    // JSON 层面即不可用：直接 Invalid（零 metrics），难度不适用
    private static PlatformSolve InvalidOutcome(string? runConfigHash, string message)
    {
        var result = new SolveResult(
            SolveStatus.Invalid, Optimality.NotApplicable, false,
            TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix,
            [], null, message);
        var difficulty = DifficultyModel.Evaluate(
            new DifficultyFeatures(0, Optimality.NotApplicable, 0, 0, 0, 0, 0, null),
            result.Status, SlidePigArtifacts.DefaultDifficultyWeights);
        return new PlatformSolve(result, null, difficulty, runConfigHash);
    }

    #endregion
}
