using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// 贪心 DFS 策略：启发式排序的深度优先搜索——为超大规模状态空间"找出任一可解路径"设计
// 触发依据：滑动猪真实数据——110 关中除 1 关外全部 61~122 只实体，BFS/A* 30s 内全部未知（普查实测）
// 诚实边界：找到解时 Optimality=NotProven（不声称最短）；预算耗尽只报未知；只有置换表覆盖全部
// 可达状态（栈耗尽）才返回 UnsolvableProven——消费方按 optimality 字段区分两种解
public sealed class GreedyDfsSolver<TLevel, TState, TMove> : ISearchStrategy<TLevel, TState, TMove>
{
    #region 配置与探针

    // 搜索偏好（构造期注入；默认全关=原行为，既有调用方不受影响）
    // 诊断依据：滑动猪稠密关 minH=21/90——级联可用、尾部等值平原盲搜
    public sealed record Options(
        bool MobilityTiebreak = false,       // 同 h 平级时优先"后继合法操作更多"的走法（解锁更多，保持局面活性）
        int? ShuffleBudget = null);          // 路径上 h 不下降步（腾挪步）的总预算；null=不限；外部逐档递增即迭代加深

    // 病态防线：路径深度上限（置换表已保证路径无环；上限仅防栈溢出，正常稠密关路径远小于此值）
    private const int MaxDepth = 20_000;

    private readonly Options _options;

    public GreedyDfsSolver(Options? options = null)
    {
        _options = options ?? new Options();
    }

    // 诊断探针：本次 Solve 到达过的最小启发式值（越接近 0 = 搜索越接近清场）——区分"早期堵死"与"尾部卡死"
    // 并行任务须各自 new 实例读取；复用实例时该值反映最后一次求解
    public int MinHeuristicSeen { get; private set; } = int.MaxValue;

    #endregion

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

    // 栈帧：状态 + 其 h + 已排序后继（带各自后继 h）+ 试探下标 + 剩余腾挪预算
    private sealed record Frame(TState State, int H, List<(TMove Move, int NextH)> Ordered, int Index, int Budget);

    // 迭代式 DFS（避免深递归栈溢出）
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
        var frames = new Stack<Frame>();
        long expanded = 0;
        long generated = 0;
        long frontierPeak = 0;
        var abort = Abort.None;
        var budget0 = _options.ShuffleBudget ?? int.MaxValue;        // 腾挪预算（int.MaxValue=不限）

        if (game.IsGoal(initialState))
        {
            return Build(SolveStatus.Solved, TerminationReason.GoalFound, [],
                Metrics(0, 0, 1, 0, sw, 0), null, sw, Optimality.UnitCostShortest);   // 初始即目标 = 零步且显然最短
        }
        var initialH = Math.Max(0, heuristic.EstimateToGoal(initialState));
        MinHeuristicSeen = initialH;
        var initialKey = game.StateKey(initialState);
        closed.Add(initialKey);
        frames.Push(new Frame(initialState, initialH,
            OrderedMoves(bundle, heuristic, initialState, game), 0, budget0));

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
            if (frame.Index >= frame.Ordered.Count)
            {
                // 本状态的动作试尽：退栈并撤销路径最后一步
                frames.Pop();
                if (path.Count > 0) path.RemoveAt(path.Count - 1);
                continue;
            }
            var (move, nextH) = frame.Ordered[frame.Index];
            frames.Pop();
            frames.Push(frame with { Index = frame.Index + 1 });
            expanded++;

            // 腾挪预算：h 不下降的后继消耗预算；预算不足整支剪掉（迭代加深由外部逐档放宽）
            var isProgress = nextH < frame.H;
            if (!isProgress && frame.Budget <= 0) continue;

            var nextState = game.ApplyMove(frame.State, move);
            generated++;
            var nextKey = game.StateKey(nextState);
            if (!closed.Add(nextKey)) continue;                      // 置换表去重
            if (path.Count + 1 > MaxDepth) continue;                 // 病态防线

