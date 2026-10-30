using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// BFS 搜索策略：等成本动作下的最短路；前沿耗尽才可证模型内无解（求解内核 §5）
// 默认遇到首个目标即停（早停，graphComplete=false）；FullEnumeration 继续至前沿耗尽
// 铁律：预算耗尽/取消/异常一律如实上报（ResourceLimit/Cancelled/Error），绝不冒充无解
// 状态去重（visited）由内核持有，适配层只提供 StateKey——搜索正确性的单点收在内核
public sealed class BfsSolver<TLevel, TState, TMove> : ISearchStrategy<TLevel, TState, TMove>
{
    // 单个已访问状态的内存近似成本（键字符串+状态对象+父记录的粗略上界），仅用于内存预算的近似判定
    private const long EstimatedBytesPerState = 512;

    #region 求解入口

    // 执行一次求解：加载关卡 → BFS 主循环 → 按五维度组装结果
    public SolveResult Solve(AdapterBundle<TLevel, TState, TMove> bundle, SolveRequest<TLevel> request)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // 关卡解析：非法数据按契约抛 ArgumentException → Invalid；其余异常 → Error
            var initialState = bundle.Game.LoadLevel(request.Level);
            return Search(bundle, request, initialState, sw);
        }
        catch (ArgumentException ex)
        {
            return BuildResult(SolveStatus.Invalid, Optimality.NotApplicable, false,
                TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix, [], null, ex.Message, sw);
        }
        catch (Exception ex)
        {
            // 适配层/编码异常：中止并上报，不靠继续搜索掩盖
            return BuildResult(SolveStatus.Error, Optimality.NotApplicable, false,
                TerminationReason.AdapterError, MetricsScope.ExpandedPrefix, [], null, ex.Message, sw);
        }
    }

    #endregion

    #region BFS 主循环

    // BFS 主体：按层扩展；每轮先查取消与三项预算再展开
    private SolveResult Search(
        AdapterBundle<TLevel, TState, TMove> bundle,
        SolveRequest<TLevel> request,
        TState initialState,
        Stopwatch sw)
    {
        var adapter = bundle.Game;
        var visited = new HashSet<string>();                   // 已入队状态键（入队即去重）
        var parents = new Dictionary<string, (string Key, TState State, TMove Move)>();   // 路径重建用父记录
        var queue = new Queue<(TState State, string Key)>();   // 前沿队列
        long expanded = 0;                                     // 已展开（出队并生成后继）的状态数
        long generated = 0;                                    // 累计生成的后继数（分支统计分母）
        long frontierPeak = 0;                                 // 前沿队列长度峰值
        string? foundKey = null;                               // 首个命中的目标键

        var initialKey = adapter.StateKey(initialState);
        // 初始即目标：零步解（求解内核 §5）
        if (adapter.IsGoal(initialState))
        {
            foundKey = initialKey;
            if (!request.FullEnumeration)
            {
                return BuildResult(SolveStatus.Solved, Optimality.UnitCostShortest, false,
                    TerminationReason.GoalFound, MetricsScope.ExpandedPrefix,
                    [], Counters(visited, 1, 0, 0, 0, sw, 0), null, sw);
            }
        }

        visited.Add(initialKey);
        queue.Enqueue((initialState, initialKey));

        while (queue.Count > 0)
        {
            // 取消检查优先于一切预算
            if (request.CancellationToken.IsCancellationRequested)
            {
                return Interrupted(bundle, request, foundKey, parents, initialKey,
                    SolveStatus.Cancelled, TerminationReason.UserCancelled,
                    "用户取消", sw, visited, expanded, generated, frontierPeak);
            }
            // 时间预算：尚未得出结论时耗尽 = 未知，不得报无解
            if (request.TimeBudget is { } tb && sw.Elapsed >= tb)
            {
                return Interrupted(bundle, request, foundKey, parents, initialKey,
                    SolveStatus.ResourceLimit, TerminationReason.TimeLimit,
                    $"时间预算耗尽（{tb.TotalMilliseconds:F0}ms）", sw, visited, expanded, generated, frontierPeak);
            }
            // 节点预算：限制的是已展开状态数
            if (request.NodeBudget is { } nb && expanded >= nb)
            {
                return Interrupted(bundle, request, foundKey, parents, initialKey,
                    SolveStatus.ResourceLimit, TerminationReason.NodeLimit,
                    $"节点预算耗尽（{nb}）", sw, visited, expanded, generated, frontierPeak);
            }
            // 内存预算：按状态数近似判定（详见 EstimatedBytesPerState 注释）
            if (request.MemoryBudgetBytes is { } mb && visited.Count > mb / EstimatedBytesPerState)
            {
                return Interrupted(bundle, request, foundKey, parents, initialKey,
                    SolveStatus.ResourceLimit, TerminationReason.MemoryLimit,
                    $"内存预算近似耗尽（visited={visited.Count}）", sw, visited, expanded, generated, frontierPeak);
            }

            var (state, key) = queue.Dequeue();
            expanded++;

            // 目标在出队时判定：FIFO 保证等成本最短
            if (foundKey is null && adapter.IsGoal(state))
            {
                foundKey = key;
                if (!request.FullEnumeration)
                {
                    // 早停：解已取得且最优性已证，图未走完
                    var earlySolution = Reconstruct(bundle, parents, initialKey, foundKey);
                    return BuildResult(SolveStatus.Solved, Optimality.UnitCostShortest, false,
                        TerminationReason.GoalFound, MetricsScope.ExpandedPrefix,
                        earlySolution, Counters(visited, visited.Count, expanded, generated, frontierPeak, sw, earlySolution.Count), null, sw);
                }
            }

            // 生成全部合法后继：顺序由适配层保证稳定，保证搜索确定性
            foreach (var move in adapter.GetLegalMoves(state))
            {
                var nextState = adapter.ApplyMove(state, move);
                generated++;
                var nextKey = adapter.StateKey(nextState);
                if (visited.Add(nextKey))
                {
                    parents[nextKey] = (key, state, move);
                    queue.Enqueue((nextState, nextKey));
                    frontierPeak = Math.Max(frontierPeak, queue.Count);
                }
            }
        }

        // 前沿耗尽：有限可达图已完整遍历
        if (foundKey is not null)
        {
            // 完整枚举模式下取得解并走完图：solved + graphComplete=true + frontier_empty
            var solution = Reconstruct(bundle, parents, initialKey, foundKey);
            return BuildResult(SolveStatus.Solved, Optimality.UnitCostShortest, true,
                TerminationReason.FrontierEmpty, MetricsScope.FullReachableGraph,
                solution, Counters(visited, visited.Count, expanded, generated, frontierPeak, sw, solution.Count), null, sw);
        }
        // 唯一合法的"无解"：完整遍历有限可达图且未发现目标（限定在已声明规则与资源条件内）
        return BuildResult(SolveStatus.UnsolvableProven, Optimality.NotApplicable, true,
            TerminationReason.FrontierEmpty, MetricsScope.FullReachableGraph,
            [], Counters(visited, visited.Count, expanded, generated, frontierPeak, sw, 0), null, sw);
    }

    #endregion

    #region 中断与结果组装

    // 中断路径（取消/预算耗尽）：完整枚举模式下已取得解则保留 solved，图完整性降为 false 并记录终止原因
    private SolveResult Interrupted(
        AdapterBundle<TLevel, TState, TMove> bundle,
        SolveRequest<TLevel> request,
        string? foundKey,
        Dictionary<string, (string Key, TState State, TMove Move)> parents,
        string initialKey,
        SolveStatus status,
        TerminationReason reason,
        string message,
        Stopwatch sw,
        HashSet<string> visited,
        long expanded, long generated, long frontierPeak)
    {
        if (foundKey is not null && request.FullEnumeration)
        {
            // 已有解的完整图分析被预算打断：不能把已知有解降成未知/无解（求解内核 §5）
            var solution = Reconstruct(bundle, parents, initialKey, foundKey);
            return BuildResult(SolveStatus.Solved, Optimality.UnitCostShortest, false,
                reason, MetricsScope.ExpandedPrefix,
                solution, Counters(visited, visited.Count, expanded, generated, frontierPeak, sw, solution.Count), message, sw);
        }
        return BuildResult(status, Optimality.NotApplicable, false,
            reason, MetricsScope.ExpandedPrefix,
            [], Counters(visited, visited.Count, expanded, generated, frontierPeak, sw, 0), message, sw);
    }

    // 沿父记录回溯重建操作序列（含每步前状态，供 DescribeMove 与编码）
    private static List<MoveRecord> Reconstruct(
        AdapterBundle<TLevel, TState, TMove> bundle,
        Dictionary<string, (string Key, TState State, TMove Move)> parents,
        string initialKey,
        string foundKey)
    {
        var chain = new List<(string Key, TState State, TMove Move)>();
        var key = foundKey;
        // 回溯到初始键为止；初始即目标时链为空 = 零步解
        while (!string.Equals(key, initialKey, StringComparison.Ordinal) && parents.TryGetValue(key, out var p))
        {
            chain.Add(p);
            key = p.Key;
        }
        chain.Reverse();
        // 结构化动作 + 展示文本：DescribeMove 用每步的前状态，与搜索时语义一致
        return chain.Select(step => new MoveRecord(
            bundle.Execution.EncodeMove(step.Move),
            bundle.Game.DescribeMove(step.State, step.Move))).ToList();
    }

    // 组装最终结果
    private static SolveResult BuildResult(
        SolveStatus status, Optimality optimality, bool graphComplete,
        TerminationReason reason, MetricsScope scope,
        IReadOnlyList<MoveRecord> solution, SearchMetrics? metrics,
        string? errorMessage, Stopwatch sw)
    {
        sw.Stop();
        return new SolveResult(status, optimality, graphComplete, reason, scope, solution, metrics, errorMessage);
    }

    // 计数器打包为搜索元数据
    private static SearchMetrics Counters(
        HashSet<string> visited, long uniqueOverride,
        long expanded, long generated, long frontierPeak,
        Stopwatch sw, int solutionLength)
    {
        var avgBranching = expanded > 0 ? (double)generated / expanded : 0;   // 已展开状态的平均分支
        var unique = uniqueOverride >= 0 ? uniqueOverride : visited.Count;    // 短路路径允许显式指定已访问数
        return new SearchMetrics(expanded, unique, frontierPeak, sw.ElapsedMilliseconds, solutionLength, avgBranching);
    }

    #endregion
}
