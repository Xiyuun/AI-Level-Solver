using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.Mahjong;

// 麻将连连看门面（mahjong-260417）：包装 Mahjong 适配层，路径 B 交付物
// 平台消费 IGameFacade 统一入口；求解走内核 GreedyDfs 策略（超大规模状态空间找任一解，语义与游戏官方求解器一致）
public sealed class MahjongFacade : IGameFacade, ILevelComposer, ITokenBudgetHint
{
    public const string AdapterVersionValue = "1.0.0";   // 适配层版本（随适配层发布更新）
    public const string GameIdValue = "mahjong-260417";

    // 纯逻辑无状态适配器：单例复用（适配层铁律：不持有状态、不依赖引擎）
    private static readonly MahjongAdapter AdapterInstance = new();

    // 适配层包：求解/回放/合规检查共用同一实例
    private static readonly AdapterBundle<MahjongLevel, MahjongState, MahjongMove> Bundle = new(
        GameIdValue, AdapterVersionValue, AdapterInstance, AdapterInstance);

    // 供 composer 求解器在环复核使用（同程序集内部可见）
    internal static AdapterBundle<MahjongLevel, MahjongState, MahjongMove> BundleForCompose => Bundle;

    public GameRegistration Registration { get; } = new(
        GameIdValue,
        "麻将连连看（mahjong_260417）",
        GameStatus.Trial,
        AdapterVersionValue,
        AdapterFingerprint.Of(typeof(MahjongFacade).Assembly),
        TapeConstants.KernelVersion,
        TapeConstants.ContractVersion,
        "levels/mahjong-260417",
        true,   // 有真实游戏本体：门2 适用（harness 按 initFromPreloaded 加载同一全预设关卡后逐步实报语义状态）
        MahjongContent.VisualMappingJson,
        MahjongContent.MechanicsJson,
        MahjongContent.GameBrief,
        MahjongContent.FormatSpec,
        MahjongContent.RuleMatrix,
        JsonSerializer.Serialize(MahjongContent.DefaultDifficultyWeights, TapeJson.Options));

    // 内置样例关：真实语料 1.json（16 只棋子全预设，含爆竹对——按官方求解抽象作普通棋）
    public IReadOnlyList<string> BuiltinLevels => [MahjongContent.RealLevel1Json];