            path.Add(new MoveRecord(bundle.Execution.EncodeMove(move), game.DescribeMove(frame.State, move)));
            if (game.IsGoal(nextState))
            {
                // 找到任一可解路径：Optimality=NotProven（贪心不证明最短）
                return Build(SolveStatus.Solved, TerminationReason.GoalFound, path.ToList(),
                    Metrics(expanded, generated, closed.Count, frontierPeak, sw, path.Count), null, sw, Optimality.NotProven);
            }
            frames.Push(new Frame(nextState, nextH,
                OrderedMoves(bundle, heuristic, nextState, game), 0,
                isProgress ? frame.Budget : frame.Budget - 1));
        }

        // 栈耗尽且未被预算中止：置换表覆盖全部可达状态且均无目标 → 可证模型内无解
        // 注意：腾挪预算模式下"栈耗尽"只说明该预算下无解（NotProven 语义），不构成无解证明
        if (abort == Abort.None)
        {
            if (_options.ShuffleBudget is not null)
            {
                return Build(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, [],
                    Metrics(expanded, generated, closed.Count, frontierPeak, sw, 0),
                    $"腾挪预算（{_options.ShuffleBudget}）内无解——预算受限探索，不构成无解证明", sw);
            }
            sw.Stop();
            return new SolveResult(
                SolveStatus.UnsolvableProven, Optimality.NotApplicable, true,
                TerminationReason.FrontierEmpty, MetricsScope.FullReachableGraph,
                [], Metrics(expanded, generated, closed.Count, frontierPeak, sw, 0), null);
        }
        // 预算/取消中止（找不到不等于无解——DFS 非穷尽）
        var (status, reason, message) = abort switch
        {
            Abort.Cancelled => (SolveStatus.Cancelled, TerminationReason.UserCancelled, "用户取消"),
            Abort.Node => (SolveStatus.ResourceLimit, TerminationReason.NodeLimit, $"节点预算耗尽（{request.NodeBudget}）"),
            _ => (SolveStatus.ResourceLimit, TerminationReason.TimeLimit, "时间预算耗尽")
        };
        return Build(status, reason, [], Metrics(expanded, generated, closed.Count, frontierPeak, sw, 0), message, sw);
    }

    #endregion

    #region 内部工具

    // 后继动作排序：h 升序（离场/解锁优先）；可选机动性平级（后继合法操作多者优先）；同序保持枚举序（确定性）
    private List<(TMove Move, int NextH)> OrderedMoves(
        AdapterBundle<TLevel, TState, TMove> bundle, IHeuristicAdapter<TState> heuristic, TState state,
        IGameAdapter<TLevel, TState, TMove> game)
    {
        var moves = game.GetLegalMoves(state);
        var evaluated = moves
            .Select((m, i) => (Move: m, Order: i,
                Next: game.ApplyMove(state, m),
                H: Math.Max(0, heuristic.EstimateToGoal(game.ApplyMove(state, m)))))
            .ToList();
        if (evaluated.Count > 0 && evaluated.Min(e => e.H) < MinHeuristicSeen)
        {
            MinHeuristicSeen = evaluated.Min(e => e.H);   // 搜索前沿离清场的最近距离
        }
        IEnumerable<(TMove Move, int NextH, int Order, int Mobility)> enriched = evaluated
            .Select(e => (e.Move, e.H, e.Order, Mobility: _options.MobilityTiebreak ? game.GetLegalMoves(e.Next).Count : 0));
        return enriched
            .OrderBy(t => t.NextH)
            .ThenByDescending(t => t.Mobility)            // 机动性平级：合法操作多者优先
            .ThenBy(t => t.Order)
            .Select(t => (t.Move, t.NextH))
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

    // 计数器打包：平均分支 = 累计生成后继数 / 已展开状态数（与 BFS/A* 同一口径；此前置 0 等于对消费者撒谎）
    private static SearchMetrics Metrics(long expanded, long generated, long unique, long frontierPeak, Stopwatch sw, int solutionLength)
    {
        var avgBranching = expanded > 0 ? (double)generated / expanded : 0;
        return new SearchMetrics(expanded, unique, frontierPeak, sw.ElapsedMilliseconds, solutionLength, avgBranching);
    }

    #endregion
}
