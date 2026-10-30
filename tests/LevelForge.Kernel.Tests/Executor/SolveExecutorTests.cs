using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Executor;

// 执行池测试：单关/批量/并行/异常隔离
public class SolveExecutorTests
{
    // 总是抛异常的策略：验证执行器的防御包装
    private sealed class ThrowingStrategy : ISearchStrategy<SyntheticLevel, SyntheticState, SyntheticMove>
    {
        public SolveResult Solve(AdapterBundle<SyntheticLevel, SyntheticState, SyntheticMove> bundle, SolveRequest<SyntheticLevel> request)
        {
            throw new NullReferenceException("内核级异常注入");
        }
    }

    // 无状态合成适配器：一个包可解任意关卡
    private static readonly AdapterBundle<SyntheticLevel, SyntheticState, SyntheticMove> Bundle =
        SyntheticGraphAdapter.CreateBundle();

    private readonly SolveExecutor _executor = new();

    // 构造三个代表请求：有解、无解、预算受限
    private static List<SolveRequest<SyntheticLevel>> SampleRequests()
    {
        return new List<SolveRequest<SyntheticLevel>>
        {
            new(new SyntheticLevel(0, 3, new[] { (0, 1), (1, 3) })),
            new(new SyntheticLevel(0, 6, new[] { (0, 1), (1, 2), (2, 0), (5, 6) })),
            new(new SyntheticLevel(0, 99, Enumerable.Range(0, 99).Select(i => (i, i + 1)).ToArray())) { NodeBudget = 5 }
        };
    }

    [Fact]
    public void 单关求解_默认BFS策略()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (1, 3) });
        var result = _executor.Solve(Bundle, new SolveRequest<SyntheticLevel>(level));
        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Equal(2, result.Solution.Count);
    }

    [Fact]
    public void 批量顺序执行_结果与输入一一对应()
    {
        var results = _executor.SolveAll(Bundle, SampleRequests());

        Assert.Equal(3, results.Count);
        Assert.Equal(SolveStatus.Solved, results[0].Status);
        Assert.Equal(SolveStatus.UnsolvableProven, results[1].Status);
        Assert.Equal(SolveStatus.ResourceLimit, results[2].Status);
    }

    [Fact]
    public void 批量并行执行_结果与顺序执行一致且按原序()
    {
        // 同一批请求并行 4 路：结论必须与顺序执行完全一致（适配层纯函数契约）
        var sequential = _executor.SolveAll(Bundle, SampleRequests());
        var parallel = _executor.SolveAll(Bundle, SampleRequests(), maxDegreeOfParallelism: 4);

        Assert.Equal(
            sequential.Select(r => (r.Status, r.TerminationReason, r.Metrics?.SolutionLength ?? 0)),
            parallel.Select(r => (r.Status, r.TerminationReason, r.Metrics?.SolutionLength ?? 0)));
    }

    [Fact]
    public void 单项内核异常_不中断整批()
    {
        // 全部被防御包装转为 Error，批处理本身不抛出
        var results = _executor.SolveAll(Bundle, SampleRequests(), strategy: new ThrowingStrategy());

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(SolveStatus.Error, r.Status));
        Assert.All(results, r => Assert.Contains("执行器捕获", r.ErrorMessage));
    }
}
