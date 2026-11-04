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
// 坐标制出题（ILevelComposer）：模型只描述意图（头格+类型+朝向），脚印由代码按实测规则推导
// （10097 只猪验证：矩形类 100% 遵循"头格沿朝向轴、向反向延伸"；Cage=5×5 整块、CageL=5×5 缺左上 3×3）
public sealed class SlidePigFacade : IGameFacade, ITokenBudgetHint, ILevelComposer
{
    public const string AdapterVersionValue = "0.1.0";   // 适配层版本（v1 核心子集：75/110 关）

    // token 预算：坐标制意图每猪约 25 token，30 只 ≈ 750 + 规格 ≈ 1200，2048 充足
    public int EstimateLevelTokens(string? levelNumber) => 2048;

    // 纯逻辑无状态适配器：单例复用
    private static readonly SlidePigAdapter AdapterInstance = new();

    // 适配层包：门面内唯一组装点
    private static readonly AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> Bundle = new(
        "slide-pig", AdapterVersionValue, AdapterInstance, AdapterInstance);

    // 求解策略：按关卡规模选择（求解内核 §2 策略选择——适配层能力+状态空间规模）
    // 普查实测（110 关）：除 1 号关（6 猪）外全部 61~122 实体；机动性+腾挪预算阶梯把可解覆盖从 1/75 提到 31/75
    private const int DensePigThreshold = 40;
    private static readonly AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove> OptimalStrategy = new();

    // 稠密关求解（实例方法：收尾要用 Registration.AdapterHash）
    // 策略阶梯（M1 实测 75 关：束搜索 W=64 在 44 个"贪心 30s 未解"关里解出 22 个，多为 1~3s；
    // 但束搜索会漏掉 4 个贪心能解的关，W=256 只多解 1 关且吃满预算——所以三级都要，按剩余预算动态分配）：
    //   ① 束搜索 W=64（主力）：命中率最高、最快
    //   ② 束搜索 W=256（加宽）：处理窄束枯竭的关，代价高，只给它剩余预算的一部分
    //   ③ 贪心 DFS 腾挪阶梯（兜底）：束宽剪枝可能正好剪掉唯一可行路径
    // 全程失败则如实报预算耗尽（未知），绝不冒充无解
    private PlatformSolve SolveDense(
        AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> bundle, SlidePigLevel level, SolveCommand command, string? runConfigHash)
    {
        var totalMs = (long)(command.TimeBudget?.TotalMilliseconds ?? 30_000);
        var budget = command.NodeBudget;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 剩余预算（保底 400ms，避免退化为 0 预算空转）
        long Remaining() => Math.Max(400, totalMs - sw.ElapsedMilliseconds);
        SolveResult best = new SolveResult(
            SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
            TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "稠密关求解未运行");
        // 记录"探索最多"的失败结果作为对外结论（比记录最后一次更能反映实际投入）
        void Keep(SolveResult r)
        {
            if (r.Status == SolveStatus.Solved) return;
            if ((r.Metrics?.StatesExplored ?? 0) > (best.Metrics?.StatesExplored ?? 0)) best = r;
        }

        // ① 窄束（主力）：成功即返回；失败通常 2~3 秒内枯竭，剩余预算自动让给后面两级
        var narrow = new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(Width: 64));
        var r = narrow.Solve(bundle, new SolveRequest<SlidePigLevel>(
            level, TimeSpan.FromMilliseconds(Remaining() * 60 / 100), budget,
            command.MemoryBudgetBytes, command.FullEnumeration));
        if (r.Status == SolveStatus.Solved) return FinishSolve(bundle, level, command, r, runConfigHash);
        Keep(r);

