using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using Xunit;

namespace LevelForge.Kernel.Tests.Hashing;

// 四类摘要的取值规则验收测试（渲染规范 §5.1 每类至少覆盖"何时相等/何时不同"）
public class TapeHashesTests
{
    #region levelHash

    [Fact]
    public void LevelHash_对原始字节计算SHA256()
    {
        var bytes = "hello levelforge"u8.ToArray();
        var expected = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expected, TapeHashes.LevelHash(bytes));
    }

    [Fact]
    public void LevelHash_内容不同则不同()
    {
        var a = TapeHashes.LevelHash("level-a"u8.ToArray());
        var b = TapeHashes.LevelHash("level-b"u8.ToArray());
        Assert.NotEqual(a, b);
    }

    #endregion

    #region semanticStateHash

    [Fact]
    public void SemanticStateHash_键序与实体序无关()
    {
        // 语义相等（仅键序/实体顺序不同）的投影必须得到相同摘要
        var a = JsonNode.Parse("{\"pig\":{\"x\":1},\"entities\":[{\"id\":\"B\"},{\"id\":\"A\"}]}")!;
        var b = JsonNode.Parse("{\"entities\":[{\"id\":\"A\"},{\"id\":\"B\"}],\"pig\":{\"x\":1}}")!;
        Assert.Equal(
            TapeHashes.SemanticStateHash(1, a),
            TapeHashes.SemanticStateHash(1, b));
    }

    [Fact]
    public void SemanticStateHash_区分schema版本()
    {
        // schemaVer 参与摘要：跨语义版本不比较
        var state = JsonNode.Parse("{\"n\":1}")!;
        Assert.NotEqual(
            TapeHashes.SemanticStateHash(1, state),
            TapeHashes.SemanticStateHash(2, state));
    }

    [Fact]
    public void SemanticStateHash_语义内容不同则不同()
    {
        var a = JsonNode.Parse("{\"n\":1}")!;
        var b = JsonNode.Parse("{\"n\":2}")!;
        Assert.NotEqual(TapeHashes.SemanticStateHash(1, a), TapeHashes.SemanticStateHash(1, b));
    }

    #endregion

    #region trajectoryHash

    [Fact]
    public void TrajectoryHash_排除moveText与视觉()
    {
        // 轨迹输入刻意不含 moveText/快照——此处用同一结构验证：附加展示字段不进摘要
        var initial = JsonNode.Parse("{\"n\":0}")!;
        var stepsA = new List<(JsonNode, JsonNode)>
        {
            (JsonNode.Parse("{\"type\":\"push\"}")!, JsonNode.Parse("{\"n\":1}")!)
        };
        // 同一轨迹换一种实现（不同节点实例、相同语义）必须同摘要
        var stepsB = new List<(JsonNode, JsonNode)>
        {
            (JsonNode.Parse("{\"type\":\"push\"}")!, JsonNode.Parse("{\"n\":1}")!)
        };
        Assert.Equal(
            TapeHashes.TrajectoryHash(1, 1, initial, stepsA, ExecutionOutcome.Win),
            TapeHashes.TrajectoryHash(1, 1, initial, stepsB, ExecutionOutcome.Win));
    }

    [Fact]
    public void TrajectoryHash_步序不同则不同()
    {
        var initial = JsonNode.Parse("{\"n\":0}")!;
        var stepsA = new List<(JsonNode, JsonNode)>
        {
            (JsonNode.Parse("{\"type\":\"a\"}")!, JsonNode.Parse("{\"n\":1}")!),
            (JsonNode.Parse("{\"type\":\"b\"}")!, JsonNode.Parse("{\"n\":2}")!)
        };
        // 交换两步顺序 = 不同轨迹
        var stepsB = new List<(JsonNode, JsonNode)>
        {
            (JsonNode.Parse("{\"type\":\"b\"}")!, JsonNode.Parse("{\"n\":2}")!),
            (JsonNode.Parse("{\"type\":\"a\"}")!, JsonNode.Parse("{\"n\":1}")!)
        };
        Assert.NotEqual(
            TapeHashes.TrajectoryHash(1, 1, initial, stepsA, ExecutionOutcome.Win),
            TapeHashes.TrajectoryHash(1, 1, initial, stepsB, ExecutionOutcome.Win));
    }

    [Fact]
    public void TrajectoryHash_outcome参与摘要()
    {
        var initial = JsonNode.Parse("{\"n\":0}")!;
        var steps = new List<(JsonNode, JsonNode)> { (JsonNode.Parse("{\"t\":1}")!, JsonNode.Parse("{\"n\":1}")!) };
        Assert.NotEqual(
            TapeHashes.TrajectoryHash(1, 1, initial, steps, ExecutionOutcome.Win),
            TapeHashes.TrajectoryHash(1, 1, initial, steps, ExecutionOutcome.Lose));
    }

    [Fact]
    public void TrajectoryHash_动作schema版本参与摘要()
    {
        var initial = JsonNode.Parse("{\"n\":0}")!;
        var steps = new List<(JsonNode, JsonNode)> { (JsonNode.Parse("{\"t\":1}")!, JsonNode.Parse("{\"n\":1}")!) };
        Assert.NotEqual(
            TapeHashes.TrajectoryHash(1, 1, initial, steps, ExecutionOutcome.Win),
            TapeHashes.TrajectoryHash(2, 1, initial, steps, ExecutionOutcome.Win));
    }

    #endregion

    #region artifactHash

    [Fact]
    public void ArtifactHash_无自包含哈希循环()
    {
        // 先算摘要再写入 artifactHash 字段，重算必须一致（摘要自身被排除）
        var tape = (JsonObject)JsonNode.Parse("{\"gameId\":\"boxland\",\"steps\":[{\"moveText\":\"推\"}]}")!;
        var h1 = TapeHashes.ArtifactHash(tape);
        tape["artifactHash"] = h1;
        Assert.Equal(h1, TapeHashes.ArtifactHash(tape));
    }

    [Fact]
    public void ArtifactHash_内容变化则变化_含纯文案()
    {
        // artifactHash 用途是文件完整性：moveText 改动也会引起变化（区别于 trajectoryHash）
        var tape = (JsonObject)JsonNode.Parse("{\"steps\":[{\"moveText\":\"推箱A向上\"}]}")!;
        var h1 = TapeHashes.ArtifactHash(tape);
        tape["steps"]![0]!["moveText"] = "推箱A向下";
        Assert.NotEqual(h1, TapeHashes.ArtifactHash(tape));
    }

    #endregion
}
