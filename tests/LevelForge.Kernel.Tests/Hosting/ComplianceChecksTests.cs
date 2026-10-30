using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Hosting;

// 检查2 状态键判别 与 检查3 纯函数性测试（求解内核 §9.1/§9.2）
public class ComplianceChecksTests
{
    #region 状态键判别

    [Fact]
    public void 状态键判别_维度样本全部可分_通过()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var samples = new[]
        {
            new StatePairSample<SyntheticState>("节点位置", new SyntheticState(1, level), new SyntheticState(2, level))
        };

        var result = ComplianceChecks.CheckStateKeyDiscrimination(bundle.Game.StateKey, samples);

        Assert.True(result.Passed);
        Assert.Contains(result.Notes, n => n.Contains("1 个维度"));
    }

    [Fact]
    public void 状态键判别_同键维度_失败并点名()
    {
        // 两状态仅"节点位置"不同，但构造的样本用了相同节点——键必然相同，检查必须点名该维度
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var samples = new[]
        {
            new StatePairSample<SyntheticState>("节点位置", new SyntheticState(1, level), new SyntheticState(1, level))
        };

        var result = ComplianceChecks.CheckStateKeyDiscrimination(bundle.Game.StateKey, samples);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.Contains("节点位置"));
    }

    [Fact]
    public void 状态键判别_空样本集_失败()
    {
        // 维度覆盖未经确认：空集不允许通过（零豁免）
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = ComplianceChecks.CheckStateKeyDiscrimination<SyntheticState>(bundle.Game.StateKey, []);

        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.Contains("未提供"));
    }

    #endregion

    #region 纯函数性

    // 构造纯函数检查用的适配层包与样本（起点取中间节点 3，保证有左右两个合法操作）
    private static (AdapterBundle<PurityLevel, PurityState, PurityMove> Bundle, PurityState State, PurityMove Move) PurityFixture(PurityDefect defect)
    {
        var adapter = new PurityAdapter(defect);
        var bundle = new AdapterBundle<PurityLevel, PurityState, PurityMove>("purity", "0", adapter, adapter);
        var state = adapter.LoadLevel(new PurityLevel(8, 5)) with { Node = 3 };
        return (bundle, state, new PurityMove(4));
    }

    [Fact]
    public void 纯函数_合规适配器_通过()
    {
        var (bundle, state, move) = PurityFixture(PurityDefect.None);
        var result = ComplianceChecks.CheckPurity(bundle, state, move);
        Assert.True(result.Passed);
    }

    [Fact]
    public void 纯函数_入参变异_被抓()
    {
        // ApplyMove 向入参日志追加记录 → 调用后再投影原状态发生变化
        var (bundle, state, move) = PurityFixture(PurityDefect.MutateInput);
        var result = ComplianceChecks.CheckPurity(bundle, state, move);
        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.Contains("修改了入参"));
    }

    [Fact]
    public void 纯函数_结果不稳定_被抓()
    {
        // ApplyMove 结果依赖隐藏计数 → 多次应用结果不一致
        var (bundle, state, move) = PurityFixture(PurityDefect.UnstableResult);
        var result = ComplianceChecks.CheckPurity(bundle, state, move);
        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.Contains("不一致"));
    }

    [Fact]
    public void 纯函数_枚举顺序不稳定_被抓()
    {
        // GetLegalMoves 两次调用顺序翻转（违反顺序稳定契约）
        var (bundle, state, move) = PurityFixture(PurityDefect.UnstableMoveOrder);
        var result = ComplianceChecks.CheckPurity(bundle, state, move);
        Assert.False(result.Passed);
        Assert.Contains(result.Issues, i => i.Contains("顺序"));
    }

    #endregion
}
