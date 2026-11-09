using System.Text;
using System.Text.Json.Nodes;
using LevelForge.Adapters.PiggyFarm;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm.Tests;

// piggyfarm v1 适配层测试：格式校验 / 滑行规则 / 状态键 / 纯函数 / 门1 / 求解 / 试玩 / 真实语料
public sealed class PiggyFarmAdapterTests
{
    private static readonly PiggyFarmAdapter Adapter = new();

    // 合成关卡：pigs 是 JSON 数组元素原文（逗号分隔的猪对象；map 默认 5×5）
    private static string Level(string pigsJson, int width = 5, int height = 5)
    {
        var count = JsonNode.Parse("[" + pigsJson + "]")!.AsArray().Count;
        return $$"""
        {"levelId":900,"difficulty":1,"levelName":"test","mapWidth":{{width}},"mapHeight":{{height}},"pigCount":{{count}},"pigs":[{{pigsJson}}],"terrainElements":[]}
        """;
    }

    private static string Pig(int id, string facing, string type, params (int X, int Y)[] cells)
    {
        var cellsJson = string.Join(",", cells.Select(c => $"{{\"x\":{c.X},\"y\":{c.Y}}}"));
        return $$"""{"id":{{id}},"facing":"{{facing}}","pigType":"{{type}}","specialInfo":"","occupiedCells":[{{cellsJson}}]}""";
    }

    #region 解析与校验

    [Fact]
    public void 解析_官方样例关通过()
    {
        Assert.Empty(PiggyFarmLevels.Validate(PiggyFarmContent.SampleLevelJson));
        var level = PiggyFarmLevels.Load(PiggyFarmContent.SampleLevelJson);
        Assert.Equal(1, level.LevelId);
        Assert.Equal(6, level.Pigs.Count);
        Assert.All(level.Pigs, p => Assert.Equal("Normal", p.Type));
    }

    [Fact]
    public void 校验_Boom已纳入支持_其它特殊猪仍按跳过口径()
    {
        Assert.Empty(PiggyFarmLevels.Validate(Level(Pig(0, "Left", "Boom", (2, 2), (3, 2)))));
        var errors = PiggyFarmLevels.Validate(Level(Pig(0, "Left", "Zombie", (2, 2), (3, 2))));
        Assert.Contains(errors, e => e.Contains("v1 支持子集之外") && e.Contains("Zombie"));
    }

    [Fact]
    public void 校验_越界与重叠被拒绝()
    {
        var outOfBounds = PiggyFarmLevels.Validate(Level(Pig(0, "Left", "Normal", (9, 2), (10, 2))));
        Assert.Contains(outOfBounds, e => e.Contains("越界"));

        var overlap = PiggyFarmLevels.Validate(Level(
            Pig(0, "Left", "Normal", (1, 1), (2, 1)) + "," + Pig(1, "Left", "Normal", (2, 1), (3, 1))));
        Assert.Contains(overlap, e => e.Contains("占用同一格"));
    }

    [Fact]
    public void 校验_pigCount与脚印尺寸不匹配被拒绝()
    {
        var mismatchCount = PiggyFarmLevels.Validate("""
        {"levelId":1,"difficulty":1,"levelName":"t","mapWidth":5,"mapHeight":5,"pigCount":2,
         "pigs":[{"id":0,"facing":"Left","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":1,"y":1},{"x":2,"y":1}]}],
         "terrainElements":[]}
        """);
        Assert.Contains(mismatchCount, e => e.Contains("pigCount"));

        // Normal 朝左应为 2×1，这里给 1×2
        var badShape = PiggyFarmLevels.Validate(Level(Pig(0, "Left", "Normal", (1, 1), (1, 2))));
        Assert.Contains(badShape, e => e.Contains("脚印"));
    }

    [Fact]
    public void 校验_坏JSON给出可读错误()
    {
        var errors = PiggyFarmLevels.Validate("{not json");
        Assert.Single(errors);
        Assert.Contains("解析失败", errors[0]);
    }

    #endregion

    #region 滑行规则

    [Fact]
    public void 滑行_被挡停在前一格()
    {
        var level = PiggyFarmLevels.Load(Level(
            Pig(0, "Right", "Normal", (0, 2), (1, 2)) + "," + Pig(1, "Left", "Normal", (3, 2), (4, 2))));
        var state = Adapter.LoadLevel(level);
        var moved = Adapter.ApplyMove(state, new PiggyFarmMove(0));
        var pig = moved.Pigs.Single(p => p.Id == 0);
        Assert.Equal(new[] { new GridCell(1, 2), new GridCell(2, 2) }, pig.Cells.ToArray());
        Assert.Equal(2, moved.Pigs.Count);
    }