    // 关卡编号：原生格式 level 字段（JSON 坏掉时返回 null，由 Solve 如实报 Invalid）
    public string? TryGetLevelId(string levelJson)
    {
        try
        {
            if (JsonNode.Parse(levelJson) is not { } root || root["level"] is not JsonValue value
                || !value.TryGetValue<int>(out var number) || number < 1)
            {
                return null;
            }
            return number.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // 静态格式校验（含 v1 子集边界：生成型关卡规格以"v1 支持子集之外"前缀如实拒绝）
    public IReadOnlyList<string> FormatErrors(string levelJson)
    {
        return MahjongLevels.Validate(levelJson);
    }

    // 求解 + 制带 + 难度评估：JSON/校验失败 → Invalid；有解 → 录制磁带；难度走固定权重模型
    // 策略注入内核 GreedyDfsSolver：满盘 48~120 子的状态空间 BFS 会爆，贪心 DFS 找任一解（与游戏内求解器同形态）
    public PlatformSolve Solve(SolveCommand command)
    {
        var runConfigHash = RunConfig.HashOf(command);
        MahjongLevel? level;
        try
        {
            level = MahjongLevels.Parse(command.LevelJson).Level;
        }
        catch (JsonException)
        {
            level = null;
        }
        if (level is null)
        {
            var parseErrors = SafeValidate(command.LevelJson);
            return InvalidOutcome(runConfigHash, string.IsNullOrWhiteSpace(parseErrors)
                ? "关卡 JSON 解析失败"
                : $"关卡未通过静态校验：{parseErrors}");
        }
        var request = new SolveRequest<MahjongLevel>(
            level, command.TimeBudget, command.NodeBudget, command.MemoryBudgetBytes, command.FullEnumeration);
        var result = new SolveExecutor().Solve(Bundle, request, new GreedyDfsSolver<MahjongLevel, MahjongState, MahjongMove>());
        // 磁带仅在有解时录制：解法磁带承载操作序列，无解/未知没有可回放内容
        SolutionTape? tape = null;
        if (result.Status == SolveStatus.Solved)
        {
            tape = new ReplayEngine().RecordTape(Bundle, level, command.LevelFileBytes, result,
                new TapeMetadata(command.LevelRef, Registration.AdapterHash, runConfigHash));
        }
        var features = FeatureExtractor.Extract(Bundle, result);
        var difficulty = DifficultyModel.Evaluate(features, result.Status, MahjongContent.DefaultDifficultyWeights);
        return new PlatformSolve(result, tape, difficulty, runConfigHash);
    }

    // 门1五项合规检查：组装本玩法样本（键判别维度对/纯函数样本/确定性代表关+字节）后交 AdapterHosting
    // 样本源 = 门1代表关（8×8 覆盖 Normal/Ice/Cont/Flip/Wall 与墙障碍）；
    // 键判别维度对 = 规则矩阵状态维度（棋子位置/牌面/冰冻计数/连排组号/墙体配置）
    public ComplianceReport RunGate1Checks()
    {
        var levelJson = MahjongContent.Gate1SampleLevelJson;
        var level = MahjongLevels.Load(levelJson);
        var bytes = System.Text.Encoding.UTF8.GetBytes(levelJson);
        var initial = AdapterInstance.LoadLevel(level);

        var piece0 = initial.Pieces.First(p => p.Id == 1);
        var ice = initial.Pieces.First(p => p.Id == 5);
        var cont = initial.Pieces.First(p => p.Id == 9);
        var flipped = initial.Pieces.First(p => p.Id == 7);
        var contMate = initial.Pieces.First(p => p.Id == 10);

        // 键判别维度对：规则矩阵声明的全部状态维度（翻面朝向/爆竹连锁按官方抽象归并，不入键——矩阵 #6/#7）
        var keySamples = new StatePairSample<MahjongState>[]
        {
            new("棋子位置", initial, initial.With(Replace(initial.Pieces, piece0 with { X = piece0.X + 1 }))),
            new("棋子牌面", initial, initial.With(Replace(initial.Pieces, piece0 with { Type = piece0.Type + 1 }))),
            new("冰冻计数", initial, initial.With(Replace(initial.Pieces, ice with { SpecialInfo = ice.SpecialInfo + 1 }))),
            new("连排组号", initial, initial.With(Replace(Replace(initial.Pieces, cont with { SpecialInfo = 8 }), contMate with { SpecialInfo = 8 }))),
            new("翻面棋子存在性", initial, initial.With(initial.Pieces.Where(p => p.SpecialType != MahjongSpecial.Flip).ToList())),
            new("墙体配置", initial, initial.WithWalls(initial.Walls.Append(new MahjongWall(5, 2, 1)).ToList())),
        };
        var firstMove = AdapterInstance.GetLegalMoves(initial)[0];
        var input = new HostingCheckInput<MahjongLevel, MahjongState, MahjongMove>(
            Bundle, typeof(MahjongAdapter).Assembly,
            keySamples,
            (initial, firstMove),
            level, bytes);
        return new AdapterHosting().RunAll(input);
    }

    // 试玩步进：从初态顺序执行结构化动作（与求解执行同一份适配层）
    // 无状态设计：每次请求带完整动作序列重放；非法动作/关卡抛 PlayException
    public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
    {
        MahjongLevel? level;
        try
        {
            level = MahjongLevels.Parse(levelJson).Level;
        }
        catch (JsonException)
        {
            level = null;
        }
        if (level is null)
        {
            throw new PlayException(string.IsNullOrWhiteSpace(levelJson)
                ? "关卡 JSON 为空"
                : $"关卡未通过静态校验：{SafeValidate(levelJson)}");
        }
        var state = AdapterInstance.LoadLevel(level);
        var index = 0;
        foreach (var node in moves)
        {
            MahjongMove move;
            try
            {
                move = AdapterInstance.DecodeMove(node);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new PlayException($"第 {index + 1} 步动作编码非法：{ex.Message}");
            }
            // 只接受当前局面的合法动作（契约：move 须来自本状态 GetLegalMoves）
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

    // ─── 生成端能力（全预设直通出题）───

    // 出题规格：意图制（模型出尺寸/难度/冰冻对数，几何由代码按种子确定性拼装——多米诺铺装恒可解）
    public string CompositionSpec => MahjongComposer.Spec;

    // 全预设配置较大（120 子约 6~8KB），含修复余量给 8192
    public int EstimateLevelTokens(string? levelNumber) => 8192;

    // 意图制拼装（模型出意图 + 代码拼几何 + 求解器在环复核）；模型直接给出原生配置时按原生直通校验
    public ComposeResult Compose(string modelOutput, string? levelNumber)
    {
        return Compose(modelOutput, levelNumber, new ComposeContext(levelNumber, 2, null, 0));
    }

    public ComposeResult Compose(string modelOutput, string? levelNumber, ComposeContext context)
        => MahjongComposer.Compose(modelOutput, levelNumber, context);

    public IReadOnlyList<string> ValidateContext(ComposeContext context) => MahjongComposer.ValidateContext(context);

    // 语料关卡多为生成型规格（chesses 不全），不能作为合法 few-shot 示例喂给模型
    public bool WantsFewShot => false;

    public string? DescribeAsCoordinates(string levelJson) => null;

    #region 内部工具

    // 校验失败信息（Fail 快路径里做一次防御性校验，读不出来返回空串）
    private static string SafeValidate(string levelJson)
    {
        try
        {
            return string.Join("；", MahjongLevels.Validate(levelJson));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // 替换棋子列表中的一枚（键判别维度对构造用）
    private static IReadOnlyList<MahjongPiece> Replace(IReadOnlyList<MahjongPiece> pieces, MahjongPiece updated)
        => pieces.Select(p => p.Id == updated.Id ? updated : p).OrderBy(p => p.Id).ToList();

    // JSON 层面/校验层面即不可用：直接给 Invalid 结论（零 metrics），不进入求解器；难度不适用（band=0）
    private static PlatformSolve InvalidOutcome(string? runConfigHash, string message)
    {
        var result = new SolveResult(
            SolveStatus.Invalid, Optimality.NotApplicable, false,
            TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix,
            [], null, message);
        var difficulty = DifficultyModel.Evaluate(
            new DifficultyFeatures(0, Optimality.NotApplicable, 0, 0, 0, 0, 0, null),
            result.Status, MahjongContent.DefaultDifficultyWeights);
        return new PlatformSolve(result, null, difficulty, runConfigHash);
    }

    #endregion
}
