using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Search;

// 束搜索策略测试：可解路径、NotProven 最优性、预算与束枯竭的诚实语义、能力缺失拒绝
// 重点防线：束枯竭**不得**返回 unsolvable_proven（被剪掉的分支没被考察，那不是证明）
public class BeamSearchSolverTests
{
    private static readonly BeamSearchSolver<SyntheticLevel, SyntheticState, SyntheticMove> Beam = new();

    [Fact]
    public void 可解图_找到路径且回放通关_NotProven最优性()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Beam.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        // 束宽剪枝意味着不保证最短，即使碰巧最短也如实标 NotProven
        Assert.Equal(Optimality.NotProven, result.Optimality);
        Assert.False(result.GraphComplete);
        // 独立回放：操作序列必须真的能通关
        var state = bundle.Game.LoadLevel(level);
        foreach (var record in result.Solution)
        {
            state = bundle.Game.ApplyMove(state, bundle.Execution.DecodeMove(record.Move));
        }
        Assert.True(bundle.Game.IsGoal(state));
    }

    [Fact]
    public void 零步解_初始即目标()
    {
        var level = new SyntheticLevel(0, 0, new[] { (0, 1) });
        var result = Beam.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Empty(result.Solution);
        Assert.Equal(Optimality.UnitCostShortest, result.Optimality);
    }

    [Fact]
    public void 束枯竭_绝不冒充无解证明()
    {
        // 目标不可达：束会逐层缩小直至枯竭
        var level = new SyntheticLevel(0, 6, new[] { (0, 1), (1, 2), (2, 0), (5, 6) });
        var result = Beam.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));

        Assert.NotEqual(SolveStatus.UnsolvableProven, result.Status);
        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.False(result.GraphComplete);
        // 文案必须说清楚"这不是无解证明"
        Assert.Contains("不构成无解证明", result.ErrorMessage);
    }

    [Fact]
    public void 预算耗尽_未知不冒充无解()
    {
        var edges = Enumerable.Range(0, 300_000).Select(i => (i, i + 1)).ToArray();
        var level = new SyntheticLevel(0, 300_000, edges);
        var result = Beam.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level)
        {
            TimeBudget = TimeSpan.FromMilliseconds(150)
        });

        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.Equal(TerminationReason.TimeLimit, result.TerminationReason);
    }

    [Fact]
    public void 未声明启发式_明确拒绝()
    {
        var adapter = new PurityAdapter(PurityDefect.None);
        var bundle = new AdapterBundle<PurityLevel, PurityState, PurityMove>("p", "0", adapter, adapter);
        var result = new BeamSearchSolver<PurityLevel, PurityState, PurityMove>()
            .Solve(bundle, new SolveRequest<PurityLevel>(new PurityLevel(8, 5)));

        Assert.Equal(SolveStatus.Error, result.Status);
        Assert.Contains("IHeuristicAdapter", result.ErrorMessage);
    }

    [Fact]
    public void 非法关卡_Invalid上报()
    {
        var level = new SyntheticLevel(42, 3, new[] { (0, 1), (1, 3) });   // 起点 42 不在图中
        var result = Beam.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Invalid, result.Status);
    }

    [Fact]
    public void 指标真实_展开数与分支因子都由实际搜索产出()
    {
        var level = new SyntheticLevel(0, 5, new[] { (0, 1), (0, 2), (1, 3), (2, 3), (3, 4), (4, 5) });
        var result = Beam.Solve(SyntheticGraphAdapter.CreateBundle(), new SolveRequest<SyntheticLevel>(level));

        Assert.NotNull(result.Metrics);
        Assert.True(result.Metrics!.StatesExplored > 0);
        Assert.True(result.Metrics.AvgBranchingFactor > 0);
    }
}