    [Fact]
    public void 滑行_越界离场并判定胜利()
    {
        var level = PiggyFarmLevels.Load(Level(Pig(0, "Up", "Normal", (2, 1), (2, 2))));
        var state = Adapter.LoadLevel(level);
        Assert.Single(Adapter.GetLegalMoves(state));
        var moved = Adapter.ApplyMove(state, new PiggyFarmMove(0));
        Assert.Empty(moved.Pigs);
        Assert.True(Adapter.IsGoal(moved));
    }

    [Fact]
    public void 起步_贴脸互相阻挡只剩原地顶撞()
    {
        var level = PiggyFarmLevels.Load(Level(
            Pig(0, "Right", "Normal", (0, 2), (1, 2)) + "," + Pig(1, "Left", "Normal", (2, 2), (3, 2))));
        var state = Adapter.LoadLevel(level);
        // 游戏语义：贴脸互相阻挡的猪仍可点击 = 原地顶撞（起步即碰撞，无位移，事件照常触发）
        var moves = Adapter.GetLegalMoves(state);
        Assert.Equal(new[] { new PiggyFarmMove(0), new PiggyFarmMove(1) }, moves);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(0));
        Assert.Equal(Adapter.StateKey(state), Adapter.StateKey(next));   // 无位移：局面不变
    }

    [Fact]
    public void 贴脸死局_完整搜索可证无解()
    {
        var json = Level(Pig(0, "Right", "Normal", (0, 2), (1, 2)) + "," + Pig(1, "Left", "Normal", (2, 2), (3, 2)));
        var outcome = new PiggyFarmFacade().Solve(new SolveCommand(
            json, Encoding.UTF8.GetBytes(json), "deadlock", TimeSpan.FromSeconds(5)));
        Assert.Equal(SolveStatus.UnsolvableProven, outcome.Result.Status);
        Assert.True(outcome.Result.GraphComplete);
        Assert.Equal(TerminationReason.FrontierEmpty, outcome.Result.TerminationReason);
        Assert.Null(outcome.Tape);
    }

    #endregion

    #region 状态键与纯函数

    [Fact]
    public void 状态键_猪顺序无关且区分朝向()
    {
        var level = PiggyFarmLevels.Load(PiggyFarmContent.SampleLevelJson);
        var state = Adapter.LoadLevel(level);
        var reversed = state with { Pigs = state.Pigs.Reverse().ToArray() };
        Assert.Equal(Adapter.StateKey(state), Adapter.StateKey(reversed));

        var first = state.Pigs[0];
        var flipped = state with
        {
            Pigs = state.Pigs.Select(p => p.Id == first.Id
                ? p with { Facing = p.Facing == PigFacing.Up ? PigFacing.Down : PigFacing.Up }
                : p).ToArray()
        };
        Assert.NotEqual(Adapter.StateKey(state), Adapter.StateKey(flipped));

        var shifted = state with
        {
            Pigs = state.Pigs.Select(p => p.Id == first.Id
                ? p with { Cells = p.Cells.Select(c => new GridCell(c.X + 1, c.Y)).ToArray() }
                : p).ToArray()
        };
        Assert.NotEqual(Adapter.StateKey(state), Adapter.StateKey(shifted));
    }

    [Fact]
    public void 纯函数_重复应用不改入参且结果一致()
    {
        var level = PiggyFarmLevels.Load(PiggyFarmContent.SampleLevelJson);
        var state = Adapter.LoadLevel(level);
        var move = Adapter.GetLegalMoves(state)[0];
        var beforeKey = Adapter.StateKey(state);
        var beforeSemantic = Adapter.ProjectSemanticState(state).ToJsonString();

        var first = Adapter.ApplyMove(state, move);
        var view = Adapter.StateKey(first) + "|" + Adapter.ProjectSemanticState(first).ToJsonString();
        for (var i = 0; i < 3; i++)
        {
            var again = Adapter.ApplyMove(state, move);
            Assert.Equal(view, Adapter.StateKey(again) + "|" + Adapter.ProjectSemanticState(again).ToJsonString());
        }
        Assert.Equal(beforeKey, Adapter.StateKey(state));
        Assert.Equal(beforeSemantic, Adapter.ProjectSemanticState(state).ToJsonString());
    }

    #endregion

    #region 门1 / 求解 / 试玩

    [Fact]
    public void 门1_五项合规检查全绿()
    {
        var report = new PiggyFarmFacade().RunGate1Checks();
        var detail = string.Join(" / ", new[]
        {
            report.DependencyScan, report.StateKeyDiscrimination, report.Purity,
            report.Determinism, report.KernelSelfTest
        }.Select(r => $"{r.Kind}:{(r.Passed ? "pass" : "FAIL[" + string.Join(";", r.Issues) + "]")}"));
        Assert.True(report.AllPassed, detail);
    }

    [Fact]
    public void 求解_样例关有解_磁带可回放且最优()
    {
        var outcome = new PiggyFarmFacade().Solve(new SolveCommand(
            PiggyFarmContent.SampleLevelJson,
            Encoding.UTF8.GetBytes(PiggyFarmContent.SampleLevelJson),
            "1", TimeSpan.FromSeconds(15)));

        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.Equal(6, outcome.Result.Solution.Count);
        // 每只猪至少要点击一次才能离场：6 次点击 = 动作数下界，故最优性可证
        Assert.Equal(Optimality.UnitCostShortest, outcome.Result.Optimality);
        Assert.NotNull(outcome.Tape);
        Assert.Equal(ExecutionOutcome.Win, outcome.Tape!.Outcome);
        Assert.Equal(6, outcome.Tape.Steps.Count);
        Assert.Equal(1, outcome.Difficulty.Band);
    }

    [Fact]
    public void 求解_真实关卡000004可解()
    {
        var file = Path.Combine(PiggyFarmConstants.ProjectRoot,
            "Assets", "Main", "LevelEditor", "Luban~", "Datas", "Levels", "000004.json");
        if (!File.Exists(file))
        {
            return;   // 语料不在本机时不阻塞测试（真实语料覆盖测试单独做）
        }
        var json = File.ReadAllText(file);
        Assert.Empty(PiggyFarmLevels.Validate(json));
        var outcome = new PiggyFarmFacade().Solve(new SolveCommand(
            json, Encoding.UTF8.GetBytes(json), "4", TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.NotNull(outcome.Tape);
        Assert.Equal(ExecutionOutcome.Win, outcome.Tape!.Outcome);
        Assert.True(outcome.Result.Solution.Count >= 88);
    }

    [Fact]
    public void 试玩_非法动作抛PlayException()
    {
        // 用不存在的猪号 99 触发非法动作
        var badMove = new JsonObject { ["pigId"] = 99 };
        var ex = Assert.Throws<PlayException>(() =>
            new PiggyFarmFacade().Play(PiggyFarmContent.SampleLevelJson, [badMove]));
        Assert.Contains("不合法", ex.Message);

        // 合法动作序列可以执行并返回局面（与求解共用同一份适配层）
        var state = Adapter.LoadLevel(PiggyFarmLevels.Load(PiggyFarmContent.SampleLevelJson));
        var legal = Adapter.GetLegalMoves(state)[0];
        var play = new PiggyFarmFacade().Play(PiggyFarmContent.SampleLevelJson, [Adapter.EncodeMove(legal)]);
        Assert.Equal(1, play.MovesExecuted);
    }

    #endregion

    #region 真实语料（项目内 499 个编辑源）

    [Fact]
    public void 语料_子集外关卡全部按跳过口径给出原因()
    {
        var dir = Path.Combine(PiggyFarmConstants.ProjectRoot,
            "Assets", "Main", "LevelEditor", "Luban~", "Datas", "Levels");
        if (!Directory.Exists(dir))
        {
            return;
        }
        var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToArray();
        Assert.Equal(499, files.Length);
        var facade = new PiggyFarmFacade();
        var accepted = 0;
        var unsupported = 0;
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var errors = facade.FormatErrors(json);
            if (errors.Count == 0)
            {
                accepted++;
                continue;
            }
            unsupported++;
            Assert.Contains(errors, e => e.Contains("v1 支持子集之外"));
        }
        // v1.1 子集：Normal/Long/RichPig/Boom/Duck、无升降梯、无动态兔子 —— 本语料实测 163 关
        Assert.Equal(163, accepted);
        Assert.Equal(336, unsupported);
    }

    #endregion
}

