using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Replay;

// 回放引擎与磁带录制测试（求解内核 §8、渲染规范 §5）
public class ReplayEngineTests
{
    private static readonly BfsSolver<SyntheticLevel, SyntheticState, SyntheticMove> Solver = new();
    private readonly ReplayEngine _engine = new();

    // 求解菱形图并录制磁带的便捷方法
    private SolutionTape SolveAndRecord(SyntheticLevel level)
    {
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));
        var bytes = "level-file-bytes"u8.ToArray();
        return _engine.RecordTape(bundle, level, bytes, result, new TapeMetadata("L-1", null, null));
    }

    #region 录制内容正确性

    [Fact]
    public void 菱形图录制_逐步语义与结局正确()
    {
        var tape = SolveAndRecord(new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) }));

        Assert.Equal(2, tape.Steps.Count);
        // 逐步语义投影记录结算后的节点
        Assert.Equal(1, tape.Steps[0].SemanticState["node"]!.GetValue<int>());
        Assert.Equal(3, tape.Steps[1].SemanticState["node"]!.GetValue<int>());
        Assert.Equal(["0->1", "1->3"], tape.Steps.Select(s => s.MoveText).ToArray());
        Assert.Equal([1, 2], tape.Steps.Select(s => s.Index).ToArray());
        // 结局由实际执行重判：到达目标 = win
        Assert.Equal(ExecutionOutcome.Win, tape.Outcome);
        // 初始帧语义为起点
        Assert.Equal(0, tape.Initial.SemanticState["node"]!.GetValue<int>());
        // 版本与指纹字段齐备
        Assert.Equal(1, tape.MoveSchemaVer);
        Assert.Equal(1, tape.SemanticSchemaVer);
        Assert.Equal("L-1", tape.LevelRef);
        Assert.Equal(TapeHashes.LevelHash("level-file-bytes"u8.ToArray()), tape.LevelHash);
        Assert.Equal(TapeConstants.KernelVersion, tape.KernelVer);
    }

    [Fact]
    public void 零步解磁带_步骤为空结局win()
    {
        // 初始即目标：磁带只有初始帧
        var tape = SolveAndRecord(new SyntheticLevel(0, 0, new[] { (0, 1) }));

        Assert.Empty(tape.Steps);
        Assert.Equal(ExecutionOutcome.Win, tape.Outcome);
        Assert.NotNull(tape.Initial.SemanticState);
        Assert.NotEmpty(tape.TrajectoryHash);
    }

    [Fact]
    public void 序列未达目标_结局incomplete()
    {
        // 手工构造只走一步 0->1 的序列（目标 3 未达）
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var adapter = (SyntheticGraphAdapter)bundle.Execution;
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (1, 3) });
        var moves = new List<MoveRecord>
        {
            new(adapter.EncodeMove(new SyntheticMove(1)), "0->1")
        };
        var tape = _engine.RecordTape(bundle, level, "bytes"u8.ToArray(), moves, new TapeMetadata("L-2", null, null));

        Assert.Single(tape.Steps);
        Assert.Equal(ExecutionOutcome.Incomplete, tape.Outcome);
    }

    #endregion

    #region 摘要与确定性

    [Fact]
    public void 轨迹哈希与TapeHashes手算一致()
    {
        // 磁带内嵌的 trajectoryHash 必须能由磁带字段独立重算出来（证据可追溯）
        var tape = SolveAndRecord(new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) }));
        var recomputed = TapeHashes.TrajectoryHash(
            tape.MoveSchemaVer, tape.SemanticSchemaVer,
            tape.Initial.SemanticState,
            tape.Steps.Select(s => (s.Move, s.SemanticState)).ToList(),
            tape.Outcome);

        Assert.Equal(recomputed, tape.TrajectoryHash);
    }

    [Fact]
    public void 同输入两次录制_轨迹与文件摘要一致()
    {
        // 磁带无时间戳等运行元信息：双跑两个摘要都必须一致（确定性测试的磁带级形态）
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var t1 = SolveAndRecord(level);
        var t2 = SolveAndRecord(level);

        Assert.Equal(t1.TrajectoryHash, t2.TrajectoryHash);
        Assert.Equal(t1.ArtifactHash, t2.ArtifactHash);
    }

    #endregion

    #region 序列化往返

    [Fact]
    public void 序列化往返_字段与摘要一致()
    {
        var tape = SolveAndRecord(new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) }));
        var json = TapeJson.Serialize(tape);
        var parsed = TapeJson.Parse(json);

        // 逐字段往返一致
        Assert.Equal(tape.GameId, parsed.GameId);
        Assert.Equal(tape.LevelRef, parsed.LevelRef);
        Assert.Equal(tape.TrajectoryHash, parsed.TrajectoryHash);
        Assert.Equal(tape.ArtifactHash, parsed.ArtifactHash);
        Assert.Equal(tape.Outcome, parsed.Outcome);
        Assert.Equal(tape.Steps.Select(s => s.MoveText), parsed.Steps.Select(s => s.MoveText));
        Assert.Equal(tape.Steps.Select(s => s.SemanticState.ToJsonString()), parsed.Steps.Select(s => s.SemanticState.ToJsonString()));

        // 枚举输出为小写字符串（渲染规范 §5 字段形态）
        Assert.Contains("\"outcome\":\"win\"", json);

        // 文件完整性摘要可从解析后的磁带独立重算
        var recomputed = TapeHashes.ArtifactHash((JsonObject)JsonSerializer.SerializeToNode(parsed, TapeJson.Options)!);
        Assert.Equal(tape.ArtifactHash, recomputed);
    }

    [Fact]
    public void 非法磁带JSON_解析抛异常()
    {
        Assert.ThrowsAny<JsonException>(() => TapeJson.Parse("{ not valid json"));
    }

    #endregion

    #region 异常传播

    [Fact]
    public void 非法动作编码_中断录制()
    {
        // 动作 JSON 无法解码 → 按契约抛异常，录制中止（调用方决定如何报告）
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (1, 3) });
        var badMove = new MoveRecord(new JsonObject { ["bad"] = 1 }, "坏动作");
        Assert.Throws<ArgumentException>(() =>
            _engine.RecordTape(bundle, level, "bytes"u8.ToArray(), new[] { badMove }, new TapeMetadata("L-3", null, null)));
    }

    #endregion
}
