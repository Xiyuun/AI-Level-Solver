using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Search;

// BFS 求解器内核自测（求解内核 §5/§9.5、验收项 A02）
// 每个用例都对应一条"结论语义"规则：未知不冒充无解、初始即目标零步解、完整枚举才 graphComplete 等
public class BfsSolverTests
{
    private static readonly BfsSolver<SyntheticLevel, SyntheticState, SyntheticMove> Solver = new();

    // 构造图关卡的便捷方法
    private static SyntheticLevel Graph(int start, int goal, params (int From, int To)[] edges)
    {
        return new SyntheticLevel(start, goal, edges);
    }

    #region 已知最短路与零步解

    [Fact]
    public void 菱形图_解长与序列正确_早停未完整()
    {
        var level = Graph(0, 3, (0, 1), (0, 2), (1, 3), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Equal(Optimality.UnitCostShortest, result.Optimality);
        Assert.Equal(TerminationReason.GoalFound, result.TerminationReason);
        // 早停：拿到解但图未走完
        Assert.False(result.GraphComplete);
        Assert.Equal(MetricsScope.ExpandedPrefix, result.MetricsScope);
        Assert.Equal(2, result.Solution.Count);
        // 邻接序决定等长路径中选 0->1->3
        Assert.Equal(["0->1", "1->3"], result.Solution.Select(r => r.MoveText).ToArray());
    }

    [Fact]
    public void 初始即目标_零步解()
    {
        var level = Graph(0, 0, (0, 1));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Empty(result.Solution);                                // 零步解
        Assert.Equal(TerminationReason.GoalFound, result.TerminationReason);
        Assert.False(result.GraphComplete);
    }

    #endregion

    #region 无解与环路

    [Fact]
    public void 目标不可达_完整遍历后可证无解()
    {
        // 两个连通分量：0-1-2 环 与 5→6；目标 6 从起点不可达
        var level = Graph(0, 6, (0, 1), (1, 2), (2, 0), (5, 6));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.UnsolvableProven, result.Status);
        Assert.Equal(TerminationReason.FrontierEmpty, result.TerminationReason);
        Assert.True(result.GraphComplete);                            // 前沿耗尽 = 有限可达图完整
        Assert.Equal(MetricsScope.FullReachableGraph, result.MetricsScope);
        // 可达分量仅 {0,1,2}
        Assert.Equal(3, result.Metrics!.UniqueStates);
    }

    [Fact]
    public void 有环图_访问去重不死循环()
    {
        // 0→1→2→0 环 + 2→3 出口；无去重将无限循环
        var level = Graph(0, 3, (0, 1), (1, 2), (2, 0), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Equal(3, result.Solution.Count);
        Assert.Equal(4, result.Metrics!.UniqueStates);
    }

    [Fact]
    public void 完整枚举模式_有解且图完整()
    {
        var level = Graph(0, 3, (0, 1), (0, 2), (1, 3), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level) { FullEnumeration = true });

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.True(result.GraphComplete);                            // 走完图
        Assert.Equal(TerminationReason.FrontierEmpty, result.TerminationReason);
        Assert.Equal(MetricsScope.FullReachableGraph, result.MetricsScope);
        Assert.Equal(4, result.Metrics!.UniqueStates);
    }

    #endregion

    #region 预算、取消与异常（诚实结论的核心用例）

