using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Search;

// A* 策略测试：与 BFS 语义等价对拍（h=0）+ 非零启发式 + 能力缺失拒绝 + 预算诚实性
public class AStarSolverTests
{
    private static readonly BfsSolver<SyntheticLevel, SyntheticState, SyntheticMove> Bfs = new();
    private static readonly AStarSolver<SyntheticLevel, SyntheticState, SyntheticMove> AStar = new();

    // 合成适配器 h≡0（可采纳）：A* 退化为 Dijkstra，与 BFS 做语义等价对拍；
    // 非零启发式的真实效果由滑动猪真实关测试验证（h=剩余非笼实体数）

    [Fact]
    public void 零启发式_与BFS结论完全一致()
    {
        var cases = new[]
        {
            new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) }),      // 最短路
            new SyntheticLevel(0, 0, new[] { (0, 1) }),                               // 零步解
            new SyntheticLevel(0, 6, new[] { (0, 1), (1, 2), (2, 0), (5, 6) }),       // 可证无解
            new SyntheticLevel(0, 3, new[] { (0, 1), (1, 3) })                        // 简单两步
        };
        foreach (var level in cases)
        {
            var bundle = SyntheticGraphAdapter.CreateBundle();
            var rb = Bfs.Solve(bundle, new SolveRequest<SyntheticLevel>(level));
            var ra = AStar.Solve(bundle, new SolveRequest<SyntheticLevel>(level));
            Assert.Equal(rb.Status, ra.Status);
            Assert.Equal(rb.Optimality, ra.Optimality);
            Assert.Equal(rb.GraphComplete, ra.GraphComplete);
            Assert.Equal(rb.Solution.Count, ra.Solution.Count);
        }
    }

    [Fact]
    public void 完整枚举模式_图完整且结论保留()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = AStar.Solve(bundle, new SolveRequest<SyntheticLevel>(level) { FullEnumeration = true });

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.True(result.GraphComplete);
        Assert.Equal(2, result.Solution.Count);
    }

    [Fact]
    public void 已得解被预算打断_保留solved不降级()
    {
        // 目标近在咫尺（1 步），其后长尾；预算 2 个节点：初始与目标弹出后中断，解已取得但图未走完
        var level = new SyntheticLevel(0, 1, new[] { (0, 1), (1, 2), (2, 3), (3, 4) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = AStar.Solve(bundle, new SolveRequest<SyntheticLevel>(level)
        {
            FullEnumeration = true,
            NodeBudget = 2
        });

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.False(result.GraphComplete);
        Assert.Equal(TerminationReason.NodeLimit, result.TerminationReason);
    }

    [Fact]
    public void 预算耗尽_未知不冒充无解()
    {
        var edges = Enumerable.Range(0, 300_000).Select(i => (i, i + 1)).ToArray();
        var level = new SyntheticLevel(0, 300_000, edges);
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = AStar.Solve(bundle, new SolveRequest<SyntheticLevel>(level)
        {
            TimeBudget = TimeSpan.FromMilliseconds(200)
        });

        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.Equal(TerminationReason.TimeLimit, result.TerminationReason);
    }

    // 诚实指标防线：平均分支因子必须由真实生成量算出，不得用 0 冒充"统计过"
    // （曾经 A* 写死 0，导致难度模型把分支特征当成 0 参与评分）
    [Fact]
    public void 平均分支因子_由真实后继数算出而非占位0()
    {
        var level = new SyntheticLevel(0, 5, new[] { (0, 1), (0, 2), (1, 3), (2, 3), (3, 4), (4, 5) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = AStar.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.NotNull(result.Metrics);
        Assert.True(result.Metrics!.StatesExplored > 0);
        Assert.True(result.Metrics.AvgBranchingFactor > 0,
            $"平均分支因子应为正数，实际 {result.Metrics.AvgBranchingFactor}");
    }

    [Fact]
    public void 未声明启发式_明确拒绝()
    {
        // 不实现 IHeuristicAdapter 的包装（PurityAdapter 不实现启发式）
        var adapter = new PurityAdapter(PurityDefect.None);
        var bundle = new AdapterBundle<PurityLevel, PurityState, PurityMove>("p", "0", adapter, adapter);
        var result = new AStarSolver<PurityLevel, PurityState, PurityMove>()
            .Solve(bundle, new SolveRequest<PurityLevel>(new PurityLevel(8, 5)));

        Assert.Equal(SolveStatus.Error, result.Status);
        Assert.Contains("IHeuristicAdapter", result.ErrorMessage);
    }
}
