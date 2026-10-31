using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Adapters.SokobanToy;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;

namespace LevelForge.Platform.Facades;

// 玩具推箱子门面：包装 SokobanToy 适配层（开源默认参考实现）
// 原生 JSON → SokobanLevel 的解析属玩法知识，收在门面内；平台其余部分零游戏语义
public sealed class SokobanToyFacade : IGameFacade
{
    public const string AdapterVersionValue = "1.0.0";   // 适配层版本（随适配层发布更新）

    // 纯逻辑无状态适配器：单例复用（适配层铁律：不持有状态、不依赖引擎）
    private static readonly SokobanToyAdapter AdapterInstance = new();

    // 适配层包：门面内的唯一组装点（求解/回放/合规检查共用同一实例）
    private static readonly AdapterBundle<SokobanLevel, SokobanState, SokobanMove> Bundle = new(
        "sokoban-toy", AdapterVersionValue, AdapterInstance, AdapterInstance);

    public GameRegistration Registration { get; } = new(
        "sokoban-toy",
        "玩具推箱子（内置参考）",
        GameStatus.Production,
        AdapterVersionValue,
        AdapterFingerprint.Of(typeof(SokobanToyAdapter).Assembly),
        TapeConstants.KernelVersion,
        TapeConstants.ContractVersion,
        "levels/sokoban-toy",
        false,   // 无独立游戏本体：门2真机对拍不适用（规则矩阵备注），前端显示"对拍不适用"而非"未通过"
        SokobanToyContent.VisualMapping,
        SokobanToyContent.MechanicsTable,
        SokobanToyContent.GameBrief,
        SokobanToyContent.FormatSpec,
        SokobanToyContent.RuleMatrix,
        JsonSerializer.Serialize(SokobanToyContent.DefaultDifficultyWeights, TapeJson.Options));

    public IReadOnlyList<string> BuiltinLevels => SokobanToyContent.LevelJsons;

    // 关卡编号：原生格式的 id 字段（JSON 层坏掉时返回 null，由 Solve 如实报 Invalid）
    public string? TryGetLevelId(string levelJson)
    {
        try
        {
            return JsonNode.Parse(levelJson)?["id"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 静态格式校验：直用接入产物校验器（生成端出题分析 §3.2：只判格式不判可解性）
    public IReadOnlyList<string> FormatErrors(string levelJson)
    {
        return SokobanToyValidator.ValidateJson(levelJson).Errors;
    }

    // 求解 + 制带 + 难度评估：JSON 解析失败 → Invalid；有解 → 录制磁带；难度走固定权重模型
    public PlatformSolve Solve(SolveCommand command)
    {
        var runConfigHash = RunConfig.HashOf(command);
        SokobanLevel? level;
        try
        {
            level = JsonSerializer.Deserialize<SokobanLevel>(command.LevelJson, TapeJson.Options);
        }
        catch (JsonException)
        {
            level = null;   // 坏 JSON 统一走下方缺字段分支报 Invalid，禁止静默修复
        }
        if (level is null || level.Id is null || level.Grid is null)
        {
            return InvalidOutcome(runConfigHash, "关卡 JSON 解析失败：缺少合法的 id/grid 字段");
        }
        // 语义级校验（矩形/字符/数量）在 LoadLevel 内做，违规抛 ArgumentException → Invalid
        var request = new SolveRequest<SokobanLevel>(
            level, command.TimeBudget, command.NodeBudget, command.MemoryBudgetBytes, command.FullEnumeration);
        var result = new SolveExecutor().Solve(Bundle, request);
        // 磁带仅在有解时录制：解法磁带承载操作序列，无解/未知没有可回放内容
        SolutionTape? tape = null;
        if (result.Status == SolveStatus.Solved)
        {
            tape = new ReplayEngine().RecordTape(Bundle, level, command.LevelFileBytes, result,
                new TapeMetadata(command.LevelRef, Registration.AdapterHash, runConfigHash));
        }
        var features = FeatureExtractor.Extract(Bundle, result);
        var difficulty = DifficultyModel.Evaluate(features, result.Status, SokobanToyContent.DefaultDifficultyWeights);
        return new PlatformSolve(result, tape, difficulty, runConfigHash);
    }

    // 门1五项合规检查：组装本玩法样本（键判别维度对/纯函数样本/确定性关卡）后交 AdapterHosting
    // 样本来源=适配层测试套件口径：维度对直接构造"仅该维度不同"的状态（验证门 §3.1）
    public ComplianceReport RunGate1Checks()
    {
        var levelJson = SokobanToyContent.LevelJsons[0];   // toy-001：确定性检查代表关
        var level = JsonSerializer.Deserialize<SokobanLevel>(levelJson, TapeJson.Options)!;
        var bytes = System.Text.Encoding.UTF8.GetBytes(levelJson);
        var initial = AdapterInstance.LoadLevel(level);
        // 键判别维度对：玩家位置 / 箱子位置（规则矩阵声明的全部状态维度）
        var keySamples = new StatePairSample<SokobanState>[]
        {
            new("玩家位置", initial, initial with { Px = initial.Px + 1 }),
            new("箱子位置", initial, initial with { Boxes = [..initial.Boxes.Select((b, i) => i == 0 ? (b.X + 1, b.Y) : b)] })
        };
        var input = new HostingCheckInput<SokobanLevel, SokobanState, SokobanMove>(
            Bundle, typeof(SokobanToyAdapter).Assembly,
            keySamples,
            (initial, AdapterInstance.GetLegalMoves(initial)[0]),
            level, bytes);
        return new AdapterHosting().RunAll(input);
    }

    #region 内部工具

    // JSON 层面即不可用：直接给 Invalid 结论（零 metrics），不进入求解器；难度不适用（band=0）
    private static PlatformSolve InvalidOutcome(string? runConfigHash, string message)
    {
        var result = new SolveResult(
            SolveStatus.Invalid, Optimality.NotApplicable, false,
            TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix,
            [], null, message);
        var difficulty = DifficultyModel.Evaluate(
            new DifficultyFeatures(0, Optimality.NotApplicable, 0, 0, 0, 0, 0, null),
            result.Status, SokobanToyContent.DefaultDifficultyWeights);
        return new PlatformSolve(result, null, difficulty, runConfigHash);
    }

    #endregion
}
