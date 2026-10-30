using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;

namespace LevelForge.Kernel.Executor;

// 执行池：单关求解与批量执行的统一入口（求解内核 §2 Executor 层；批量生产与全量回归共用）
// 适配层为纯函数（契约铁律），并行安全；单项内核级异常被捕获转为 Error 结果，不中断整批
public sealed class SolveExecutor
{
    #region 单关求解

    // 单关求解：默认 BFS 策略，可注入其他策略实现（策略模式换算法不动调用方）
    public SolveResult Solve<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        SolveRequest<TLevel> request,
        ISearchStrategy<TLevel, TState, TMove>? strategy = null)
    {
        return SafeSolve(bundle, request, strategy);
    }

    #endregion

    #region 批量求解

    // 批量求解：默认顺序执行；maxDegreeOfParallelism > 1 时并行调度
    // 结果按下标与输入请求一一对应，与执行顺序无关
    public IReadOnlyList<SolveResult> SolveAll<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        IReadOnlyList<SolveRequest<TLevel>> requests,
        int? maxDegreeOfParallelism = null,
        ISearchStrategy<TLevel, TState, TMove>? strategy = null)
    {
        var results = new SolveResult[requests.Count];
        if (maxDegreeOfParallelism is > 1)
        {
            // 固定并发窗口 + 信号量限流（表现层_批量生产：并发默认 4，可调）
            var throttle = new SemaphoreSlim(maxDegreeOfParallelism.Value, maxDegreeOfParallelism.Value);
            Parallel.For(0, requests.Count, i =>
            {
                throttle.Wait();
                try { results[i] = SafeSolve(bundle, requests[i], strategy); }
                finally { throttle.Release(); }
            });
        }
        else
        {
            // 顺序执行：批量回归等无需并发的场景
            for (var i = 0; i < requests.Count; i++)
            {
                results[i] = SafeSolve(bundle, requests[i], strategy);
            }
        }
        return results;
    }

    #endregion

    #region 内部工具

    // 单次求解的防御包装：内核级异常（非适配层已捕获异常）转 Error 结果并保留堆栈信息
    private static SolveResult SafeSolve<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        SolveRequest<TLevel> request,
        ISearchStrategy<TLevel, TState, TMove>? strategy)
    {
        try
        {
            var solver = strategy ?? new BfsSolver<TLevel, TState, TMove>();
            return solver.Solve(bundle, request);
        }
        catch (Exception ex)
        {
            // 走到这里说明异常来自策略/内核自身而非适配层（适配层异常已在策略内转为结果）
            return new SolveResult(
                SolveStatus.Error, Optimality.NotApplicable, false,
                TerminationReason.AdapterError, MetricsScope.ExpandedPrefix,
                [], null, $"执行器捕获未处理异常：{ex.Message}");
        }
    }

    #endregion
}
