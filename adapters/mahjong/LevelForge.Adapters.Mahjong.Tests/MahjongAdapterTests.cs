using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.Mahjong.Tests;

// 解析与校验：结构错误 / 子集外拒绝 / 特殊棋子参数边界
public sealed class MahjongParseTests
{
    private static string Level(string chesses, int chessCount, string obstacles = "[]", int width = 6, int height = 6)
        => $$"""{"level":910,"difficulty":1,"isDailyChallenge":false,"chessCount":{{chessCount}},"mapWidth":{{width}},"mapHeight":{{height}},"chesses":[{{chesses}}],"obstacles":{{obstacles}}}""";

    private static string Chess(int id, int x, int y, int type, int specialType = 0, int specialInfo = 0)
        => $$"""{"id":{{id}},"x":{{x}},"y":{{y}},"type":{{type}},"specialType":{{specialType}},"specialInfo":{{specialInfo}}}""";

    [Fact]
    public void 解析_全预设普通关通过()
    {
        var json = Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 2, 0, 2)},{Chess(4, 3, 0, 2)}", 4);
        Assert.Empty(MahjongLevels.Validate(json));
        var level = MahjongLevels.Load(json);
        Assert.Equal(4, level.Chesses.Count);
        Assert.Equal(6, level.MapWidth);
    }

    [Fact]
    public void 解析_type0空位合法()
    {
        // flattenConfig 允许 type=0 空位条目（数量计入 chessCount，但不生成棋子）
        var json = Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 2, 0, 0)},{Chess(4, 3, 0, 2)}", 4);
        Assert.Empty(MahjongLevels.Validate(json));
        var level = MahjongLevels.Load(json);
        Assert.Equal(3, level.Chesses.Count(c => c.Type > 0));
    }

    [Fact]
    public void 子集外_生成型关卡规格拒绝()
    {
        // chesses 2 条 < chessCount 4：运行时随机填充不可复现 → 如实拒绝
        var json = Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)}", 4);
        var errors = MahjongLevels.Validate(json);
        Assert.Contains(errors, e => e.Contains("v1 支持子集之外") && e.Contains("flattenConfig"));
    }

    [Fact]
    public void 子集外_生成期填充参数拒绝()
    {
        // 全预设但携带生成期参数：flatten 后应为 0/缺省
        var json = """{"level":1,"difficulty":1,"isDailyChallenge":false,"chessCount":2,"mapWidth":6,"mapHeight":6,"fillTypeCount":12,"chesses":[{"id":1,"x":0,"y":0,"type":1},{"id":2,"x":1,"y":0,"type":1}],"obstacles":[]}""";
        Assert.Contains(MahjongLevels.Validate(json), e => e.Contains("v1 支持子集之外"));
    }

    [Fact]
    public void 解析_非法数据逐项拒绝()
    {
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1)},{Chess(1, 1, 0, 1)}", 2)));                    // id 重复
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 0, 0, 1)}", 2)));                    // 同格重叠
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 6, 0, 1)},{Chess(2, 1, 0, 1)}", 2)));                    // 越界
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1, 6)},{Chess(2, 1, 0, 1)}", 2)));                 // specialType 越界
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1, 5, 2)},{Chess(2, 1, 0, 1)}", 2)));              // 翻面 info>1
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1, 1, -1)},{Chess(2, 1, 0, 1)}", 2)));             // 冰冻 info 负
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)}", 2, """[{"id":1,"x":0,"y":0,"dir":9}]""")));   // 墙 dir 越界
        Assert.NotEmpty(MahjongLevels.Validate(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)}", 2, width: 40)));         // 尺寸越界
    }

    [Fact]
    public void TryReadLevelId_读level字段()
    {
        Assert.Equal(910, MahjongLevels.TryReadLevelId(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)}", 2)));
        Assert.Null(MahjongLevels.TryReadLevelId("not json"));
    }
}

// 规则语义：相邻/无遮挡消除、拖动推动链、连排、冰冻、墙体、胜负、死局
public sealed class MahjongRuleTests
{
    private static readonly MahjongAdapter Adapter = new();

    private static string Level(string chesses, int chessCount, string obstacles = "[]", int width = 6, int height = 6)
        => $$"""{"level":920,"difficulty":1,"isDailyChallenge":false,"chessCount":{{chessCount}},"mapWidth":{{width}},"mapHeight":{{height}},"chesses":[{{chesses}}],"obstacles":{{obstacles}}}""";

    private static string Chess(int id, int x, int y, int type, int specialType = 0, int specialInfo = 0)
        => $$"""{"id":{{id}},"x":{{x}},"y":{{y}},"type":{{type}},"specialType":{{specialType}},"specialInfo":{{specialInfo}}}""";

    private static MahjongState Load(string json) => Adapter.LoadLevel(MahjongLevels.Load(json));

    [Fact]
    public void 规则_相邻同型可消除_异型不可()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 0, 1)},{Chess(4, 4, 0, 2)}", 4));
        var moves = Adapter.GetLegalMoves(state);
        Assert.Contains(moves, m => m.SourceId == 2 && m.TargetId == 1);    // (1,0)-(0,0) 相邻同型
        Assert.DoesNotContain(moves, m => m.SourceId == 3 && m.TargetId == 4);   // 相邻异型
    }

    [Fact]
    public void 规则_同轴无遮挡消除()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 3, 0, 1)},{Chess(3, 0, 3, 2)},{Chess(4, 3, 3, 2)}", 4));
        var moves = Adapter.GetLegalMoves(state);
        Assert.Contains(moves, m => m.SourceId == 1 && m.TargetId == 2 && m.LandingX == 0 && m.LandingY == 0);
    }

    [Fact]
    public void 规则_同轴有棋子遮挡则不可消()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 2)},{Chess(3, 3, 0, 1)},{Chess(4, 2, 3, 2)}", 4));
        Assert.DoesNotContain(Adapter.GetLegalMoves(state), m => m.SourceId == 1 && m.TargetId == 3);
    }

    [Fact]
    public void 规则_墙体阻断相邻消除()
    {
        var state = Load(Level(
            $"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 0, 1)},{Chess(4, 3, 3, 2)}", 4,
            """[{"id":1,"x":0,"y":0,"dir":4}]"""));
        Assert.DoesNotContain(Adapter.GetLegalMoves(state), m => m.SourceId == 1 && m.TargetId == 2);
    }

    [Fact]
    public void 规则_拖动落点消除()
    {
        var state = Load(Level($"{Chess(1, 1, 0, 1)},{Chess(2, 0, 1, 1)},{Chess(3, 3, 3, 2)},{Chess(4, 4, 3, 2)}", 4));
        Assert.Contains(Adapter.GetLegalMoves(state),
            m => m.SourceId == 2 && m.TargetId == 1 && m.LandingX == 0 && m.LandingY == 0);
    }

    [Fact]
    public void 规则_消除后移除棋子且冰冻解冻()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 3, 2, 1, 2)},{Chess(4, 4, 3, 2, 1, 2)}", 4));
        var move = Adapter.GetLegalMoves(state).First(m => m.SourceId == 1 && m.TargetId == 2);
        var next = Adapter.ApplyMove(state, move);
        Assert.Equal(2, next.Pieces.Count);   // 一对已移除
        var ice = next.Pieces.Single(p => p.Id == 3);
        Assert.Equal(0, ice.SpecialInfo);
        Assert.Equal(MahjongSpecial.Normal, ice.SpecialType);   // 计数归零解冻转普通
    }

    [Fact]
    public void 规则_冰冻棋不可拖不可消()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 0, 2, 1, 2)},{Chess(4, 4, 0, 2, 1, 2)}", 4));
        Assert.DoesNotContain(Adapter.GetLegalMoves(state), m => m.SourceId is 3 or 4 || m.TargetId is 3 or 4);
    }

    [Fact]
    public void 规则_连排组刚性位移()
    {
        // 连排组（#1#2 同组 type1）右移一格后，#1 落 (2,0) 与 #3(4,0) 同型相邻消除：
        // 断言组员 #1 随源棋 #2 同位移、源/目标被移除——连排刚性跟随的语义核心
        var state = Load(Level(
            $"{Chess(1, 0, 0, 1, 3, 7)},{Chess(2, 1, 0, 1, 3, 7)},{Chess(3, 4, 0, 1)},{Chess(4, 4, 2, 2)},{Chess(5, 5, 2, 2)},{Chess(6, 5, 4, 1)}", 6, width: 8));
        var moves = Adapter.GetLegalMoves(state);
        var move = Assert.Single(moves.Where(m => m.SourceId == 2 && m.TargetId == 3 && m.LandingX == 3 && m.LandingY == 0));
        var next = Adapter.ApplyMove(state, move);
        Assert.DoesNotContain(next.Pieces, p => p.Id is 2 or 3);   // 源/目标已消除
        var mate = next.Pieces.Single(p => p.Id == 1);
        Assert.Equal(2, mate.X);   // 组员随源棋同位移 (0,0)+(3-1,0)=(2,0)
        Assert.Equal(0, mate.Y);
    }

    [Fact]
    public void 规则_墙体棋不计入胜利且不可操作()
    {
        // 只剩 1 只普通棋 + 1 只墙棋：非墙奇数、无配对 → 死局（无合法走法、非胜利）
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 5, 5, 6, 4)}", 2));
        Assert.False(Adapter.IsGoal(state));
        Assert.Empty(Adapter.GetLegalMoves(state));
    }

    [Fact]
    public void 规则_清盘胜利()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 5, 5, 6, 4)}", 3));
        var move = Adapter.GetLegalMoves(state).Single(m => m.SourceId == 1 && m.TargetId == 2);
        Assert.True(Adapter.IsGoal(Adapter.ApplyMove(state, move)));   // 只剩墙体 → 胜利
        Assert.False(Adapter.IsGoal(state));
    }

    [Fact]
    public void 规则_死局无合法走法()
    {
        // 四只棋子两两异型、无同轴视线 → 无任何可消除走法
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 1, 2)},{Chess(3, 3, 3, 3)},{Chess(4, 4, 4, 4)}", 4));
        Assert.Empty(Adapter.GetLegalMoves(state));
    }

    [Fact]
    public void 状态键_覆盖位置与牌面维度()
    {
        var baseState = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)}", 2));
        var moved = baseState.With([baseState.Pieces[0] with { X = 3 }, baseState.Pieces[1]]);
        var typed = baseState.With([baseState.Pieces[0] with { Type = 9 }, baseState.Pieces[1]]);
        Assert.NotEqual(Adapter.StateKey(baseState), Adapter.StateKey(moved));
        Assert.NotEqual(Adapter.StateKey(baseState), Adapter.StateKey(typed));
    }

    [Fact]
    public void 状态键_抽象口径_翻面朝向不入键()
    {
        // 游戏 hashState 官方口径：FLIP 的朝向归并不入键（LevelSolvabilitySolver.ts:358-366）
        var flipA = Load(Level($"{Chess(1, 0, 0, 4, 5, 0)},{Chess(2, 1, 0, 4, 5, 1)}", 2));
        var flipB = flipA.With([flipA.Pieces[0] with { SpecialInfo = 1 }, flipA.Pieces[1]]);
        Assert.Equal(Adapter.StateKey(flipA), Adapter.StateKey(flipB));
        // 冰冻计数在语义内 → 入键
        var iceA = Load(Level($"{Chess(1, 0, 0, 4, 1, 2)},{Chess(2, 1, 0, 4, 1, 2)}", 2));
        var iceB = iceA.With([iceA.Pieces[0] with { SpecialInfo = 3 }, iceA.Pieces[1]]);
        Assert.NotEqual(Adapter.StateKey(iceA), Adapter.StateKey(iceB));
    }

    [Fact]
    public void 纯函数_ApplyMove不改入参且结果确定()
    {
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 3, 2)},{Chess(4, 4, 3, 2)}", 4));
        var keyBefore = Adapter.StateKey(state);
        var move = Adapter.GetLegalMoves(state).First();
        var next1 = Adapter.ApplyMove(state, move);
        var next2 = Adapter.ApplyMove(state, move);
        Assert.Equal(keyBefore, Adapter.StateKey(state));   // 入参未被修改
        Assert.Equal(Adapter.StateKey(next1), Adapter.StateKey(next2));
        Assert.Equal(next1, next2);
    }

    [Fact]
    public void 启发式_剩余对数下界()
    {
        // 非墙 4 只 + 墙 1 只 → 至少 2 步（墙不计入）
        var state = Load(Level($"{Chess(1, 0, 0, 1)},{Chess(2, 1, 0, 1)},{Chess(3, 3, 3, 2)},{Chess(4, 4, 3, 2)},{Chess(5, 5, 5, 6, 4)}", 5));
        Assert.Equal(2, Adapter.EstimateToGoal(state));
    }
}

// 执行协议 + 试玩 + 门1 + 真实关端到端
public sealed class MahjongProtocolAndGateTests
{
    private static readonly MahjongFacade Facade = new();
    private static readonly MahjongAdapter Adapter = new();

    [Fact]
    public void 协议_编码解码往返()
    {
        var move = new MahjongMove(3, 7, 2, 5);
        var decoded = Adapter.DecodeMove(Adapter.EncodeMove(move));
        Assert.Equal(move, decoded);
        Assert.ThrowsAny<Exception>(() => Adapter.DecodeMove(new JsonObject { ["sourceId"] = 3 }));
    }

    [Fact]
    public void 试玩_合法序列步进_非法动作拒绝()
    {
        var levelJson = MahjongContent.RealLevel1Json;
        var moves = Adapter.GetLegalMoves(Adapter.LoadLevel(MahjongLevels.Load(levelJson)));
        var play = Facade.Play(levelJson, [Adapter.EncodeMove(moves[0])]);
        Assert.Equal(1, play.MovesExecuted);
        Assert.NotEmpty(play.LegalMoves);
        Assert.False(play.IsGoal);
        Assert.Throws<PlayException>(() =>
            Facade.Play(levelJson, [Adapter.EncodeMove(moves[0]), Adapter.EncodeMove(new MahjongMove(99, 98, 0, 0))]));
    }

    [Fact]
    public void 门1_五项检查全绿()
    {
        var report = Facade.RunGate1Checks();
        var failures = new[] { report.DependencyScan, report.StateKeyDiscrimination, report.Purity, report.Determinism, report.KernelSelfTest }
            .Where(c => !c.Passed)
            .Select(c => $"{c.Kind}: {string.Join("|", c.Issues)}")
            .ToList();
        Assert.True(report.AllPassed, string.Join("；", failures));
    }

    [Fact]
    public void 真实关端到端_语料1号关求解出磁带_win()
    {
        var levelJson = MahjongContent.RealLevel1Json;
        var outcome = Facade.Solve(new SolveCommand(levelJson, System.Text.Encoding.UTF8.GetBytes(levelJson), "1", TimeSpan.FromSeconds(30)));

        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.NotNull(outcome.Tape);
        Assert.NotNull(outcome.Result.Metrics);
        Assert.Equal(8, outcome.Result.Solution.Count);          // 16 只 = 8 对
        Assert.Equal(8, outcome.Tape.Steps.Count);
        Assert.Equal(ExecutionOutcome.Win, outcome.Tape.Outcome);
        Assert.Equal("mahjong-260417", outcome.Tape.GameId);
        Assert.NotEmpty(outcome.Tape.Initial.SemanticStateHash);
        // 难度结论带模型版本
        Assert.Equal("mahjong-fixed-v1", outcome.Difficulty.ModelVersion);
        Assert.InRange(outcome.Difficulty.Band, 1, 3);
    }

    [Fact]
    public void 真实关_生成型规格被拒_子集外()
    {
        // 语料 3.json：chesses 为空、chessCount=80（生成型规格）→ 子集外拒绝
        var generatorSpec = """{"level":3,"difficulty":2,"isDailyChallenge":false,"chessCount":80,"mapWidth":8,"mapHeight":10,"fillTypeCount":20,"adjacentPairCount":9,"popChessPairCount":2,"countdown":240,"chesses":[],"obstacles":[]}""";
        Assert.Contains(Facade.FormatErrors(generatorSpec), e => e.Contains("v1 支持子集之外"));
        var outcome = Facade.Solve(new SolveCommand(generatorSpec, System.Text.Encoding.UTF8.GetBytes(generatorSpec), "3"));
        Assert.Equal(SolveStatus.Invalid, outcome.Result.Status);
    }
}