// 项目根路径：本机语料位置（测试目录不在游戏项目内，单独常量便于迁移）
public static class PiggyFarmConstants
{
    public static readonly string ProjectRoot =
        Environment.GetEnvironmentVariable("PIGGYFARM_PROJECT_ROOT") ?? @"D:\temp\PiggyFarm\piggyfarm";
}

// 生成质量门（只作用于生成候选）：格式合法但"全部同朝向/独立直道"的图必须被拒
public sealed class PiggyFarmQualityGateTests
{
    [Fact]
    public void 质量门_官方样例关通过()
    {
        Assert.Empty(new PiggyFarmFacade().QualityErrors(PiggyFarmContent.SampleLevelJson));
    }

    [Fact]
    public void 质量门_拒绝全部同朝向的规则直道()
    {
        // 6 行 × 4 列、全部朝右的等距直道：格式合法，但没有设计
        var pigs = new List<string>();
        for (var row = 0; row < 6; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                var x = col * 3;
                var y = 1 + row * 2;
                pigs.Add($$"""{"id":{{row * 4 + col}},"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":{{x}},"y":{{y}}},{"x":{{x + 1}},"y":{{y}}}]}""");
            }
        }
        var json = $$"""{"levelId":901,"difficulty":2,"levelName":"grid","mapWidth":12,"mapHeight":19,"pigCount":{{pigs.Count}},"pigs":[{{string.Join(",", pigs)}}],"terrainElements":[]}""";

