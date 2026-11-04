using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// 束搜索策略：每层只保留最有希望的 W 个状态，用"宽度换深度"
// 适用：状态空间巨大、动作多、但解的分层结构清晰（典型：滑动猪 60~120 实体，BFS/A* 组合爆炸）
//
// 与既有策略的关系：
//   BFS  = 宽度无穷、无启发式（保证最优，跑不动大空间）
//   A*   = 优先级队列、有启发式（保证最优，内存仍爆）
//   贪心  = 宽度 1 的深度优先（最快，但一条路走到黑）
//   束搜索 = 宽度 W（可调），介于贪心与 A* 之间
//
// 诚实边界（求解内核 §5 铁律）：
//   - 找到解 → Solved，但 Optimality=NotProven（束宽剪枝意味着不保证最短）
//   - 预算耗尽 → ResourceLimit（未知，不是无解）
//   - 束枯竭（所有保留状态都无后继）→ 同样不构成无解证明，因为被剪掉的分支没被考察
//     （BFS 的"前沿耗尽"才是证明；这里用 NodeLimit + 明确文案区分，绝不冒充 unsolvable_proven）
public sealed class BeamSearchSolver<TLevel, TState, TMove> : ISearchStrategy<TLevel, TState, TMove>
{
    #region 配置

    // 搜索参数（构造期注入；默认值按滑动猪稠密关实测定）
    // Seed：同分候选的随机排序种子（null = 确定性顺序）
    //   用途：束枯竭时换个种子重跑——不同随机序会保留不同的状态子集，等于对搜索树做不同剪枝
    public sealed record Options(
        int Width = 64,              // 束宽：每层保留的状态数（64 ≈ 4 秒内跑完 100 层）
        int MobilityProbeFactor = 3, // 机动性细排：先按 f 取 W×factor 个候选，再按机动性重排取前 W
        int MaxLayers = 4000,        // 层数上限（病态防线，正常关 100~300 层收敛）
        int? Seed = null);           // 同分随机种子；null = 确定性

    private readonly Options _options;

    public BeamSearchSolver(Options? options = null)
    {
        _options = options ?? new Options();
    }

    #endregion

    #region 数据

    // 搜索节点：状态 + 键 + 父节点下标 + 入边动作 + g（已走步数）+ h（启发式）
    // 节点全部保留在 _nodes 里（束宽 × 层数 ≈ 数千），重建路径靠 parent 下标回溯
    private sealed record Node(TState State, string Key, int Parent, TMove Move, int G, int H);

    // 候选后继（层内排序用，避免过早装箱）
    private readonly record struct Candidate(int NodeIndex, int F, int Mobility);

    #endregion

    #region 求解入口

