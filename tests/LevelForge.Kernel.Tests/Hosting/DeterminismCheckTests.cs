using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Hosting;

// 检查4 确定性测试：双跑求解+制带的轨迹一致性（求解内核 §9.3）
public class DeterminismCheckTests
{
    [Fact]
    public void 合规适配器_双跑一致_通过()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();

        var result = ComplianceChecks.CheckDeterminism(bundle, level, "bytes"u8.ToArray());

        Assert.True(result.Passed);
        Assert.Contains(result.Notes, n => n.Contains("结论一致"));
    }

    [Fact]
    public void 跨次加载顺序翻转_双跑暴露_被抓()
    {
        // FlipMoveOrderPerLoad：第二次加载起合法操作顺序翻转——同一次运行内看似正常，双跑必现分歧
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle(SyntheticFailure.FlipMoveOrderPerLoad);

        var result = ComplianceChecks.CheckDeterminism(bundle, level, "bytes"u8.ToArray());

        Assert.False(result.Passed);
        // 两次求解选择了不同的等长最短路
        Assert.Contains(result.Issues, i => i.Contains("动作序列不一致") || i.Contains("描述序列不一致"));
    }
}