        var facade = new PiggyFarmFacade();
        Assert.Empty(facade.FormatErrors(json));                 // 格式合法：质量门才是拦截点
        var errors = facade.QualityErrors(json);
        Assert.Contains(errors, e => e.Contains("朝向过少"));
    }
}

// 结构化出题器（模板）：模型只选档位，几何由代码拼装；输出必须格式合法、质量门通过、可解且命中档位
public sealed class PiggyFarmComposerTests
{
    private static (PiggyFarmFacade Facade, string Json) Compose(int band)
    {
        var facade = new PiggyFarmFacade();
        var result = facade.Compose($$"""{"band":{{band}}}""", "501");
        Assert.NotNull(result.LevelJson);
        Assert.Empty(result.Errors);
        return (facade, result.LevelJson!);
    }

    [Fact]
    public void 模板出题_2档为原生2档模板()
    {
        var (facade, json) = Compose(2);
        Assert.Empty(facade.FormatErrors(json));
        Assert.Empty(facade.QualityErrors(json));
        // 无类型需求 = 自由生成：模板池全部真实关卡（Level374/DuckB2/424B2/024B2/014B2/194B2）皆可
        var level = PiggyFarmLevels.Load(json);
        Assert.InRange(level.Pigs.Count, 70, 99);

        var outcome = facade.Solve(new SolveCommand(json, Encoding.UTF8.GetBytes(json), "compose-2",
            TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.Equal(2, outcome.Difficulty.Band);
        Assert.NotNull(outcome.Tape);
    }

    [Fact]
    public void 模板出题_3档为原生3档模板()
    {
        var (facade, json) = Compose(3);
        Assert.Empty(facade.FormatErrors(json));
        Assert.Empty(facade.QualityErrors(json));
        // band3 池：Level19/29/59/DuckB3/469B3（≤96）+ Dense112（>96 兜底）；无需求时取 ≤96 的
        var level = PiggyFarmLevels.Load(json);
        Assert.InRange(level.Pigs.Count, 10, 112);

        var outcome = facade.Solve(new SolveCommand(json, Encoding.UTF8.GetBytes(json), "compose-3",
            TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.Equal(3, outcome.Difficulty.Band);
        Assert.NotNull(outcome.Tape);
    }

    [Fact]
    public void 模板出题_非JSON或非法档位返回错误清单()
    {
        var facade = new PiggyFarmFacade();
        Assert.NotEmpty(facade.Compose("我建议做一道好玩的关卡", "501").Errors);
        Assert.NotEmpty(facade.Compose("""{"band":9}""", "501").Errors);
    }
}

// 生成上下文：换种子/换提示词必须得到不同布局；特殊要求（猪数）要生效
public sealed class PiggyFarmComposerVariationTests
{
    [Fact]
    public void 不同种子生成不同布局()
    {
        var facade = new PiggyFarmFacade();
        var a = facade.Compose("""{"band":2}""", "601", new ComposeContext("601", 2, null, 11));
        var b = facade.Compose("""{"band":2}""", "602", new ComposeContext("602", 2, null, 22));
        Assert.NotNull(a.LevelJson);
        Assert.NotNull(b.LevelJson);
        Assert.NotEqual(a.LevelJson, b.LevelJson);
        Assert.Empty(facade.QualityErrors(a.LevelJson!));
        Assert.Empty(facade.QualityErrors(b.LevelJson!));
    }

    [Fact]
    public void 特殊要求猪数生效_不足时给出可操作错误()
    {
        var facade = new PiggyFarmFacade();
        var ok = facade.Compose("""{"band":3}""", "603", new ComposeContext("603", 3, "至少包含30只猪", 7));
        Assert.NotNull(ok.LevelJson);
        Assert.True(PiggyFarmLevels.Load(ok.LevelJson!).Pigs.Count >= 30);

        var tooMany = facade.Compose("""{"band":2}""", "604", new ComposeContext("604", 2, "至少包含100只猪", 7));
        Assert.Null(tooMany.LevelJson);
        Assert.NotEmpty(tooMany.Errors);
    }
}

// 生成前参数可行性：不可满足的需求必须在调 LLM 前失败，而不是当成格式错误重试 7 次
public sealed class PiggyFarmComposerPreflightTests
{
    [Fact]
    public void 预检_2档至少48猪通过_至少78猪拒绝()
    {
        var facade = new PiggyFarmFacade();
        Assert.Empty(facade.ValidateContext(new ComposeContext("801", 2, "至少包含48只猪", 1)));
        var errors = facade.ValidateContext(new ComposeContext("802", 2, "至少包含100只猪", 1));
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("97"));
    }

    [Fact]
    public void 预检_3档至少30猪通过()
    {
        Assert.Empty(new PiggyFarmFacade().ValidateContext(new ComposeContext("802", 3, "至少包含30只猪", 1)));
    }

    [Fact]
    public void 组合_模型只输出空对象时按目标档生成()
    {
        var json = new PiggyFarmFacade()
            .Compose("{}", "803", new ComposeContext("803", 2, null, 5)).LevelJson;
        Assert.NotNull(json);
        Assert.Equal(2, PiggyFarmLevels.Load(json!).Difficulty);
    }

    [Fact]
    public void 组合_模型提示档位与目标不一致时仍按目标档()
    {
        var json = new PiggyFarmFacade()
            .Compose("""{"band":3}""", "804", new ComposeContext("804", 2, null, 5)).LevelJson;
        Assert.NotNull(json);
        Assert.Equal(2, PiggyFarmLevels.Load(json!).Difficulty);
    }

    [Fact]
    public void 组合_不可满足需求返回Constraint而不是Format()
    {
        var result = new PiggyFarmFacade()
            .Compose("{}", "805", new ComposeContext("805", 2, "至少包含100只猪", 5));
        Assert.Null(result.LevelJson);
        Assert.Equal(ComposeFailureKind.Constraint, result.Kind);
    }
}

// 需求解析与类型纯度：类型词不以"猪"结尾也要识别（如"30只小黄鸭"）；
// 未要求的特殊猪不得"顺带"出现在输出——模板自带的土豪猪/大长猪一律移除（需求即契约）
public sealed class PiggyFarmComposerRequirementPurityTests
{
    private static readonly PiggyFarmFacade Facade = new();

    [Fact]
    public void 需求_不结尾猪字的类型被解析_小黄鸭按池容量给可操作错误()
    {
        // 旧正则只认"N只…猪"："30只小黄鸭"整体匹配失败，鸭子需求被静默丢弃（既不产出也不报错）。
        // 现在小黄鸭已建模：解析成功 → 按池容量给可操作错误（2 档鸭子模板最多 10 只）
        var errors = Facade.ValidateContext(new ComposeContext("500", 2, "至少包含70只猪，其中包含30只小黄鸭", 1));
        Assert.Contains(errors, e => e.Contains("小黄鸭") && e.Contains("最多支持 10 只"));
    }

    [Fact]
    public void 需求_不支持类型仍如实拒绝_僵尸与兔子()
    {
        var errors = Facade.ValidateContext(new ComposeContext("500", 2, "至少包含3只僵尸", 1));
        Assert.Contains(errors, e => e.Contains("丧尸猪") || e.Contains("Zombie"));
        var errors2 = Facade.ValidateContext(new ComposeContext("500", 3, "加入2只兔子", 1));
        Assert.Contains(errors2, e => e.Contains("兔子") || e.Contains("Rabbit"));
    }

    [Fact]
    public void 需求_多个不支持类型逐条给原因_弹簧猪与兔子()
    {
        var errors = Facade.ValidateContext(new ComposeContext("500", 3, "加入3只弹簧猪和2只兔子", 1));
        Assert.Contains(errors, e => e.Contains("弹簧"));
        Assert.Contains(errors, e => e.Contains("兔"));
    }

    [Fact]
    public void 需求_土豪猪别名按要求识别()
    {
        Assert.Empty(Facade.ValidateContext(new ComposeContext(null, 2, "至少包含2只土豪猪", 1)));
    }

    [Fact]
    public void 类型纯度_要求土豪猪时输出不含未要求类型()
    {
        // 需求即契约：要了 RichPig×2 → 候选过滤只选类型 ⊆ {普通,土豪} 的模板（Level374）→
        // 输出不含大长猪/爆炸猪/小黄鸭
        var result = Facade.Compose("""{"band":2}""", "501", new ComposeContext("501", 2, "至少包含70只猪，其中包含2只土豪猪", 777));
        Assert.NotNull(result.LevelJson);
        Assert.Empty(result.Errors);
        var counts = ParseTypes(result.LevelJson!);
        Assert.True(counts.GetValueOrDefault("RichPig") >= 2);
        Assert.DoesNotContain("Long", counts.Keys);
        Assert.DoesNotContain("Boom", counts.Keys);
        Assert.DoesNotContain("Duck", counts.Keys);
        Assert.True(counts.GetValueOrDefault("Normal") >= 70);
    }

    [Fact]
    public void 类型纯度_要求的小黄鸭按需求保留()
    {
        // band3 鸭子模板 000359：110 猪含恰好 30 只小黄鸭
        var result = Facade.Compose("""{"band":3}""", "502", new ComposeContext("502", 3, "至少包含100只猪，其中包含30只小黄鸭", 42));
        Assert.NotNull(result.LevelJson);
        var counts = ParseTypes(result.LevelJson!);
        Assert.True(counts.GetValueOrDefault("Duck") >= 30);
        Assert.True(counts.GetValueOrDefault("Normal") >= 70);
    }

    [Fact]
    public void 类型纯度_要求的土豪猪按需求保留()
    {
        var result = Facade.Compose("""{"band":2}""", "501", new ComposeContext("501", 2, "至少包含2只土豪猪", 777));
        Assert.NotNull(result.LevelJson);
        Assert.True(ParseTypes(result.LevelJson!).GetValueOrDefault("RichPig") >= 2);
    }

    private static Dictionary<string, int> ParseTypes(string levelJson)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pig in JsonNode.Parse(levelJson)?["pigs"]!.AsArray())
        {
            var type = pig?["pigType"]?.GetValue<string>() ?? "";
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }
        return counts;
    }
}

// 主玩法类型：Boom 计数、类型需求解析/预检；不只针对某两种猪
public sealed class PiggyFarmPigTypeTests
{
    [Fact]
    public void Boom_被撞三次后状态失败且无合法动作()
    {
        // 两格 Boom 朝右，前方一只 Normal 挡住；每次点击撞一次
        var json = """
        {"levelId":990,"difficulty":1,"levelName":"boom","mapWidth":6,"mapHeight":4,"pigCount":2,
         "pigs":[
           {"id":0,"facing":"Right","pigType":"Boom","specialInfo":"1","occupiedCells":[{"x":0,"y":1},{"x":1,"y":1}]},
           {"id":1,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":3,"y":1},{"x":4,"y":1}]}
         ],"terrainElements":[]}
        """;
        var adapter = new PiggyFarmAdapter();
        var state = adapter.LoadLevel(PiggyFarmLevels.Load(json));
        var next = adapter.ApplyMove(state, new PiggyFarmMove(0));
        Assert.True(next.Failed);
        Assert.Empty(adapter.GetLegalMoves(next));
        Assert.False(adapter.IsGoal(next));
    }