    [Fact]
    public void 节点预算耗尽_未知不冒充无解()
    {
        // 0→1→…→99 长链，只允许展开 5 个节点
        var edges = Enumerable.Range(0, 99).Select(i => (i, i + 1)).ToArray();
        var level = Graph(0, 99, edges);
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level) { NodeBudget = 5 });

        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.Equal(TerminationReason.NodeLimit, result.TerminationReason);
        Assert.False(result.GraphComplete);
        // 不能是无解——预算内未得出结论
        Assert.NotEqual(SolveStatus.UnsolvableProven, result.Status);
    }

    [Fact]
    public void 时间预算耗尽_未知不冒充无解()
    {
        // 三十万节点长链 + 1ms 预算：BFS 循环必然超时（每轮含哈希集合与字典操作）
        var edges = Enumerable.Range(0, 300_000).Select(i => (i, i + 1)).ToArray();
        var level = Graph(0, 300_000, edges);
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level)
        {
            TimeBudget = TimeSpan.FromMilliseconds(1)
        });

        Assert.Equal(SolveStatus.ResourceLimit, result.Status);
        Assert.Equal(TerminationReason.TimeLimit, result.TerminationReason);
    }

    [Fact]
    public void 取消_如实上报Cancelled()
    {
        var level = Graph(0, 3, (0, 1), (1, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var cts = new CancellationTokenSource();
        cts.Cancel();                                                 // 预先取消
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level)
        {
            CancellationToken = cts.Token
        });

        Assert.Equal(SolveStatus.Cancelled, result.Status);
        Assert.Equal(TerminationReason.UserCancelled, result.TerminationReason);
    }

    [Fact]
    public void 完整枚举中已得解_预算耗尽保留solved()
    {
        // 目标 1 很近，其后还有长尾；预算 3 个节点：解已取得但图未走完
        var level = Graph(0, 1, (0, 1), (1, 2), (2, 3), (3, 4), (4, 5));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level)
        {
            FullEnumeration = true,
            NodeBudget = 3
        });

        // 已知有解不能因分析被打断而降级为未知/无解（求解内核 §5）
        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Equal(Optimality.UnitCostShortest, result.Optimality);
        Assert.False(result.GraphComplete);
        Assert.Equal(TerminationReason.NodeLimit, result.TerminationReason);
        Assert.Single(result.Solution);
    }

    [Fact]
    public void 适配层ApplyMove异常_Error上报()
    {
        var level = Graph(0, 3, (0, 1), (0, 2), (1, 3), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle(SyntheticFailure.ThrowOnApply);
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Error, result.Status);
        Assert.Equal(TerminationReason.AdapterError, result.TerminationReason);
        Assert.Contains("注入", result.ErrorMessage);
    }

    [Fact]
    public void 非法关卡_Invalid上报()
    {
        // 起点 42 不在图的节点集中 → 适配层按契约抛 ArgumentException
        var level = Graph(42, 3, (0, 1), (1, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(SolveStatus.Invalid, result.Status);
        Assert.Equal(TerminationReason.InvalidInput, result.TerminationReason);
    }

    #endregion

    #region 元数据与确定性

    [Fact]
    public void 搜索元数据_计数正确()
    {
        var level = Graph(0, 3, (0, 1), (0, 2), (1, 3), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        // 出队判定 0,1,2,3 共 4 个状态（目标在出队时判定）；访问去重 4 个；生成后继 4 条（含重复的 2→3）
        Assert.Equal(4, result.Metrics!.StatesExplored);
        Assert.Equal(4, result.Metrics.UniqueStates);
        Assert.Equal(2, result.Metrics.FrontierPeak);
        Assert.Equal(2, result.Metrics.SolutionLength);
        Assert.Equal(1.0, result.Metrics.AvgBranchingFactor, precision: 10);
    }

    [Fact]
    public void 同输入两次求解_结果一致()
    {
        // 确定性基础：等输入等输出（完整确定性双跑测试在合规检查里做轨迹哈希级验证）
        var level = Graph(0, 3, (0, 1), (0, 2), (1, 3), (2, 3));
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var r1 = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));
        var r2 = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        Assert.Equal(r1.Status, r2.Status);
        Assert.Equal(r1.TerminationReason, r2.TerminationReason);
        Assert.Equal(r1.Solution.Select(s => s.MoveText), r2.Solution.Select(s => s.MoveText));
        Assert.Equal(r1.Metrics!.UniqueStates, r2.Metrics!.UniqueStates);
        Assert.Equal(r1.Metrics.SolutionLength, r2.Metrics.SolutionLength);
    }

    #endregion
}
