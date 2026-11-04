using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// A* 搜索策略：需要适配层声明 IHeuristicAdapter（可采纳启发式，求解内核 §4 可选能力）
// 单位成本 + 可采纳且一致的启发式 → 首次弹出目标即最少动作解（optimality = UnitCostShortest）
// 触发依据：滑动猪真实关实测 BFS 组合爆炸（内核 §10 优化路径按需触发——数据说话）
// 启发式高估时最优性保证失效——适配层契约已声明该约束，本策略不重验（责任在适配层测试）
public sealed class AStarSolver<TLevel, TState, TMove> : ISearchStrategy<TLevel, TState, TMove>
{
    // 单个已见状态的内存近似成本（键+状态+父记录+队列条目的粗略上界），仅用于内存预算近似判定
    private const long EstimatedBytesPerState = 640;

    #region 求解入口

    // 执行一次求解：预算/取消/异常语义与 BfsSolver 完全一致
    public SolveResult Solve(AdapterBundle<TLevel, TState, TMove> bundle, SolveRequest<TLevel> request)
    {
        // 能力缺失明确拒绝，不静默降级（求解内核 §4 策略选择原则）
        if (bundle.Game is not IHeuristicAdapter<TState> heuristic)
        {
            return new SolveResult(
                SolveStatus.Error, Optimality.NotApplicable, false,
                TerminationReason.AdapterError, MetricsScope.ExpandedPrefix,
                [], null, "适配层未声明 IHeuristicAdapter，A* 策略不可用");
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var initialState = bundle.Game.LoadLevel(request.Level);
            return Search(bundle, heuristic, request, initialState, sw);
        }
        catch (ArgumentException ex)
        {
            return Build(SolveStatus.Invalid, TerminationReason.InvalidInput, [], null, ex.Message, sw);
        }
        catch (Exception ex)
        {
            return Build(SolveStatus.Error, TerminationReason.AdapterError, [], null, ex.Message, sw);
        }
    }

    #endregion

    #region A* 主循环

    // 队列条目：状态 + 键 + g（已走步数）
    private sealed record Entry(TState State, string Key, int G);

    // A* 主体：惰性删除 + 弹出即定序（一致启发式下首次弹出即最优）
    private SolveResult Search(
        AdapterBundle<TLevel, TState, TMove> bundle,
        IHeuristicAdapter<TState> heuristic,
        SolveRequest<TLevel> request,
        TState initialState,
        Stopwatch sw)
    {
        var game = bundle.Game;
        var closed = new HashSet<string>();                        // 已弹出定序的状态键
        var bestG = new Dictionary<string, int>();                 // 已见状态的最优 g（入队时记录）
        var parents = new Dictionary<string, (string Key, TState State, TMove Move)>();   // 路径重建
        var queue = new PriorityQueue<Entry, (int F, int NegG)>(); // f 升序，同 f 时 g 大者优先（更深先探）
        long expanded = 0;
        long generated = 0;
        long frontierPeak = 0;
        string? foundKey = null;

        var initialKey = game.StateKey(initialState);
        var initialH = Math.Max(0, heuristic.EstimateToGoal(initialState));
        if (game.IsGoal(initialState))
        {
            foundKey = initialKey;
            if (!request.FullEnumeration)
            {
                return Build(SolveStatus.Solved, TerminationReason.GoalFound, [],
                    Metrics(0, 0, 1, 0, sw, 0), null, sw);
            }
        }
        bestG[initialKey] = 0;
        queue.Enqueue(new Entry(initialState, initialKey, 0), (initialH, 0));

        while (queue.Count > 0)
        {
            // 取消优先，其次三项预算（语义与 BfsSolver 一致：未知不冒充无解）
            if (request.CancellationToken.IsCancellationRequested)
            {
                return Interrupted(request, foundKey, parents, initialKey, bestG,
                    SolveStatus.Cancelled, TerminationReason.UserCancelled, "用户取消",
                    expanded, generated, frontierPeak, sw);
            }
            if (request.TimeBudget is { } tb && sw.Elapsed >= tb)
            {
                return Interrupted(request, foundKey, parents, initialKey, bestG,
                    SolveStatus.ResourceLimit, TerminationReason.TimeLimit, $"时间预算耗尽（{tb.TotalMilliseconds:F0}ms）",
                    expanded, generated, frontierPeak, sw);
            }
            if (request.NodeBudget is { } nb && expanded >= nb)
            {
                return Interrupted(request, foundKey, parents, initialKey, bestG,
                    SolveStatus.ResourceLimit, TerminationReason.NodeLimit, $"节点预算耗尽（{nb}）",
                    expanded, generated, frontierPeak, sw);
            }
            if (request.MemoryBudgetBytes is { } mb && bestG.Count > mb / EstimatedBytesPerState)
            {
                return Interrupted(request, foundKey, parents, initialKey, bestG,
                    SolveStatus.ResourceLimit, TerminationReason.MemoryLimit, $"内存预算近似耗尽（seen={bestG.Count}）",
                    expanded, generated, frontierPeak, sw);
            }

            var (state, key, g) = queue.Dequeue();
            if (!closed.Add(key)) continue;                        // 惰性删除：重复条目跳过
            expanded++;

            if (foundKey is null && game.IsGoal(state))
            {
                foundKey = key;
                if (!request.FullEnumeration)
                {
                    var early = Reconstruct(bundle, parents, initialKey, foundKey);
                    return Build(SolveStatus.Solved, TerminationReason.GoalFound, early,
                        Metrics(expanded, generated, bestG.Count, frontierPeak, sw, early.Count), null, sw);
                }
            }

            foreach (var move in game.GetLegalMoves(state))
            {
                var nextState = game.ApplyMove(state, move);
                generated++;
                var nextKey = game.StateKey(nextState);
                if (closed.Contains(nextKey)) continue;
                var ng = g + 1;
                if (bestG.TryGetValue(nextKey, out var bg) && bg <= ng) continue;   // 已有更优路径
                bestG[nextKey] = ng;
                parents[nextKey] = (key, state, move);
                var h = Math.Max(0, heuristic.EstimateToGoal(nextState));
                queue.Enqueue(new Entry(nextState, nextKey, ng), (ng + h, -ng));    // 一致性保证 h≥0
                frontierPeak = Math.Max(frontierPeak, queue.Count);
            }
        }

        // 队列耗尽：可达部分完整遍历
        if (foundKey is not null)
        {
            var solution = Reconstruct(bundle, parents, initialKey, foundKey);
            return Build(SolveStatus.Solved, TerminationReason.FrontierEmpty, solution,
                Metrics(expanded, generated, bestG.Count, frontierPeak, sw, solution.Count), null, sw,
                graphComplete: true, scope: MetricsScope.FullReachableGraph);
        }
        return Build(SolveStatus.UnsolvableProven, TerminationReason.FrontierEmpty, [],
            Metrics(expanded, generated, bestG.Count, frontierPeak, sw, 0), null, sw,
            graphComplete: true, scope: MetricsScope.FullReachableGraph);
    }

