using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using Xunit;

namespace LevelForge.Adapters.SokobanToy.Tests;

// 玩具推箱子适配器测试：规则语义、状态键、快照、求解预期、磁带、门1合规
public class SokobanToyAdapterTests
{
    private static readonly SokobanToyAdapter Adapter = new();
    private static readonly BfsSolver<SokobanLevel, SokobanState, SokobanMove> Solver = new();

    // 构造适配层包
    private static AdapterBundle<SokobanLevel, SokobanState, SokobanMove> Bundle()
    {
        return new AdapterBundle<SokobanLevel, SokobanState, SokobanMove>("sokoban-toy", "0.1.0", Adapter, Adapter);
    }

    // 解析预制关 JSON
    private static SokobanLevel ParseLevel(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        return new SokobanLevel(node["id"]!.GetValue<string>(), node["grid"]!.AsArray().Select(g => g!.GetValue<string>()).ToArray());
    }

    #region LoadLevel 与校验

    [Fact]
    public void 解析_位置提取正确()
    {
        var level = ParseLevel("""{"id":"t","grid":["#####","#@$.#","#####"]}""");
        var s = Adapter.LoadLevel(level);
        Assert.Equal((1, 1), (s.Px, s.Py));                        // 玩家
        Assert.Single(s.Boxes);                                    // 一只箱子
        Assert.Equal((2, 1), s.Boxes[0]);                          // 箱子位置与扫描序
    }

    [Theory]
    [InlineData("""{"id":"t","grid":["#####","#$. #","#####"]}""", "玩家数")]          // 无玩家
    [InlineData("""{"id":"t","grid":["#####","#@$.#","#@$.#","#####"]}""", "玩家数")]   // 双玩家
    [InlineData("""{"id":"t","grid":["######","#@$.#","######"]}""", "宽度")]          // 非矩形
    [InlineData("""{"id":"t","grid":["#####","#@$.x","#####"]}""", "未知字符")]         // 未知字符
    [InlineData("""{"id":"t","grid":["#####","#@$..#","#####"]}""", "箱子数")]          // 箱≠目标
    [InlineData("""{"id":"t","grid":["#@$.#","#####"]}""", "顶边")]                     // 边界无墙
    public void 非法关卡_抛参数异常(string json, string expectedFragment)
    {
        var level = ParseLevel(json);
        var ex = Assert.Throws<ArgumentException>(() => Adapter.LoadLevel(level));
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void 校验器_合法候选通过()
    {
        var (level, errors) = SokobanToyValidator.ValidateJson(SokobanToyContent.LevelJsons[0]);
        Assert.Empty(errors);
        Assert.Equal("toy-001", level!.Id);
    }

    [Fact]
    public void 校验器_坏JSON与缺字段报错()
    {
        Assert.NotEmpty(SokobanToyValidator.ValidateJson("{ not json").Errors);
        var (_, errors2) = SokobanToyValidator.ValidateJson("""{"grid":["#####","#@$.#","#####"]}""");
        Assert.Contains(errors2, e => e.Contains("id"));
    }

    #endregion

    #region 移动与推箱规则

    [Fact]
    public void 走位_空格可行_墙与推不动不可行()
    {
        // 玩家(1,1) 箱(2,1) 目标(3,1)，下方有空地：右=推箱；上/左=墙；下=走位
        var level = ParseLevel("""{"id":"t","grid":["#####","#@$.#","#   #","#####"]}""");
        var s = Adapter.LoadLevel(level);
        var moves = Adapter.GetLegalMoves(s);
        // 只有 右(推箱) 与 下(走位) 合法
        Assert.Contains(new SokobanMove(1, 0), moves);
        Assert.Contains(new SokobanMove(0, 1), moves);
        Assert.DoesNotContain(new SokobanMove(0, -1), moves);      // 上=墙
        Assert.DoesNotContain(new SokobanMove(-1, 0), moves);      // 左=墙
    }

    [Fact]
    public void 推箱_箱后为墙不可推()
    {
        // 箱(3,1) 右侧(4,1)是墙：向右推不动；左/下为空地可走
        var level = ParseLevel("""{"id":"t","grid":["######","# @$##","#  . #","######"]}""");
        var s = Adapter.LoadLevel(level);
        var moves = Adapter.GetLegalMoves(s);
        Assert.DoesNotContain(new SokobanMove(1, 0), moves);       // 箱后为墙
        Assert.Contains(new SokobanMove(-1, 0), moves);            // 左走
        Assert.Contains(new SokobanMove(0, 1), moves);             // 下走
    }

    [Fact]
    public void 推箱_玩家与箱子同时前移()
    {
        var level = ParseLevel("""{"id":"t","grid":["#####","#@$.#","#####"]}""");
        var s = Adapter.LoadLevel(level);
        var next = Adapter.ApplyMove(s, new SokobanMove(1, 0));
        Assert.Equal((2, 1), (next.Px, next.Py));                  // 玩家进到原箱位
        Assert.Equal((3, 1), next.Boxes[0]);                       // 箱子被推到目标格
        Assert.True(Adapter.IsGoal(next));                         // 归位即胜
        Assert.Equal("右移（推箱）", Adapter.DescribeMove(s, new SokobanMove(1, 0)));
    }

    [Fact]
    public void 非法操作_抛异常且入参不变()
    {
        var level = ParseLevel("""{"id":"t","grid":["#####","#@$.#","#####"]}""");
        var s = Adapter.LoadLevel(level);
        var before = (s.Px, s.Py, s.Boxes[0]);
        Assert.Throws<InvalidOperationException>(() => Adapter.ApplyMove(s, new SokobanMove(0, -1)));   // 上=墙
        Assert.Equal(before, (s.Px, s.Py, s.Boxes[0]));            // 入参未被修改
    }

    #endregion

    #region 状态键与语义投影

    [Fact]
    public void 状态键_箱子数组顺序无关()
    {
        // 逻辑相等（同箱子集合，不同下标顺序）→ 键必须相同
        var level = ParseLevel("""{"id":"t","grid":["#######","# @ $.#","#######"]}""");
        var a = new SokobanState(2, 1, [(3, 1), (5, 1)], level);
        var b = new SokobanState(2, 1, [(5, 1), (3, 1)], level);
        Assert.Equal(Adapter.StateKey(a), Adapter.StateKey(b));
        // 任一箱子位置不同 → 键不同
        var c = new SokobanState(2, 1, [(3, 1), (4, 1)], level);
        Assert.NotEqual(Adapter.StateKey(a), Adapter.StateKey(c));
    }

    [Fact]
    public void 动作编解码_往返一致()
    {
        foreach (var (dx, dy, dir) in new[] { (0, -1, "up"), (0, 1, "down"), (-1, 0, "left"), (1, 0, "right") })
        {
            var encoded = Adapter.EncodeMove(new SokobanMove(dx, dy));
            Assert.Equal(dir, encoded["dir"]!.GetValue<string>());
            var decoded = Adapter.DecodeMove(encoded);
            Assert.Equal(new SokobanMove(dx, dy), decoded);
        }
        Assert.Throws<ArgumentException>(() => Adapter.DecodeMove(new JsonObject { ["dir"] = "jump" }));
    }

    [Fact]
    public void 快照_实体ID稳定且全局量可见()
    {
        var level = ParseLevel("""{"id":"t","grid":["#####","#@$.#","#####"]}""");
        var s = Adapter.LoadLevel(level);
        var snap = Adapter.Snapshot(s);
        Assert.Equal(5, snap.Board.Width);
        Assert.Equal("player", snap.Entities[0].Id);
        Assert.Equal("box-0", snap.Entities[1].Id);
        Assert.False(snap.Entities[1].Props!["onGoal"]!.GetValue<bool>());   // 未归位
        Assert.Equal(0, snap.Globals["boxesOnGoal"]!.GetValue<int>());
        // 推到位后归位标志翻转
        var next = Adapter.ApplyMove(s, new SokobanMove(1, 0));
        var snap2 = Adapter.Snapshot(next);
        Assert.True(snap2.Entities[1].Props!["onGoal"]!.GetValue<bool>());
        Assert.Equal(1, snap2.Globals["boxesOnGoal"]!.GetValue<int>());
    }

    #endregion

    #region 十关求解预期（玩具的基准预期）

    [Theory]
    [InlineData(0, true)]      // toy-001 示例
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]     // toy-010 已知无解（角落箱）
    public void 预制关_求解结论符合人工确认预期(int index, bool expectSolved)
    {
        var level = ParseLevel(SokobanToyContent.LevelJsons[index]);
        var result = Solver.Solve(Bundle(), new SolveRequest<SokobanLevel>(level));

        if (expectSolved)
        {
            Assert.True(result.Status == SolveStatus.Solved, $"期望可解，实际 {result.Status}（{result.ErrorMessage}）");
            // 独立回放验证：解序列逐步执行后确实通关（不采信求解器自述）
            var s = Adapter.LoadLevel(level);
            foreach (var rec in result.Solution)
            {
                s = Adapter.ApplyMove(s, Adapter.DecodeMove(rec.Move));
            }
            Assert.True(Adapter.IsGoal(s));
        }
        else
        {
            // 完整遍历后可证无解：graphComplete 必须为 true
            Assert.Equal(SolveStatus.UnsolvableProven, result.Status);
            Assert.True(result.GraphComplete);
        }
    }