    // 执行一次求解：预算/取消/异常语义与 BFS/A* 完全一致
    public SolveResult Solve(AdapterBundle<TLevel, TState, TMove> bundle, SolveRequest<TLevel> request)
    {
        if (bundle.Game is not IHeuristicAdapter<TState> heuristic)
        {
            return new SolveResult(
                SolveStatus.Error, Optimality.NotApplicable, false,
                TerminationReason.AdapterError, MetricsScope.ExpandedPrefix,
                [], null, "适配层未声明 IHeuristicAdapter，束搜索策略不可用");
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

    #region 束搜索主循环

    // 主循环：逐层扩展 → 去重 → 排序 → 保留 W 个
    private SolveResult Search(
        AdapterBundle<TLevel, TState, TMove> bundle,
        IHeuristicAdapter<TState> heuristic,
        SolveRequest<TLevel> request,
        TState initialState,
        Stopwatch sw)
    {
        var game = bundle.Game;
        var nodes = new List<Node>();
        var visited = new HashSet<string>();                 // 全局去重（跨层也去重：同一状态不必再入束）
        long expanded = 0;
        long generated = 0;
        long frontierPeak = 0;

        var initialKey = game.StateKey(initialState);
        var initialH = Math.Max(0, heuristic.EstimateToGoal(initialState));
        if (game.IsGoal(initialState))
        {
            return Build(SolveStatus.Solved, TerminationReason.GoalFound, [],
                new SearchMetrics(0, 1, 0, sw.ElapsedMilliseconds, 0, 0), null, sw,
                Optimality.UnitCostShortest);   // 零步解：最短是显然的
        }
        nodes.Add(new Node(initialState, initialKey, -1, default!, 0, initialH));
        visited.Add(initialKey);
        var frontier = new List<int> { 0 };

        for (var layer = 0; layer < _options.MaxLayers; layer++)
        {
            // 预算与取消：每层检查（层内开销有界）
            if (request.CancellationToken.IsCancellationRequested)
            {
                return Build(SolveStatus.Cancelled, TerminationReason.UserCancelled, [],
                    Metrics(expanded, generated, visited, frontierPeak, sw, 0), "用户取消", sw);
            }
            if (request.TimeBudget is { } tb && sw.Elapsed >= tb)
            {
                return Build(SolveStatus.ResourceLimit, TerminationReason.TimeLimit, [],
                    Metrics(expanded, generated, visited, frontierPeak, sw, 0),
                    $"时间预算耗尽（{tb.TotalMilliseconds:F0}ms）", sw);
            }
            if (request.NodeBudget is { } nb && expanded >= nb)
            {
                return Build(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, [],
                    Metrics(expanded, generated, visited, frontierPeak, sw, 0),
                    $"节点预算耗尽（{nb}）", sw);
            }

            var candidates = new List<Candidate>(frontier.Count * 8);
            var goalIndex = -1;
            // 扩展整层：收集所有未访问后继
            foreach (var nodeIndex in frontier)
            {
                var node = nodes[nodeIndex];
                expanded++;
                foreach (var move in game.GetLegalMoves(node.State))
                {
                    var nextState = game.ApplyMove(node.State, move);
                    generated++;
                    var nextKey = game.StateKey(nextState);
                    if (!visited.Add(nextKey)) continue;
                    var h = Math.Max(0, heuristic.EstimateToGoal(nextState));
                    if (game.IsGoal(nextState))
                    {
                        // 目标：挂在当前父节点下，立刻收口（束搜索不保证最优，如实标 NotProven）
                        nodes.Add(new Node(nextState, nextKey, nodeIndex, move, node.G + 1, 0));
                        goalIndex = nodes.Count - 1;
                        break;
                    }
                    nodes.Add(new Node(nextState, nextKey, nodeIndex, move, node.G + 1, h));
                    candidates.Add(new Candidate(nodes.Count - 1, node.G + 1 + h, 0));
                }
                if (goalIndex >= 0) break;
            }
            if (goalIndex >= 0)
            {
                var solution = Reconstruct(bundle, nodes, goalIndex);
                return Build(SolveStatus.Solved, TerminationReason.GoalFound, solution,
                    Metrics(expanded, generated, visited, frontierPeak, sw, solution.Count), null, sw,
                    Optimality.NotProven);
            }
            // 束枯竭：所有保留状态都没有未访问后继。这不是无解证明（被剪掉的分支未考察）
            if (candidates.Count == 0)
            {
                return Build(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, [],
                    Metrics(expanded, generated, visited, frontierPeak, sw, 0),
                    $"束搜索枯竭（第 {layer} 层无可扩展状态）——束宽剪枝下不构成无解证明", sw);
            }

            // 选束：先按 f=g+h 粗排取 W×factor，再对少量候选算机动性细排（机动性需额外展开一次，代价高）
            // 指定 Seed 时，同分候选按随机键排序——换种子即换一套被保留的状态子集（束枯竭后的重启手段）
            var width = _options.Width;
            var probeCount = Math.Min(candidates.Count, Math.Max(width, width * _options.MobilityProbeFactor));
            var jitter = new Random(_options.Seed ?? 0);
            var probeBase = _options.Seed is null
                ? candidates.OrderBy(c => c.F).ThenBy(c => c.NodeIndex).AsEnumerable()
                : candidates.Select(c => (c, k: jitter.Next())).OrderBy(x => x.c.F).ThenBy(x => x.k).Select(x => x.c);
            var top = probeBase.Take(probeCount).ToList();
            var refined = top
                .Select(c => c with { Mobility = game.GetLegalMoves(nodes[c.NodeIndex].State).Count })
                .OrderBy(c => c.F)
                .ThenByDescending(c => c.Mobility)      // 同名次时优先"还能动得更多"的局面（保持活性）
                .ThenBy(c => c.NodeIndex)
                .Take(width)
                .Select(c => c.NodeIndex)
                .ToList();
            frontier = refined;
            frontierPeak = Math.Max(frontierPeak, frontier.Count);
        }

        // 层数上限：病态防线，同样不构成无解证明
        return Build(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, [],
            Metrics(expanded, generated, visited, frontierPeak, sw, 0),
            $"达到层数上限（{_options.MaxLayers}）——未取得结论", sw);
    }

    #endregion

    #region 内部工具

    // 沿父指针重建操作序列（每步取父状态做 DescribeMove，与搜索时语义一致）
    private static List<MoveRecord> Reconstruct(
        AdapterBundle<TLevel, TState, TMove> bundle, List<Node> nodes, int goalIndex)
    {
        var chain = new List<Node>();
        var index = goalIndex;
        while (index >= 0 && nodes[index].Parent >= 0)
        {
            chain.Add(nodes[index]);
            index = nodes[index].Parent;
        }
        chain.Reverse();
        return chain.Select(n => new MoveRecord(
            bundle.Execution.EncodeMove(n.Move),
            bundle.Game.DescribeMove(nodes[n.Parent].State, n.Move))).ToList();
    }

    // 组装结果
    private static SolveResult Build(
        SolveStatus status, TerminationReason reason,
        IReadOnlyList<MoveRecord> solution, SearchMetrics? metrics,
        string? errorMessage, Stopwatch sw, Optimality optimality = Optimality.NotApplicable)
    {
        sw.Stop();
        return new SolveResult(status, status == SolveStatus.Solved ? optimality : Optimality.NotApplicable,
            false, reason, MetricsScope.ExpandedPrefix, solution, metrics, errorMessage);
    }

    // 计数器打包
    private static SearchMetrics Metrics(
        long expanded, long generated, HashSet<string> visited, long frontierPeak,
        Stopwatch sw, int solutionLength)
    {
        var avgBranching = expanded > 0 ? (double)generated / expanded : 0;
        return new SearchMetrics(expanded, visited.Count, frontierPeak, sw.ElapsedMilliseconds, solutionLength, avgBranching);
    }

    #endregion
}
