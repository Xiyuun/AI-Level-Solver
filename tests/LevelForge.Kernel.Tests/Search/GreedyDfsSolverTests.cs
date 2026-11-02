using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Search;

// 贪心 DFS 策略测试：可解路径、NotProven 最优性、预算诚实性、穷尽才可证无解
public class GreedyDfsSolverTests
{
    private static readonly GreedyDfsSolver<SyntheticLevel, SyntheticState, SyntheticMove> Greedy = new();

    [Fact]
    public void 可解图_找到路径且回放通关_NotProven最优性()
    {
        // 菱形图：贪心按 h 排序找到 2 步解（合成适配器 h≡0，按枚举序潜水）
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Greedy.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        // 贪心不证明最短——即使碰巧最短也如实标 NotProven
        Assert.Equal(Optimality.NotProven, result.Optimality);
        Assert.False(result.GraphComplete);
        // 独立回放验证
        var s = bundle.Game.LoadLevel(level);
        foreach (var rec in result.Solution)
        {
            s = bundle.Game.ApplyMove(s, bundle.Execution.DecodeMove(rec.Move));
        }
        Assert.True(bundle.Game.IsGoal(s));
    }

    [Fact]
    public void 零步解_唯一显然最短的场景()
    {
        var level = new SyntheticLevel(0, 0, new[] { (0, 1) });
        var result = Greedy.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));
        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Empty(result.Solution);
    }

    [Fact]
    public void 穷尽后_可证无解且图完整()
    {
        // 两连通分量：目标不可达，置换表覆盖全部可达状态后栈耗尽
        var level = new SyntheticLevel(0, 6, new[] { (0, 1), (1, 2), (2, 0), (5, 6) });
        var result = Greedy.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.UnsolvableProven, result.Status);
        Assert.True(result.GraphComplete);
        Assert.Equal(TerminationReason.FrontierEmpty, result.TerminationReason);
    }

    [Fact]
    public void 预算耗尽_未知不冒充无解()
    {
        var edges = Enumerable.Range(0, 300_000).Select(i => (i, i + 1)).ToArray();
        var level = new SyntheticLevel(0, 300_000, edges);
        var result = Greedy.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level)
        {
            TimeBudget = TimeSpan.FromMilliseconds(100)
        });

        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.NotEqual(SolveStatus.UnsolvableProven, result.Status);
    }

    [Fact]
    public void 未声明启发式_明确拒绝()
    {
        var adapter = new PurityAdapter(PurityDefect.None);
        var bundle = new AdapterBundle<PurityLevel, PurityState, PurityMove>("p", "0", adapter, adapter);
        var result = new GreedyDfsSolver<PurityLevel, PurityState, PurityMove>()
            .Solve(bundle, new SolveRequest<PurityLevel>(new PurityLevel(8, 5)));

        Assert.Equal(SolveStatus.Error, result.Status);
        Assert.Contains("IHeuristicAdapter", result.ErrorMessage);
    }

    [Fact]
    public void 非法关卡_Invalid上报()
    {
        var level = new SyntheticLevel(42, 3, new[] { (0, 1), (1, 3) });   // 起点 42 不在图中
        var result = Greedy.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));
        Assert.Equal(SolveStatus.Invalid, result.Status);
    }
}
