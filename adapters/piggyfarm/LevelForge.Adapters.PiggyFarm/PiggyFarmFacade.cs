using System.Text;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm;

// piggyfarm 门面：把 ClassicMode 适配层包成平台统一入口（插件装载后由 GameRegistry 动态注册）
// 门面类必须能无参构造——PluginLoader 用 Activator.CreateInstance 创建
public sealed class PiggyFarmFacade : IGameFacade, ILevelQualityGate, ILevelComposer, ITokenBudgetHint
{
    // 纯逻辑无状态适配器：单例复用（适配层铁律：不持有状态、不依赖引擎）
    private static readonly PiggyFarmAdapter AdapterInstance = new();

    // 适配层包：求解/回放/合规检查共用同一实例
    private static readonly AdapterBundle<PiggyFarmLevel, PiggyFarmState, PiggyFarmMove> Bundle = new(
        "piggyfarm", PiggyFarmContent.AdapterVersionValue, AdapterInstance, AdapterInstance);

    private static readonly PiggyFarmSolver Solver = new();

    // 注册元数据：GameId 必须与平台已导入玩法行一字不差
    public GameRegistration Registration { get; } = new(
        "piggyfarm",
        "PiggyFarm（经典模式）",
        GameStatus.Trial,
        PiggyFarmContent.AdapterVersionValue,
        AdapterFingerprint.Of(typeof(PiggyFarmFacade).Assembly),
        TapeConstants.KernelVersion,
        TapeConstants.ContractVersion,
        "levels/piggyfarm",
        true,   // 有真实游戏本体：门2 真机对拍适用（驱动待登记，属于 wizard⑤ 的后续工作）
        PiggyFarmContent.VisualMapping,
        PiggyFarmContent.MechanicsTable,
        PiggyFarmContent.GameBrief,
        PiggyFarmContent.FormatSpec,
        PiggyFarmContent.RuleMatrix,
        PiggyFarmContent.DifficultyWeightsJson);

    // 导入玩法没有内置关卡：关卡由向导从项目编辑源目录导入（BuiltinLevels 为空时 SeedLevels 不写库）
    public IReadOnlyList<string> BuiltinLevels => Array.Empty<string>();

    // 关卡编号：原生格式 levelId（JSON 坏掉时返回 null，由 Solve 如实报 Invalid）
    public string? TryGetLevelId(string levelJson)
    {
        return PiggyFarmLevels.TryReadLevelId(levelJson)?.ToString();
    }

    // 静态格式校验（接入产物校验器）：只判格式与子集范围，不判可解性
    public IReadOnlyList<string> FormatErrors(string levelJson)
    {
        return PiggyFarmLevels.Validate(levelJson);
    }

    // 结构化出题：模型只选档位，几何由模板拼装（详见 PiggyFarmComposer）
    public string CompositionSpec => PiggyFarmComposer.Spec;

    public ComposeResult Compose(string modelOutput, string? levelNumber) =>
        PiggyFarmComposer.Compose(modelOutput, levelNumber);

    public ComposeResult Compose(string modelOutput, string? levelNumber, ComposeContext context) =>
        PiggyFarmComposer.Compose(modelOutput, levelNumber, context);

    public IReadOnlyList<string> ValidateContext(ComposeContext context) =>
        PiggyFarmComposer.ValidateContext(context);

    // 模板出题不传原生 few-shot（避免模型照抄几何，也跳过逐关求解采样）
    public bool WantsFewShot => false;
    public string? DescribeAsCoordinates(string levelJson) => null;

    // 模板出题输出只是一个 {"band":N}，1k token 足够
    public int EstimateLevelTokens(string? levelNumber) => 1024;

    // 生成质量门（仅作用于生成候选，不拦导入的真实关卡）：
    // 格式合法 ≠ 设计可用——拦截"全部同朝向/独立直道/初始全可走"的规则网格。
    public IReadOnlyList<string> QualityErrors(string levelJson)
    {
        var format = PiggyFarmLevels.Validate(levelJson);
        if (format.Count > 0) return format;   // 格式问题交给 FormatErrors 处理，这里不重复报
        var level = PiggyFarmLevels.Load(levelJson);
        var pigs = level.Pigs;
        var errors = new List<string>();

        var facings = pigs.Select(p => p.Facing).Distinct().Count();
        if (pigs.Count >= 8 && facings < 3)
        {
            errors.Add($"朝向过少：{pigs.Count} 只猪只有 {facings} 种朝向（至少 3 种；禁止全部同向/单排直道）");
        }

        var state = AdapterInstance.LoadLevel(level);
        var legal = AdapterInstance.GetLegalMoves(state).Count;
        if (pigs.Count >= 8 && legal * 2 > pigs.Count)
        {
            errors.Add($"初始可走猪过多：{legal}/{pigs.Count} 只可直接起步（需 ≤50%，要有阻挡依赖）");
        }
        var blocked = pigs.Count - legal;
        var requiredBlocked = Math.Max(2, pigs.Count / 5);
        if (pigs.Count >= 8 && blocked < requiredBlocked)
        {
            errors.Add($"受阻猪过少：仅 {blocked}/{pigs.Count} 只被其它猪阻挡（至少 {requiredBlocked} 只）");
        }

        return errors;
    }