    [Fact]
    public void 磁带录制_结局与步数正确()
    {
        var level = ParseLevel(SokobanToyContent.LevelJsons[0]);
        var result = Solver.Solve(Bundle(), new SolveRequest<SokobanLevel>(level));
        var tape = new ReplayEngine().RecordTape(Bundle(), level, "toy-001-bytes"u8.ToArray(), result, new TapeMetadata("toy-001", null, null));

        Assert.Single(tape.Steps);
        Assert.Equal(ExecutionOutcome.Win, tape.Outcome);
        // 末步语义投影中箱子已在目标位
        var box = tape.Steps[0].SemanticState["boxes"]![0]!;
        Assert.Equal(3, box["x"]!.GetValue<int>());
    }

    #endregion

    #region 门1合规（五项检查）

    [Fact]
    public void 门1合规_五项全绿()
    {
        // 玩具适配器是 skill 参考实现：必须自身通过门1机器部分
        var level = ParseLevel(SokobanToyContent.LevelJsons[1]);   // toy-002
        var state = Adapter.LoadLevel(level);
        var input = new HostingCheckInput<SokobanLevel, SokobanState, SokobanMove>(
            Bundle(),
            typeof(SokobanToyAdapter).Assembly,
            [
                new StatePairSample<SokobanState>("玩家位置",
                    state with { Px = 1, Py = 1 }, state with { Px = 1, Py = 3 }),
                new StatePairSample<SokobanState>("箱子位置",
                    state, state with { Boxes = [(3, 2)] })
            ],
            (state, new SokobanMove(1, 0)),                        // toy-002 起点向右推箱合法
            level,
            "toy-002-bytes"u8.ToArray());

        var report = new AdapterHosting().RunAll(input);
        Assert.True(report.AllPassed, string.Join(";", report.StateKeyDiscrimination.Issues
            .Concat(report.Purity.Issues).Concat(report.Determinism.Issues)));
    }

    #endregion
}
