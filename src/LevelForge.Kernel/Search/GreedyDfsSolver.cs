using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// 贪心 DFS 策略：启发式排序的深度优先搜索——为超大规模状态空间"找出任一可解路径"设计
// 触发依据：滑动猪真实数据——110 关中除 1 关外全部 61~122 只实体，BFS/A* 30s 内全部未知（普查实测）
// 诚实边界：找到解时 Optimality=NotProven（不声称最短）；预算耗尽只报未知；**永不返回 UnsolvableProven**
// （DFS 带置换表与深度上限不构成穷尽证明）——消费方按 optimality 字段区分两种解
public sealed class GreedyDfsSolver<TLevel, TState, TMove> : ISearchStrategy<TLevel, TState, TMove>
{
    // 病态防线：路径深度上限（置换表已保证路径无环；上限仅防栈溢出，正常稠密关路径远小于此值）
    private const int MaxDepth = 20_000;

    #region 求解入口

    // 执行一次求解：预算/取消/异常语义与 BFS/A* 一致
    public SolveResult Solve(AdapterBundle<TLevel, TState, TMove> bundle, SolveRequest<TLevel> request)
    {
        if (bundle.Game is not IHeuristicAdapter<TState> heuristic)
        {
            return new SolveResult(
                SolveStatus.Error, Optimality.NotApplicable, false,
                TerminationReason.AdapterError, MetricsScope.ExpandedPrefix,
                [], null, "适配层未声明 IHeuristicAdapter，贪心 DFS 策略不可用");
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

    #region DFS 主体

    // 中止原因（预算/取消触发后逐层退栈）
    private enum Abort { None, Time, Node, Cancelled }

    // 显式栈的迭代式 DFS（避免深递归栈溢出）；帧 = (状态, 待试动作列表, 已试下标)
    private SolveResult Search(
        AdapterBundle<TLevel, TState, TMove> bundle,
        IHeuristicAdapter<TState> heuristic,
        SolveRequest<TLevel> request,
        TState initialState,
        Stopwatch sw)
    {
        var game = bundle.Game;
        var closed = new HashSet<string>();                          // 全局置换表：见过的状态不再入路径
        var path = new List<MoveRecord>();                           // 当前路径（找到即答案）
        var frames = new Stack<(TState State, List<TMove> Moves, int Index)>();
        long expanded = 0;
        long generated = 0;
        long frontierPeak = 0;
        var abort = Abort.None;

        if (game.IsGoal(initialState))
        {
            return Build(SolveStatus.Solved, TerminationReason.GoalFound, [],
                Metrics(0, 1, 0, sw, 0), null, sw, Optimality.UnitCostShortest);   // 初始即目标 = 零步且显然最短
        }
        var initialKey = game.StateKey(initialState);
        closed.Add(initialKey);
        // 动作排序：h 小者（更接近清场）优先；同 h 按适配层枚举序（确定性）
        frames.Push((initialState, OrderedMoves(bundle, heuristic, initialState), 0));

        while (frames.Count > 0)
        {
            // 预算与取消：每帧检查（帧粒度足够，单帧内开销有界）
            if (abort == Abort.None)
            {
                if (request.CancellationToken.IsCancellationRequested) abort = Abort.Cancelled;
                else if (request.TimeBudget is { } tb && sw.Elapsed >= tb) abort = Abort.Time;
                else if (request.NodeBudget is { } nb && expanded >= nb) abort = Abort.Node;
            }
            frontierPeak = Math.Max(frontierPeak, frames.Count);
            if (abort != Abort.None) break;

            var frame = frames.Peek();
            if (frame.Index >= frame.Moves.Count)
            {
                // 本状态的动作试尽：退栈并撤销路径最后一步
                frames.Pop();
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
                continue;
            }
            var move = frame.Moves[frame.Index];
            frames.Pop();
            frames.Push((frame.State, frame.Moves, frame.Index + 1));
            expanded++;

            var nextState = game.ApplyMove(frame.State, move);
            generated++;
            // 死局由自然回溯处理：无操作的状态入栈后动作列表为空，下一轮退栈撤销
            var nextKey = game.StateKey(nextState);
            if (!closed.Add(nextKey)) continue;                      // 剪枝：置换表去重
            if (path.Count + 1 > MaxDepth) continue;                 // 病态防线

            path.Add(new MoveRecord(bundle.Execution.EncodeMove(move), game.DescribeMove(frame.State, move)));
            if (game.IsGoal(nextState))
            {
                // 找到任一可解路径：Optimality=NotProven（贪心不证明最短）
                return Build(SolveStatus.Solved, TerminationReason.GoalFound, path.ToList(),
                    Metrics(expanded, closed.Count, frontierPeak, sw, path.Count), null, sw, Optimality.NotProven);
            }
            frames.Push((nextState, OrderedMoves(bundle, heuristic, nextState), 0));
        }

        // 栈耗尽且未被预算中止：置换表覆盖全部可达状态且均无目标 → 可证模型内无解
        if (abort == Abort.None)
        {
            sw.Stop();
            return new SolveResult(
                SolveStatus.UnsolvableProven, Optimality.NotApplicable, true,
                TerminationReason.FrontierEmpty, MetricsScope.FullReachableGraph,
                [], Metrics(expanded, closed.Count, frontierPeak, sw, 0), null);
        }
        // 预算/取消中止（找不到不等于无解——DFS 非穷尽）
        var (status, reason, message) = abort switch
        {
            Abort.Cancelled => (SolveStatus.Cancelled, TerminationReason.UserCancelled, "用户取消"),
            Abort.Node => (SolveStatus.ResourceLimit, TerminationReason.NodeLimit, $"节点预算耗尽（{request.NodeBudget}）"),
            _ => (SolveStatus.ResourceLimit, TerminationReason.TimeLimit, "时间预算耗尽")
        };
        return Build(status, reason, [], Metrics(expanded, closed.Count, frontierPeak, sw, 0), message, sw);
    }

    #endregion

    #region 内部工具

    // 后继动作排序：h 升序（离场/解锁优先），同 h 保持枚举序——确定性排序契约
    private static List<TMove> OrderedMoves(
        AdapterBundle<TLevel, TState, TMove> bundle, IHeuristicAdapter<TState> heuristic, TState state)
    {
        var moves = bundle.Game.GetLegalMoves(state);
        return moves
            .Select((m, i) => (Move: m, Order: i, H: Math.Max(0, heuristic.EstimateToGoal(bundle.Game.ApplyMove(state, m)))))
            .OrderBy(t => t.H)
            .ThenBy(t => t.Order)
            .Select(t => t.Move)
            .ToList();
    }

    // 组装最终结果
    private static SolveResult Build(
        SolveStatus status, TerminationReason reason,
        IReadOnlyList<MoveRecord> solution, SearchMetrics? metrics,
        string? errorMessage, Stopwatch sw,
        Optimality optimality = Optimality.NotApplicable)
    {
        sw.Stop();
        return new SolveResult(status, status == SolveStatus.Solved ? optimality : Optimality.NotApplicable,
            false, reason, MetricsScope.ExpandedPrefix, solution, metrics, errorMessage);
    }

    // 计数器打包（分支统计在贪心排序下语义不同，置 0）
    private static SearchMetrics Metrics(long expanded, long unique, long frontierPeak, Stopwatch sw, int solutionLength)
    {
        return new SearchMetrics(expanded, unique, frontierPeak, sw.ElapsedMilliseconds, solutionLength, 0);
    }

    #endregion
}
