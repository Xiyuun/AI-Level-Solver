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
public sealed class SokobanToyFacade : IGameFacade, ITokenBudgetHint, ILevelComposer
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

    // 试玩步进：从初态顺序执行结构化动作（与求解执行同一份适配层——架构承诺，渲染规范 §6）
    // 无状态设计：每次请求带完整动作序列重放，服务端不存会话；非法动作/关卡抛 PlayException
    public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
    {
        SokobanLevel? level;
        try
        {
            level = JsonSerializer.Deserialize<SokobanLevel>(levelJson, TapeJson.Options);
        }
        catch (JsonException)
        {
            level = null;
        }
        if (level is null || level.Id is null || level.Grid is null)
        {
            throw new PlayException("关卡 JSON 非法：缺少合法的 id/grid 字段");
        }
        SokobanState state;
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
            SokobanMove move;
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

    #region 生成端能力（坐标制出题 + token 预算）

    // 出题规格：模型只描述"设计意图"，不写 grid 字符串（矩形与边界墙交给代码）
    // 实测背景：让 glm-4-flash 数出等长字符串数组，88% 候选死在格式校验（前后端全流程优化 §1.1）
    public string CompositionSpec => """
        # 出题输出格式（坐标制，务必遵守）

        只输出一个 JSON 对象，结构如下（不要输出 grid 字符串，不要任何解释）：
        {"width":7,"height":5,"player":{"x":1,"y":1},"boxes":[{"x":3,"y":1}],"goals":[{"x":5,"y":1}]}

        - width / height：棋盘总尺寸（含四周墙），取值范围 4~12；
        - 坐标 x = 列、y = 行，原点在左上角；
        - 可放实体的范围是 x∈[1,width-2]、y∈[1,height-2]——最外圈由系统自动补墙，你不要填；
        - player 恰好 1 个；boxes 与 goals 数量相等，且各至少 1 个；
        - 同一个格子不能既放玩家又放箱子；目标格上可以放箱子；
        - 布局与难度由你按上文规则与难度要求决定。
        """;

    // token 预算：坐标制输出仅数十 token，2048 已含修复余量
    public int EstimateLevelTokens(string? levelNumber) => 2048;

    // 拼装：坐标清单 → 原生 grid（矩形、边界墙、字符集由代码保证，不再依赖模型数格子）
    // 失败时错误清单逐条可回喂——这些是代码能确定性发现的错误，不是猜测
    // 兼容：模型若直接给出合法原生 grid（照 few-shot 学的），原样接受——不改写、不"修复"
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
        // 原生格式直通：已带 grid 就按原生校验裁决（合法即接受，非法照实报错）
        if (obj["grid"] is JsonArray)
        {
            var native = obj.ToJsonString();
            var nativeErrors = FormatErrors(native);
            return nativeErrors.Count == 0 ? ComposeResult.Ok(native) : ComposeResult.Fail(nativeErrors);
        }
        var errors = new List<string>();

        // 尺寸：含四周墙的外框，范围收紧以保证可玩性
        var width = ReadInt(obj, "width", errors);
        var height = ReadInt(obj, "height", errors);
        if (width is { } w && w is < 4 or > 12) errors.Add($"width={w} 超出范围 4~12");
        if (height is { } h && h is < 4 or > 12) errors.Add($"height={h} 超出范围 4~12");
        if (errors.Count > 0 || width is not { } W || height is not { } H)
        {
            return ComposeResult.Fail(errors);
        }

        // 实体坐标：玩家必填，箱子/目标数量相等
        var player = ReadPoint(obj["player"], "player", errors);
        var boxes = ReadPoints(obj["boxes"], "boxes", errors);
        var goals = ReadPoints(obj["goals"], "goals", errors);
        if (boxes.Count == 0) errors.Add("boxes 至少要有 1 个");
        if (goals.Count == 0) errors.Add("goals 至少要有 1 个");
        if (boxes.Count != goals.Count)
        {
            errors.Add($"箱子数 {boxes.Count} 与目标数 {goals.Count} 不一致（标准胜利条件要求相等）");
        }
        // 越界与重叠检查：最外圈留给墙
        foreach (var (pts, name) in new[] { (boxes, "boxes"), (goals, "goals") })
        {
            foreach (var p in pts)
            {
                if (p.X < 1 || p.X > W - 2 || p.Y < 1 || p.Y > H - 2)
                {
                    errors.Add($"{name} 坐标 ({p.X},{p.Y}) 越界：可用范围 x∈[1,{W - 2}]、y∈[1,{H - 2}]");
                }
            }
        }
        if (player is { } pp)
        {
            if (pp.X < 1 || pp.X > W - 2 || pp.Y < 1 || pp.Y > H - 2)
            {
                errors.Add($"player 坐标 ({pp.X},{pp.Y}) 越界：可用范围 x∈[1,{W - 2}]、y∈[1,{H - 2}]");
            }
        }
        else
        {
            errors.Add("缺少 player（恰好 1 个 {x,y}）");
        }
        // 同一格重复放置
        var occupied = new Dictionary<(int, int), string>();
        foreach (var (pts, name) in new[] { (boxes, "箱子"), (goals, "目标") })
        {
            foreach (var p in pts)
            {
                if (occupied.TryGetValue((p.X, p.Y), out var prior) && prior != name)
                {
                    errors.Add($"({p.X},{p.Y}) 同时放了{prior}和{name}，同一格只能放一种");
                }
                occupied[(p.X, p.Y)] = name;
            }
        }
        if (player is { } pTest && boxes.Any(b => b.X == pTest.X && b.Y == pTest.Y))
        {
            errors.Add($"玩家起点 ({pTest.X},{pTest.Y}) 与箱子重合");
        }
        if (errors.Count > 0)
        {
            return ComposeResult.Fail(errors);
        }

        // 拼装 grid：外圈墙 + 内部地板，再落目标 → 箱子 → 玩家（箱子/玩家压在目标上时升级为 * / +）
        var goalSet = goals.Select(g => (g.X, g.Y)).ToHashSet();
        var boxSet = boxes.Select(b => (b.X, b.Y)).ToHashSet();
        var playerPoint = player!.Value;   // 错误清单为空 → player 必非空（上方已判）
        var rows = new List<string>(H);
        for (var y = 0; y < H; y++)
        {
            var chars = new char[W];
            for (var x = 0; x < W; x++)
            {
                var border = x == 0 || y == 0 || x == W - 1 || y == H - 1;
                var onGoal = goalSet.Contains((x, y));
                var onBox = boxSet.Contains((x, y));
                var onPlayer = playerPoint.X == x && playerPoint.Y == y;
                chars[x] = border ? '#'
                    : onBox && onGoal ? '*'
                    : onBox ? '$'
                    : onPlayer ? (onGoal ? '+' : '@')
                    : onGoal ? '.'
                    : ' ';
            }
            rows.Add(new string(chars));
        }
        var level = new JsonObject
        {
            ["id"] = ComposeLevelId(levelNumber),
            ["grid"] = new JsonArray(rows.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray())
        };
        return ComposeResult.Ok(level.ToJsonString());
    }

    // few-shot 示例转坐标制：扫描原生 grid 取出尺寸与实体坐标
    // 示例必须与要求的输出格式一致——否则模型照着原生示例写 grid，坐标制等于白做
    public string? DescribeAsCoordinates(string levelJson)
    {
        var (level, errors) = SokobanToyValidator.ValidateJson(levelJson);
        if (level is null || errors.Count > 0)
        {
            return null;
        }
        var grid = level.Grid;
        var height = grid.Length;
        var width = grid[0].Length;
        var boxes = new JsonArray();
        var goals = new JsonArray();
        int? px = null, py = null;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                switch (grid[y][x])
                {
                    case '@':
                        px = x; py = y;
                        break;
                    case '+':
                        px = x; py = y;
                        goals.Add(PointNode(x, y));
                        break;
                    case '$':
                        boxes.Add(PointNode(x, y));
                        break;
                    case '*':
                        boxes.Add(PointNode(x, y));
                        goals.Add(PointNode(x, y));
                        break;
                    case '.':
                        goals.Add(PointNode(x, y));
                        break;
                }
            }
        }
        if (px is null || py is null)
        {
            return null;
        }
        var coord = new JsonObject
        {
            ["width"] = width,
            ["height"] = height,
            ["player"] = new JsonObject { ["x"] = px.Value, ["y"] = py.Value },
            ["boxes"] = boxes,
            ["goals"] = goals
        };
        return coord.ToJsonString();
    }

    // 坐标点节点（x=列、y=行）
    private static JsonObject PointNode(int x, int y)
    {
        return new JsonObject { ["x"] = x, ["y"] = y };
    }

    // 关卡编号：数字按内置命名习惯补零（11 → toy-011），非数字原样，缺省 generated
    private static string ComposeLevelId(string? levelNumber)
    {
        if (!string.IsNullOrWhiteSpace(levelNumber))
        {
            return int.TryParse(levelNumber.Trim(), out var n) ? $"toy-{n:000}" : levelNumber.Trim();
        }
        return "toy-generated";
    }

    // 读整数字段：缺失/非数字记错误并返回 null
    private static int? ReadInt(JsonObject obj, string name, List<string> errors)
    {
        var node = obj[name];
        if (node is null)
        {
            errors.Add($"缺少字段 {name}");
            return null;
        }
        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            errors.Add($"字段 {name} 不是整数");
            return null;
        }
    }

    // 读单个 {x,y}
    private static (int X, int Y)? ReadPoint(JsonNode? node, string name, List<string> errors)
    {
        if (node is not JsonObject o)
        {
            errors.Add($"{name} 必须是形如 {{\"x\":1,\"y\":1}} 的对象");
            return null;
        }
        var x = ReadInt(o, "x", errors);
        var y = ReadInt(o, "y", errors);
        return x is { } vx && y is { } vy ? (vx, vy) : null;
    }

    // 读坐标数组；缺失按空数组处理（数量检查会报错）
    private static List<(int X, int Y)> ReadPoints(JsonNode? node, string name, List<string> errors)
    {
        var points = new List<(int X, int Y)>();
        if (node is null)
        {
            return points;
        }
        if (node is not JsonArray array)
        {
            errors.Add($"{name} 必须是数组");
            return points;
        }
        for (var i = 0; i < array.Count; i++)
        {
            if (ReadPoint(array[i], $"{name}[{i}]", errors) is { } p)
            {
                points.Add(p);
            }
        }
        return points;
    }

    #endregion

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