    // 求解 + 制带 + 难度评估：JSON/规则非法 → Invalid；有解 → 录制磁带；难度只对有解给档
    public PlatformSolve Solve(SolveCommand command)
    {
        var runConfigHash = RunConfig.HashOf(command);
        PiggyFarmLevel level;
        try
        {
            level = PiggyFarmLevels.Load(command.LevelJson);
        }
        catch (ArgumentException ex)
        {
            return InvalidOutcome(runConfigHash, ex.Message);
        }

        SolveResult result;
        try
        {
            // FullEnumeration 是内核搜索的枚举模式提示；本适配层自带两阶段搜索，阶段2 本身就是完整遍历，
            // 因此该标志不影响结论语义（无解只在阶段2 穷尽后声明）
            result = Solver.Solve(level, command.TimeBudget, command.NodeBudget,
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return InvalidOutcome(runConfigHash, $"适配层拒绝该关卡：{ex.Message}");
        }

        // 磁带仅在有解时录制：解法磁带承载操作序列，无解/未知没有可回放内容
        SolutionTape? tape = null;
        if (result.Status == SolveStatus.Solved)
        {
            tape = new ReplayEngine().RecordTape(Bundle, level, command.LevelFileBytes, result,
                new TapeMetadata(command.LevelRef, Registration.AdapterHash, runConfigHash));
        }
        var features = FeatureExtractor.Extract(Bundle, result);
        var difficulty = DifficultyModel.Evaluate(features, result.Status, PiggyFarmContent.Weights);
        return new PlatformSolve(result, tape, difficulty, runConfigHash);
    }

    // 门1 五项合规检查：组装本玩法样本（键判别维度对/纯函数样本/确定性关卡）后交 AdapterHosting
    // 样本来源 = 规则矩阵声明的状态维度：位置（占格）与朝向
    public ComplianceReport RunGate1Checks()
    {
        var level = PiggyFarmLevels.Load(PiggyFarmContent.SampleLevelJson);
        var initial = AdapterInstance.LoadLevel(level);
        var firstPig = initial.Pigs[0];
        var shifted = new PiggyFarmPig(firstPig.Id, firstPig.Type, firstPig.Facing,
            PiggyFarmAdapter.Normalize(firstPig.Cells.Select(c => new GridCell(c.X + 1, c.Y))));
        var flipped = new PiggyFarmPig(firstPig.Id, firstPig.Type,
            firstPig.Facing == PigFacing.Up ? PigFacing.Down : PigFacing.Up, firstPig.Cells);
        var keySamples = new StatePairSample<PiggyFarmState>[]
        {
            new("位置（占格）", initial, initial with
            {
                Pigs = initial.Pigs.Select(p => p.Id == firstPig.Id ? shifted : p).ToArray()
            }),
            new("朝向", initial, initial with
            {
                Pigs = initial.Pigs.Select(p => p.Id == firstPig.Id ? flipped : p).ToArray()
            })
        };
        var legal = AdapterInstance.GetLegalMoves(initial);
        if (legal.Count == 0)
        {
            throw new InvalidOperationException("门1样本关没有合法动作：样本关选择有误");
        }
        var input = new HostingCheckInput<PiggyFarmLevel, PiggyFarmState, PiggyFarmMove>(
            Bundle, typeof(PiggyFarmAdapter).Assembly,
            keySamples,
            (initial, legal[0]),
            level, Encoding.UTF8.GetBytes(PiggyFarmContent.SampleLevelJson));
        return new AdapterHosting().RunAll(input);
    }

    // 试玩步进：从初态顺序执行结构化动作（与求解执行同一份适配层）
    // 无状态设计：每次请求带完整动作序列重放；非法动作/关卡抛 PlayException
    public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
    {
        PiggyFarmLevel level;
        try
        {
            level = PiggyFarmLevels.Load(levelJson);
        }
        catch (ArgumentException ex)
        {
            throw new PlayException($"关卡未通过静态校验：{ex.Message}");
        }
        var state = AdapterInstance.LoadLevel(level);
        var index = 0;
        foreach (var node in moves)
        {
            PiggyFarmMove move;
            try
            {
                move = AdapterInstance.DecodeMove(node);
            }
            catch (ArgumentException ex)
            {
                throw new PlayException($"第 {index + 1} 步动作编码非法：{ex.Message}");
            }
            if (!AdapterInstance.GetLegalMoves(state).Contains(move))
            {
                var detail = state.Pigs.Any(p => p.Id == move.PigId)
                    ? AdapterInstance.DescribeMove(state, move)
                    : $"关卡中没有猪 #{move.PigId}";
                throw new PlayException($"第 {index + 1} 步动作不合法：{detail}");
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

    // JSON/规则层面即不可用：直接给 Invalid 结论（零 metrics），难度不适用（band=0）
    private PlatformSolve InvalidOutcome(string? runConfigHash, string message)
    {
        var result = new SolveResult(
            SolveStatus.Invalid, Optimality.NotApplicable, false,
            TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix,
            [], null, message);
        var difficulty = DifficultyModel.Evaluate(
            new DifficultyFeatures(0, Optimality.NotApplicable, 0, 0, 0, 0, 0, null),
            result.Status, PiggyFarmContent.Weights);
        return new PlatformSolve(result, null, difficulty, runConfigHash);
    }

    #endregion
}