    #endregion

    #region 中断与结果组装

    // 中断路径：完整枚举模式下已得解保留 solved（与 BfsSolver 同一规则）
    private SolveResult Interrupted(
        SolveRequest<TLevel> request, string? foundKey,
        Dictionary<string, (string Key, TState State, TMove Move)> parents, string initialKey,
        Dictionary<string, int> bestG,
        SolveStatus status, TerminationReason reason, string message,
        long expanded, long generated, long frontierPeak, Stopwatch sw)
    {
        if (foundKey is not null && request.FullEnumeration)
        {
            var solution = ReconstructFrom(parents, initialKey, foundKey);
            return Build(SolveStatus.Solved, reason, solution,
                Metrics(expanded, generated, bestG.Count, frontierPeak, sw, solution.Count), message, sw);
        }
        return Build(status, reason, [], Metrics(expanded, generated, bestG.Count, frontierPeak, sw, 0), message, sw);
    }

    // 沿父记录重建（含每步前状态）
    private static List<MoveRecord> Reconstruct(
        AdapterBundle<TLevel, TState, TMove> bundle,
        Dictionary<string, (string Key, TState State, TMove Move)> parents,
        string initialKey, string foundKey)
    {
        return ReconstructFrom(parents, initialKey, foundKey, bundle);
    }

    // 重建主体（bundle 可空：中断路径无需编码动作）
    private static List<MoveRecord> ReconstructFrom(
        Dictionary<string, (string Key, TState State, TMove Move)> parents,
        string initialKey, string foundKey,
        AdapterBundle<TLevel, TState, TMove>? bundle = null)
    {
        var chain = new List<(string Key, TState State, TMove Move)>();
        var key = foundKey;
        while (!string.Equals(key, initialKey, StringComparison.Ordinal) && parents.TryGetValue(key!, out var p))
        {
            chain.Add(p);
            key = p.Key;
        }
        chain.Reverse();
        if (bundle is null) return [];
        return chain.Select(step => new MoveRecord(
            bundle.Execution.EncodeMove(step.Move),
            bundle.Game.DescribeMove(step.State, step.Move))).ToList();
    }

    // 组装最终结果
    private static SolveResult Build(
        SolveStatus status, TerminationReason reason,
        IReadOnlyList<MoveRecord> solution, SearchMetrics? metrics,
        string? errorMessage, Stopwatch sw,
        bool graphComplete = false, MetricsScope scope = MetricsScope.ExpandedPrefix)
    {
        sw.Stop();
        var optimality = status == SolveStatus.Solved ? Optimality.UnitCostShortest : Optimality.NotApplicable;
        return new SolveResult(status, optimality, graphComplete, reason, scope, solution, metrics, errorMessage);
    }

    // 计数器打包：平均分支 = 累计生成后继数 / 已展开状态数（与 BfsSolver 同一口径）
    // A* 生成的后继含"已被 close 或已有更优 g"的重复项，但它们同样是该状态的真实分支尝试，故计入分母外的分子
    private static SearchMetrics Metrics(long expanded, long generated, long unique, long frontierPeak, Stopwatch sw, int solutionLength)
    {
        var avgBranching = expanded > 0 ? (double)generated / expanded : 0;
        return new SearchMetrics(expanded, unique, frontierPeak, sw.ElapsedMilliseconds, solutionLength, avgBranching);
    }

    #endregion
}