    [Fact]
    public void 类型需求_爆炸猪可解析_弹簧猪明确拒绝()
    {
        var facade = new PiggyFarmFacade();
        Assert.Empty(facade.ValidateContext(new ComposeContext("991", 2, "至少包含3只爆炸猪", 1)));

        var errors = facade.ValidateContext(new ComposeContext("992", 2, "至少包含3只弹簧猪", 1));
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("弹簧猪") && e.Contains("支持子集之外"));
    }

    [Fact]
    public void 类型别名_定时炸弹猪识别为TimeBoom且按未建模拒绝()
    {
        Assert.Equal("TimeBoom", PiggyFarmPigTypes.Resolve("定时炸弹"));
        Assert.Equal("TimeBoom", PiggyFarmPigTypes.Resolve("定时炸弹猪"));
        var errors = new PiggyFarmFacade()
            .ValidateContext(new ComposeContext("996", 2, "至少包含50只猪，其中包含3只定时炸弹猪", 1));
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("定时爆炸猪") && e.Contains("TimeBoom"));
        Assert.DoesNotContain(errors, e => e.Contains("无法识别"));
    }

    [Fact]
    public void 类型需求_土豪猪超过模板数量时拒绝()
    {
        var errors = new PiggyFarmFacade()
            .ValidateContext(new ComposeContext("993", 2, "至少包含3只土豪猪", 1));
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("土豪猪"));
    }

    [Fact]
    public void 类型需求_生成结果真的含3只爆炸猪并满足目标档()
    {
        // band3：Dense112 纯普通猪模板 ⊆ {普通,爆炸} → 转换 3 只普通猪为爆炸猪 → 原生密集设计仍判 3 档
        var facade = new PiggyFarmFacade();
        var result = facade.Compose("{}", "994", new ComposeContext("994", 3, "至少包含50只猪，其中至少包含3只爆炸猪", 7));
        Assert.NotNull(result.LevelJson);
        var level = PiggyFarmLevels.Load(result.LevelJson!);
        Assert.True(level.Pigs.Count(p => p.Type == "Boom") >= 3);
        var outcome = facade.Solve(new SolveCommand(result.LevelJson!, Encoding.UTF8.GetBytes(result.LevelJson!),
            "type-994", TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.Equal(3, outcome.Difficulty.Band);
    }
}

// 渲染契约：四种支持类型都必须有独立 entityType，且 Boom 快照带 counter
public sealed class PiggyFarmVisualTypeTests
{
    [Fact]
    public void 视觉映射_四种支持类型都有独立实体类型()
    {
        var mapping = System.Text.Json.Nodes.JsonNode.Parse(PiggyFarmContent.VisualMapping)!;
        var types = mapping["entityTypes"]!.AsObject().Select(kv => kv.Key).ToHashSet();
        foreach (var type in new[] { "Normal", "Long", "RichPig", "Boom" })
        {
            Assert.Contains("pigHead" + type, types);
            Assert.Contains("pigBody" + type, types);
        }
    }

    [Fact]
    public void 快照_Boom头格实体类型与counter可见()
    {
        var json = """
        {"levelId":995,"difficulty":1,"levelName":"boom","mapWidth":6,"mapHeight":4,"pigCount":1,
         "pigs":[{"id":0,"facing":"Right","pigType":"Boom","specialInfo":"2","occupiedCells":[{"x":0,"y":1},{"x":1,"y":1}]}],
         "terrainElements":[]}
        """;
        var adapter = new PiggyFarmAdapter();
        var snapshot = adapter.Snapshot(adapter.LoadLevel(PiggyFarmLevels.Load(json)));
        var head = snapshot.Entities.Single(e => e.Type == "pigHeadBoom");
        Assert.Equal(2, head.Props!["counter"]!.GetValue<int>());
        Assert.Contains(snapshot.Entities, e => e.Type == "pigBodyBoom");
    }
}

// 小黄鸭规则（Duck.cs 契约）：不可点击；任一离场/碰撞事件后站立鸭子 BFS 寻路到边界即可达离场
//（鸭不挡鸭：BFS 把其它鸭子占格视为空）；被围死的鸭子不离场；顶撞事件同样触发离场检查
public sealed class PiggyFarmDuckRuleTests
{
    private static readonly PiggyFarmAdapter Adapter = new();

    private static string Duck(int id, int x, int y) =>
        $$"""{"id":{{id}},"facing":"Up","pigType":"Duck","specialInfo":"","occupiedCells":[{"x":{{x}},"y":{{y}}}]}""";

    // 双格猪：脚印两格显式给出（竖放/横放由调用方保证与朝向一致，校验器会把关）
    private static string Pig(int id, string facing, (int X, int Y) a, (int X, int Y) b, string type = "Normal") =>
        $$"""{"id":{{id}},"facing":"{{facing}}","pigType":"{{type}}","specialInfo":"3","occupiedCells":[{"x":{{a.X}},"y":{{a.Y}}},{"x":{{b.X}},"y":{{b.Y}}}]}""";

    private static string Level(string pigsJson, int width = 6, int height = 6)
    {
        var count = JsonNode.Parse("[" + pigsJson + "]")!.AsArray().Count;
        return $$"""{"levelId":950,"difficulty":2,"levelName":"duck","mapWidth":{{width}},"mapHeight":{{height}},"pigCount":{{count}},"pigs":[{{pigsJson}}],"terrainElements":[]}""";
    }

    private static PiggyFarmState LoadState(string pigsJson, int width = 6, int height = 6) =>
        Adapter.LoadLevel(PiggyFarmLevels.Load(Level(pigsJson, width, height)));

    [Fact]
    public void 解析_Duck单格脚印合法()
    {
        var json = Level(Duck(2, 3, 3));
        Assert.Empty(new PiggyFarmFacade().FormatErrors(json));
        Assert.Equal("Duck", PiggyFarmLevels.Load(json).Pigs.Single().Type);
    }

    [Fact]
    public void 解析_Duck多格脚印拒绝()
    {
        // 两格脚印的小黄鸭：脚印几何与 1×1 不一致 → 校验拒绝
        var bad = Level(Duck(2, 3, 3) + "," + Pig(1, "Up", (5, 0), (5, 1))).Replace(
            """{"id":2,"facing":"Up","pigType":"Duck","specialInfo":"","occupiedCells":[{"x":3,"y":3}]}""",
            """{"id":2,"facing":"Up","pigType":"Duck","specialInfo":"","occupiedCells":[{"x":3,"y":3},{"x":3,"y":4}]}""");
        Assert.Contains(new PiggyFarmFacade().FormatErrors(bad), e => e.Contains("脚印"));
    }

    [Fact]
    public void 规则_鸭子不在合法动作中()
    {
        var state = LoadState(Pig(1, "Right", (0, 2), (1, 2)) + "," + Duck(2, 5, 5));
        var moves = Adapter.GetLegalMoves(state);
        Assert.Contains(moves, m => m.PigId == 1);
        Assert.DoesNotContain(moves, m => m.PigId == 2);
    }

    [Fact]
    public void 规则_普通猪离场后可达鸭子自动离场()
    {
        // #1 朝右一路滑出右边界 → 离场事件 → 鸭子 (5,5) 可达边界 → 自动离场 → 清场胜利
        var state = LoadState(Pig(1, "Right", (0, 2), (1, 2)) + "," + Duck(2, 5, 5));
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(1));
        Assert.Empty(next.Pigs);
        Assert.True(Adapter.IsGoal(next));
    }

    [Fact]
    public void 规则_被围死的鸭子不离场()
    {
        // 鸭子 (2,2) 四面被 1x2 猪围死（四只猪都朝向鸭子 → 全部被鸭挡住，只能顶撞）
        var layout = string.Join(",",
            Pig(1, "Right", (0, 2), (1, 2)),   // 左：前进一步 = (2,2) 鸭 → 顶撞
            Pig(3, "Left", (3, 2), (4, 2)),    // 右：前进一步 = (2,2) 鸭 → 顶撞
            Pig(4, "Down", (2, 0), (2, 1)),    // 上：前进一步 = (2,2) 鸭 → 顶撞
            Pig(5, "Up", (2, 3), (2, 4)),      // 下：前进一步 = (2,2) 鸭 → 顶撞
            Duck(2, 2, 2));
        var state = LoadState(layout);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(1));   // 顶撞事件
        Assert.Contains(next.Pigs, p => p.Id == 2);                  // 鸭子无通路，留下
        Assert.False(Adapter.IsGoal(next));
    }

    [Fact]
    public void 规则_鸭不挡鸭_缺口被同伴堵住时两只都离场()
    {
        // 口袋唯一开口朝下，且鸭子 B(2,3) 正好堵在鸭子 A(2,2) 的通路上——
        // BFS 把其它鸭子视为空 → A 经 B 的格直下出界，B 亦可达 → 两只都离场
        var layout = string.Join(",",
            Pig(1, "Right", (0, 2), (1, 2)),   // 左墙（朝右被鸭 A 挡）
            Pig(11, "Right", (0, 3), (1, 3)),  // 左墙下段（朝右被鸭 B 挡）
            Pig(3, "Left", (3, 2), (4, 2)),    // 右墙（朝左被鸭 A 挡）
            Pig(31, "Left", (3, 3), (4, 3)),   // 右墙下段（朝左被鸭 B 挡）
            Pig(4, "Down", (2, 0), (2, 1)),    // 顶墙（朝下被鸭 A 挡）
            Duck(2, 2, 2),
            Duck(6, 2, 3));
        var state = LoadState(layout, width: 5, height: 6);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(1));   // 任一顶撞事件
        Assert.DoesNotContain(next.Pigs, p => p.Type == "Duck");     // 两只鸭子都离场
        Assert.Equal(5, next.Pigs.Count);                            // 只剩四面墙
    }

    [Fact]
    public void 规则_顶撞事件触发可达鸭子离场()
    {
        // #1 被墙猪 #2 挡住（顶撞），鸭子 (4,4) 可达边界——顶撞事件 → 鸭子离场
        var layout = string.Join(",",
            Pig(1, "Right", (0, 2), (1, 2)),
            Pig(2, "Up", (2, 2), (2, 3)),
            Duck(4, 4, 4));
        var state = LoadState(layout);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(1));
        Assert.DoesNotContain(next.Pigs, p => p.Type == "Duck");
        Assert.Contains(next.Pigs, p => p.Id == 2);
    }

    [Fact]
    public void 规则_顶撞爆炸猪_阻挡方计数递减()
    {
        var layout = string.Join(",",
            Pig(1, "Right", (0, 2), (1, 2)),
            Pig(2, "Down", (2, 2), (2, 3), type: "Boom"));   // 爆炸猪挡在 #1 前方
        var state = LoadState(layout);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(1));
        Assert.False(next.Failed);
        Assert.Equal(2, next.Pigs.Single(p => p.Id == 2).Counter);
    }

    [Fact]
    public void 规则_爆炸猪顶撞_自身计数递减()
    {
        var layout = string.Join(",",
            Pig(3, "Left", (2, 2), (3, 2)),
            Pig(2, "Right", (0, 2), (1, 2), type: "Boom"));   // 爆炸猪 #2 朝右被 #3 挡
        var state = LoadState(layout);
        var next = Adapter.ApplyMove(state, new PiggyFarmMove(2));   // 爆炸猪顶撞 → 自身计数 -1
        Assert.False(next.Failed);
        Assert.Equal(2, next.Pigs.Single(p => p.Id == 2).Counter);
    }
}
