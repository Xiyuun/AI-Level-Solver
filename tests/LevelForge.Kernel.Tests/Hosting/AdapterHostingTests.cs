using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Hosting;

// 适配托管编排测试：五项检查汇总、缺陷汇总、内核自测
public class AdapterHostingTests
{
    // 合成适配器的完整检查输入（全绿基线）
    private static HostingCheckInput<SyntheticLevel, SyntheticState, SyntheticMove> SyntheticInput(
        SyntheticFailure failure = SyntheticFailure.None)
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle(failure);
        return new HostingCheckInput<SyntheticLevel, SyntheticState, SyntheticMove>(
            bundle,
            typeof(SyntheticGraphAdapter).Assembly,
            [new StatePairSample<SyntheticState>("节点位置", new SyntheticState(1, level), new SyntheticState(2, level))],
            (new SyntheticState(0, level), new SyntheticMove(1)),
            level,
            "bytes"u8.ToArray());
    }

    [Fact]
    public void 五项检查_合规适配器全绿()
    {
        var report = new AdapterHosting().RunAll(SyntheticInput());
        Assert.True(report.AllPassed);
        Assert.True(report.DependencyScan.Passed);
        Assert.True(report.StateKeyDiscrimination.Passed);
        Assert.True(report.Purity.Passed);
        Assert.True(report.Determinism.Passed);
        Assert.True(report.KernelSelfTest.Passed);
    }

    [Fact]
    public void 单项缺陷_总报告不通过并点名()
    {
        // 跨次顺序翻转：确定性检查单独失败，其余通过，AllPassed=false
        var report = new AdapterHosting().RunAll(SyntheticInput(SyntheticFailure.FlipMoveOrderPerLoad));
        Assert.False(report.AllPassed);
        Assert.False(report.Determinism.Passed);
        Assert.True(report.StateKeyDiscrimination.Passed);
        Assert.True(report.KernelSelfTest.Passed);
    }

    [Fact]
    public void 未提供程序集_依赖扫描明确Fail()
    {
        var input = SyntheticInput() with { AdapterAssembly = null };
        var report = new AdapterHosting().RunAll(input);
        Assert.False(report.DependencyScan.Passed);
        Assert.Contains(report.DependencyScan.Issues, i => i.Contains("未提供"));
    }

    [Fact]
    public void 内核自测_独立运行通过()
    {
        // 门1第5项：不依赖任何真项目与测试框架
        Assert.True(KernelSelfTest.Run().Passed);
    }
}