        // ② 宽束（加宽）：吃掉剩余预算的 70%
        if (Remaining() > 1500)
        {
            var wide = new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
                new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(Width: 256));
            r = wide.Solve(bundle, new SolveRequest<SlidePigLevel>(
                level, TimeSpan.FromMilliseconds(Remaining() * 70 / 100), budget,
                command.MemoryBudgetBytes, command.FullEnumeration));
            if (r.Status == SolveStatus.Solved) return FinishSolve(bundle, level, command, r, runConfigHash);
            Keep(r);
        }

        // ③ 贪心兜底：平分剩余预算跑腾挪阶梯
        var tierMs = Math.Max(400, Remaining() / 3);
        foreach (var shuffle in new[] { 8, 16, 32 })
        {
            var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
                new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(
                    MobilityTiebreak: true, ShuffleBudget: shuffle));
            r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(
                level, TimeSpan.FromMilliseconds(tierMs), budget, command.MemoryBudgetBytes, command.FullEnumeration));
            if (r.Status == SolveStatus.Solved) return FinishSolve(bundle, level, command, r, runConfigHash);
            Keep(r);
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
        GameStatus.Production,   // 2026-11-03 转正：门1全绿+门2 17/20+门3基准建立+④人工审核通过（1 关物理停格回退为已查明模型边界，规则矩阵附证据）
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

    #region 坐标制出题（ILevelComposer；脚印推导规则经 10097 只猪数据验证）

    // 出题规格：模型只描述意图（头格+类型+朝向），脚印由代码推导——实测背景：
    // 让 glm-4-flash 手写 100 只猪的 occupiedCells，2/3 候选死于脚印重叠/越界（原生 JSON 模式实测）
    public string CompositionSpec => """
        # 出题输出格式（坐标意图制，务必遵守）

        只输出一个 JSON 对象（不要输出 occupiedCells，脚印由系统按规则推导；不要任何解释）：
        {"levelId":500,"width":12,"height":19,"pigs":[
          {"id":1,"type":"Normal","facing":"Down","head":{"x":5,"y":12}},
          {"id":2,"type":"Long","facing":"Left","head":{"x":8,"y":6}},
          {"id":3,"type":"Cage","anchor":{"x":3,"y":4},"counter":5}
        ]}

        - width/height：棋盘尺寸（12×19 为标准，可 8~14 × 12~22）；坐标 x=列、y=行，原点左上；
        - pigs[].type：Normal/Boom/Spring/Long/Lock（矩形猪，head=头格）或 Cage/CageL（铁笼，anchor=包围盒左上角）；
        - facing：Up/Left/Down/Right（小象不允许 Up）；铁笼朝向固定 Up；
        - 脚印规则（系统推导，你只需给头格/锚点）：
          · Normal/Boom/Spring/Lock：2 格（头格+反向延伸 1 格）；Long：3 格（反向延伸 2 格）；
          · SmallElephant：2×3 块（Down=2宽3高，Left/Right=3宽2高）；
          · Cage：以 anchor 为左上的 5×5 整块；CageL：5×5 缺左上 3×3（保留右臂和底边）；
        - 计数（可选 counter）：Boom=碰撞爆炸次数、Lock/Cage=解锁次数（不给则用默认值）；
        - 约束：所有格必须在棋盘内；猪与猪脚印不得重叠（猪与铁笼重叠是允许的，表示被关住）；
          铁笼 5×5 必须整体在棋盘内；
        - 数量参考：难度 1 档 4~8 只、难度 2 档 8~16 只、难度 3 档 16~30 只（普通猪为主，特殊猪点缀）；
        - 布局与阻挡关系由你按上文规则与难度要求设计——先想好哪些猪先出、哪些被挡。
        """;

    // 拼装：意图清单 → 原生关卡 JSON（脚印推导、计数默认、格式合法由代码保证）
    // 兼容：模型若直接输出带 occupiedCells 的原生 pigs（照 few-shot 学的），按原生校验裁决——合法即接受
    public ComposeResult Compose(string modelOutput, string? levelNumber)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(modelOutput);
        }
        catch (JsonException ex)
        {
            return ComposeResult.Fail($"输出不是合法 JSON：{ex.Message}");
        }
        if (node is not JsonObject obj)
        {
            return ComposeResult.Fail("输出不是 JSON 对象");
        }
        // 原生格式直通：pigs 里已带 occupiedCells 就按原生校验裁决（合法即接受，非法照实报错）
        var firstPig = obj["pigs"]?.AsArray().FirstOrDefault() as JsonObject;
        if (firstPig?["occupiedCells"] is JsonArray)
        {
            var native = obj.ToJsonString();
            var nativeErrors = FormatErrors(native);
            return nativeErrors.Count == 0 ? ComposeResult.Ok(native) : ComposeResult.Fail(nativeErrors);
        }

        var errors = new List<string>();

        // 尺寸：默认 12×19（标准棋盘）
        var width = obj["width"]?.GetValue<int?>() ?? 12;
        var height = obj["height"]?.GetValue<int?>() ?? 19;
        if (width is < 8 or > 14) errors.Add($"width={width} 超出范围 8~14");
        if (height is < 12 or > 22) errors.Add($"height={height} 超出范围 12~22");
        if (errors.Count > 0)
        {
            return ComposeResult.Fail(errors);
        }

        // 逐猪推导脚印
        var pigNodes = obj["pigs"]?.AsArray() ?? new JsonArray();
        if (pigNodes.Count == 0)
        {
            errors.Add("pigs 为空——至少要设计 1 只猪");
            return ComposeResult.Fail(errors);
        }
        var pigFootprint = new HashSet<string>();     // 猪脚印（猪与猪不得重叠）
        var cageFootprint = new HashSet<string>();    // 铁笼脚印（笼与笼不得重叠；猪可入笼=被关住）
        var pigsOut = new JsonArray();
        var seenIds = new HashSet<int>();
        for (var i = 0; i < pigNodes.Count; i++)
        {
            if (pigNodes[i] is not JsonObject p)
            {
                errors.Add($"pigs[{i}] 不是对象");
                continue;
            }
            // id：模型给了就用，没给按序号补
            var id = p["id"]?.GetValue<int?>() ?? i;
            if (!seenIds.Add(id))
            {
                errors.Add($"猪 id={id} 重复");
                continue;
            }
            var type = p["type"]?.GetValue<string>() ?? "";
            var facing = p["facing"]?.GetValue<string>() ?? "Up";
            var counter = p["counter"]?.GetValue<int?>();
            var specialInfo = counter is { } c && type is "Boom" or "Lock" or "Cage" ? c.ToString() : "";

            // 铁笼：anchor → 推导 5×5 形状
            if (type is "Cage" or "CageL")
            {
                var anchor = ReadPoint(p["anchor"], $"pigs[{i}].anchor", errors);
                if (anchor is not { } a) continue;
                var cells = CageCells(a, type == "CageL");
                // 笼整块在界内
                foreach (var (x, y) in cells)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        errors.Add($"铁笼 id={id} 脚印 ({x},{y}) 越界（5×5 必须整体在棋盘内）");
                        break;
                    }
                }
                var cageDup = false;
                foreach (var (x, y) in cells)
                {
                    if (!cageFootprint.Add($"{x},{y}")) cageDup = true;
                }
                if (cageDup) errors.Add($"铁笼 id={id} 与其他铁笼重叠");
                pigsOut.Add(new JsonObject
                {
                    ["id"] = id,
                    ["facing"] = "Up",
                    ["pigType"] = type,
                    ["specialInfo"] = specialInfo,
                    ["occupiedCells"] = CellsNode(cells),
                });
                continue;
            }

            // 矩形猪：head → 按朝向轴推导脚印
            if (!SlidePigSupportedTypes.Contains(type))
            {
                errors.Add($"pigs[{i}] 类型 {type} 不在坐标制支持范围（Normal/Boom/Spring/Long/SmallElephant/Lock）");
                continue;
            }
            if (type == "SmallElephant" && facing == "Up")
            {
                errors.Add($"猪 id={id} 小象不允许朝 Up（无数据验证），请改 Down");
                continue;
            }
            var head = ReadPoint(p["head"], $"pigs[{i}].head", errors);
            if (head is not { } h) continue;
            var footprint = DeriveFootprint(type, facing, h);
            var outOfBounds = false;
            var pigDup = false;
            foreach (var (x, y) in footprint)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    errors.Add($"猪 id={id} 脚印 ({x},{y}) 越界");
                    outOfBounds = true;
                }
                if (!pigFootprint.Add($"{x},{y}")) pigDup = true;
            }
            if (outOfBounds) continue;
            if (pigDup)
            {
                errors.Add($"猪 id={id} 脚印与其他猪重叠——请调整头格位置");
                continue;
            }
            pigsOut.Add(new JsonObject
            {
                ["id"] = id,
                ["facing"] = facing,
                ["pigType"] = type,
                ["specialInfo"] = specialInfo,
                ["occupiedCells"] = CellsNode(footprint),
            });
        }
        if (errors.Count > 0)
        {
            return ComposeResult.Fail(errors);
        }

        // 组装原生关卡 JSON（levelId 来自请求参数；pigCount=pigs.length）
        var levelId = int.TryParse(levelNumber, out var n) ? n : 1;
        var native2 = new JsonObject
        {
            ["levelId"] = levelId,
            ["levelName"] = $"gen-{levelId}",
            ["mapWidth"] = width,
            ["mapHeight"] = height,
            ["pigCount"] = pigsOut.Count,
            ["pigs"] = pigsOut,
        };
        var composed = native2.ToJsonString();
        var finalErrors = FormatErrors(composed);
        return finalErrors.Count == 0 ? ComposeResult.Ok(composed) : ComposeResult.Fail(finalErrors);
    }

    // few-shot 原生关卡 → 坐标意图（与 Compose 的输出格式一致，否则模型会照示例写原生格式）
    // 含 CageL（不规则形状）的示例无法无损表达 → 返回 null 跳过该示例
    public string? DescribeAsCoordinates(string levelJson)
    {
        (SlidePigLevel? Level, IReadOnlyList<string> Errors) parsed;
        try
        {
            parsed = SlidePigValidator.ValidateJson(levelJson);
        }
        catch (Exception)
        {
            return null;
        }
        if (parsed.Level is null || parsed.Errors.Count > 0)
        {
            return null;
        }
        var level = parsed.Level;
        var pigs = new JsonArray();
        foreach (var p in level.Pigs)
        {
            // 非矩形猪（CageL）无法用 head/anchor 无损表达：跳过整个示例（保守但保证格式一致）
            var bboxW = p.OccupiedCells.Max(c => c.X) - p.OccupiedCells.Min(c => c.X) + 1;
            var bboxH = p.OccupiedCells.Max(c => c.Y) - p.OccupiedCells.Min(c => c.Y) + 1;
            if (p.PigType == "CageL") return null;
            var pigObj = new JsonObject { ["id"] = p.Id, ["type"] = p.PigType, ["facing"] = p.Facing };
            if (p.PigType == "Cage")
            {
                // 笼：anchor = 5×5 包围盒左上（真实数据 100% 为 5×5 整块）
                pigObj["anchor"] = new JsonObject { ["x"] = p.OccupiedCells.Min(c => c.X), ["y"] = p.OccupiedCells.Min(c => c.Y) };
                if (!string.IsNullOrEmpty(p.SpecialInfo)) pigObj["counter"] = int.Parse(p.SpecialInfo.Trim());
            }
            else
            {
                // 头格 = 沿朝向最前的那格（脚印集中沿朝向轴的极值格）
                var head = HeadCell(p.OccupiedCells, p.Facing);
                pigObj["head"] = new JsonObject { ["x"] = head.X, ["y"] = head.Y };
                if (!string.IsNullOrEmpty(p.SpecialInfo)) pigObj["counter"] = int.Parse(p.SpecialInfo.Trim());
            }
            pigs.Add(pigObj);
            _ = (bboxW, bboxH);
        }
        return new JsonObject
        {
            ["levelId"] = level.LevelId,
            ["width"] = level.MapWidth,
            ["height"] = level.MapHeight,
            ["pigs"] = pigs,
        }.ToJsonString();
    }

    #endregion

    #region 内部工具

    // 坐标制支持的矩形类型
    private static readonly HashSet<string> SlidePigSupportedTypes =
        ["Normal", "Boom", "Spring", "Long", "SmallElephant", "Lock"];

    // 从意图 JSON 读 {x,y} 点
    private static (int X, int Y)? ReadPoint(JsonNode? node, string name, List<string> errors)
    {
        if (node is not JsonObject o || o["x"] is null || o["y"] is null)
        {
            errors.Add($"缺少 {name}（{{x,y}}）");
            return null;
        }
        return (o["x"]!.GetValue<int>(), o["y"]!.GetValue<int>());
    }

    // 头格推导：朝向轴上最"前"的格（Down/Right 取最大坐标，Up/Left 取最小）
    private static (int X, int Y) HeadCell(IReadOnlyList<(int X, int Y)> cells, string facing)
    {
        var vertical = facing is "Up" or "Down";
        var takeMax = facing is "Down" or "Right";
        var best = cells[0];
        foreach (var c in cells)
        {
            var v = vertical ? c.Y : c.X;
            var b = vertical ? best.Y : best.X;
            if (takeMax ? v > b : v < b) best = c;
        }
        return best;
    }

    // 脚印推导：头格沿朝向轴，向反向延伸（100% 数据验证：9359 矩形猪全匹配）
    private static List<(int X, int Y)> DeriveFootprint(string type, string facing, (int X, int Y) head)
    {
        var (dx, dy) = facing switch
        {
            "Up" => (0, -1),
            "Down" => (0, 1),
            "Left" => (-1, 0),
            _ => (1, 0)
        };
        var length = type == "Long" ? 3 : 2;
        var cells = new List<(int X, int Y)>(length);
        // 头在前、身体向反向延伸：脚印集合 = 头 + 反向 × i
        if (type == "SmallElephant")
        {
            // 2×3 块：Down=2宽3高；Left/Right=3宽2高（Up 已在规格中禁止）
            var w = facing is "Left" or "Right" ? 3 : 2;
            var h = facing is "Left" or "Right" ? 2 : 3;
            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    cells.Add((head.X + x, head.Y + y));
                }
            }
            return cells;
        }
        for (var i = 0; i < length; i++)
        {
            cells.Add((head.X - dx * i, head.Y - dy * i));
        }
        return cells;
    }

    // 铁笼脚印：anchor 为 5×5 左上；CageL 缺左上 3×3（保留右臂和底边；数据验证 6/6）
    private static List<(int X, int Y)> CageCells((int X, int Y) anchor, bool lShape)
    {
        var cells = new List<(int X, int Y)>(lShape ? 16 : 25);
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
            {
                if (lShape && x < 3 && y < 3) continue;   // 缺左上 3×3
                cells.Add((anchor.X + x, anchor.Y + y));
            }
        }
        return cells;
    }

    // 脚印 → occupiedCells JSON
    private static JsonArray CellsNode(IEnumerable<(int X, int Y)> cells)
    {
        var arr = new JsonArray();
        foreach (var (x, y) in cells)
        {
            arr.Add(new JsonObject { ["x"] = x, ["y"] = y });
        }
        return arr;
    }

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
