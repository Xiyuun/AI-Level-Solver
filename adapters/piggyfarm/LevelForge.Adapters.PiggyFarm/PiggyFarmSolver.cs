using System.Diagnostics;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.PiggyFarm;

// 求解器（v1）：确定性多策略搜索，结论按内核五维度如实表达
//   阶段1 归约启发：只要存在"当前就能直接离场"的猪就先让它离场（清场单调收益），
//          没有可直接离场的猪时才在可起步的猪上分支 —— 快速找到大关的解（不完整：找到即返回）
//   阶段2 完整搜索组合：同一状态图用三种"先试离场、再按步长"的排序，配合逐级放大的节点上限反复尝试；
//          任一策略自然穷尽（未触发上限）即整图无解 → UnsolvableProven；全部被预算截断 → ResourceLimit
// 预算耗尽（时间/节点）一律 ResourceLimit，不冒充无解；找到解时按"每猪至少点一次"的下界判定最优性
public sealed class PiggyFarmSolver
{
    private readonly PiggyFarmAdapter _adapter = new();

    // 默认预算：调用方未给时也要有上界，避免 HTTP 请求无限等待
    private static readonly TimeSpan DefaultTimeBudget = TimeSpan.FromSeconds(30);
    private const long DefaultNodeBudget = 300_000;

    // 阶段1最多消耗的节点数：留足预算给阶段2
    private const long GreedyPhaseNodeCap = 40_000;

    // 阶段2 的组合策略：同一图、不同动作排序；逐级放大每策略的节点配额
    private static readonly SearchOrder[] Orders = [SearchOrder.ExitThenId, SearchOrder.ExitThenStepsDesc, SearchOrder.ExitThenStepsAsc];
    private static readonly long[] PortfolioCaps = [5_000, 50_000, 150_000];

    public SolveResult Solve(PiggyFarmLevel level, TimeSpan? timeBudget, long? nodeBudget, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var timeLimit = timeBudget ?? DefaultTimeBudget;
        var nodeLimit = nodeBudget ?? DefaultNodeBudget;
        if (timeLimit <= TimeSpan.Zero)
        {
            return Failure(SolveStatus.ResourceLimit, TerminationReason.TimeLimit,
                new SearchMetrics(0, 0, 0, 0, 0, 0), "求解时间预算为 0，未开始搜索");
        }

        var initial = _adapter.LoadLevel(level);
        if (_adapter.IsGoal(initial))
        {
            // 初始即目标：零步解（与内核语义一致）；下界 0 使最优性成立
            return new SolveResult(
                SolveStatus.Solved, Optimality.UnitCostShortest, false,
                TerminationReason.GoalFound, MetricsScope.ExpandedPrefix,
                [], new SearchMetrics(0, 0, 0, watch.ElapsedMilliseconds, 0, 0), null);
        }

        var context = new SearchContext(timeLimit, nodeLimit, watch, ct);
        var path = new List<PiggyFarmMove>();
        try
        {
            // 阶段1：归约启发（不完整；触到阶段额度即止，不视为失败）
            // 配额是"本阶段可用的新增节点数"，要换算成绝对节点上限（Nodes 是跨阶段累计值）
            context.PhaseNodeCap = context.Nodes + Math.Min(GreedyPhaseNodeCap, nodeLimit - context.Nodes);
            try
            {
                if (GreedySearch(initial, path, new HashSet<string>(StringComparer.Ordinal), context))
                {
                    return BuildSolved(initial, path, context);
                }
            }
            catch (PhaseCapReachedException)
            {
                path.Clear();   // 阶段1 用完自己的额度：交给阶段2 从头完整搜索
            }
            context.PhaseNodeCap = long.MaxValue;

            // 阶段2：完整搜索组合；任一策略穷尽 = 整图无解
            foreach (var cap in PortfolioCaps)
            {
                foreach (var order in Orders)
                {
                    var remaining = nodeLimit - context.Nodes;
                    if (remaining <= 0)
                    {
                        break;
                    }
                    // 同样是换算成绝对上限：本策略最多新增 cap 个节点
                    context.PhaseNodeCap = context.Nodes + Math.Min(cap, remaining);
                    path.Clear();
                    try
                    {
                        if (CompleteSearch(initial, path, new HashSet<string>(StringComparer.Ordinal), context, order))
                        {
                            return BuildSolved(initial, path, context);
                        }
                    }
                    catch (PhaseCapReachedException)
                    {
                        continue;   // 该策略被配额截断：换下一个策略
                    }
                    // 未触发配额且返回 false = 该策略穷尽了可达图：无解（与排序无关）
                    return new SolveResult(
                        SolveStatus.UnsolvableProven, Optimality.NotApplicable, true,
                        TerminationReason.FrontierEmpty, MetricsScope.FullReachableGraph,
                        [], context.Metrics(0), null);
                }
            }

            // 所有策略都被预算截断：未知，不冒充无解
            return Failure(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, context.Metrics(0),
                $"达到节点上限（{nodeLimit}），本次未得出结论（未知≠无解）");
        }
        catch (SearchCancelledException)
        {
            return Failure(SolveStatus.Cancelled, TerminationReason.UserCancelled, context.Metrics(0),
                "求解被调用方取消");
        }
        catch (SearchBudgetException ex)
        {
            return ex.Kind == BudgetKind.Time
                ? Failure(SolveStatus.ResourceLimit, TerminationReason.TimeLimit, context.Metrics(0),
                    $"达到时间上限（{timeLimit.TotalSeconds:0.#}s），本次未得出结论（未知≠无解）")
                : Failure(SolveStatus.ResourceLimit, TerminationReason.NodeLimit, context.Metrics(0),
                    $"达到节点上限（{nodeLimit}），本次未得出结论（未知≠无解）");
        }
    }

    #region 阶段1：归约启发搜索

    // 归约：只要存在可直接离场的猪，逐个离开（不分支）；之后才在可起步的猪上分支
    // 回溯必须把本次调用追加的所有动作（含归约阶段的离场动作）一起回退——否则路径里会残留
    // 已离场猪的动作，后续重放时表现为"动作不合法/关卡中没有该猪"
    private bool GreedySearch(PiggyFarmState state, List<PiggyFarmMove> path, HashSet<string> visited, SearchContext context)
    {
        var baseCount = path.Count;
        context.Step(path.Count);
        while (true)
        {
            var exitMove = FindExitMove(state);
            if (exitMove is null)
            {
                break;
            }
            path.Add(exitMove);
            state = _adapter.ApplyMove(state, exitMove);
            context.Step(path.Count);
            if (state.Pigs.Count == 0)
            {
                return true;
            }
        }

        if (!visited.Add(_adapter.StateKey(state)))
        {
            path.RemoveRange(baseCount, path.Count - baseCount);
            return false;
        }
        context.MarkUnique();

        foreach (var candidate in OrderedLegalMoves(state, SearchOrder.ExitThenId))
        {
            path.Add(candidate.Move);
            if (candidate.Next.Pigs.Count == 0
                || GreedySearch(candidate.Next, path, visited, context))
            {
                return true;
            }
            path.RemoveAt(path.Count - 1);
        }
        path.RemoveRange(baseCount, path.Count - baseCount);
        return false;
    }

    // 找到一只"沿朝向整条路径畅通、点击即离场"的猪；按 Id 升序取第一只，保证确定性
    // 小黄鸭不可点击（离场由事件结算的 ResolveDucks 自动处理），不在此列
    private PiggyFarmMove? FindExitMove(PiggyFarmState state)
    {
        foreach (var pig in state.Pigs.OrderBy(p => p.Id))
        {
            if (pig.Type == "Duck") continue;
            if (PiggyFarmAdapter.CanExitNow(state, pig))
            {
                return new PiggyFarmMove(pig.Id);
            }
        }
        return null;
    }

    #endregion

    #region 阶段2：完整搜索

    // 完整 DFS：遍历全部合法动作；只有自然结束（未触发配额）才是无解证明
    private bool CompleteSearch(PiggyFarmState state, List<PiggyFarmMove> path, HashSet<string> visited,
        SearchContext context, SearchOrder order)
    {
        context.Step(path.Count);
        if (!visited.Add(_adapter.StateKey(state)))
        {
            return false;
        }
        context.MarkUnique();

        foreach (var candidate in OrderedLegalMoves(state, order))
        {
            path.Add(candidate.Move);
            if (candidate.Next.Pigs.Count == 0
                || CompleteSearch(candidate.Next, path, visited, context, order))
            {
                return true;
            }
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    // 动作枚举 + 排序（一次性算好下一步与滑行结果，避免求解与排序重复模拟）
    // 小黄鸭不可点击（离场由 ResolveDucks 在事件结算时自动处理），不产生动作
    private List<MoveCandidate> OrderedLegalMoves(PiggyFarmState state, SearchOrder order)
    {
        var list = new List<MoveCandidate>();
        foreach (var pig in state.Pigs.OrderBy(p => p.Id))
        {
            if (pig.Type == "Duck") continue;
            if (!PiggyFarmAdapter.CanAdvance(state, pig))
            {
                continue;
            }
            var (next, slide) = PiggyFarmAdapter.ApplySlide(state, pig);
            list.Add(new MoveCandidate(new PiggyFarmMove(pig.Id), next,
                Exit: next.Pigs.Count < state.Pigs.Count, Steps: slide.Steps));
        }
        list.Sort((a, b) =>
        {
            // 所有排序都先试"点击即离场"的动作（只改变顺序，不改变完整性）
            var byExit = (a.Exit ? 0 : 1).CompareTo(b.Exit ? 0 : 1);
            if (byExit != 0)
            {
                return byExit;
            }
            switch (order)
            {
                case SearchOrder.ExitThenStepsDesc:
                    var byStepsDesc = b.Steps.CompareTo(a.Steps);
                    if (byStepsDesc != 0) return byStepsDesc;
                    break;
                case SearchOrder.ExitThenStepsAsc:
                    var byStepsAsc = a.Steps.CompareTo(b.Steps);
                    if (byStepsAsc != 0) return byStepsAsc;
                    break;
            }
            return a.Move.PigId.CompareTo(b.Move.PigId);
        });
        return list;
    }

    private enum SearchOrder
    {
        ExitThenId,
        ExitThenStepsDesc,
        ExitThenStepsAsc
    }

    private sealed record MoveCandidate(PiggyFarmMove Move, PiggyFarmState Next, bool Exit, int Steps);

    #endregion

    #region 结果组装

    private SolveResult BuildSolved(PiggyFarmState initial, List<PiggyFarmMove> path, SearchContext context)
    {
        // 按动作序列重放，生成结构化动作 + 展示文本（描述取自动作执行前的局面）
        var records = new List<MoveRecord>(path.Count);
        var state = initial;
        foreach (var move in path)
        {
            var text = _adapter.DescribeMove(state, move);
            var encoded = _adapter.EncodeMove(move);
            state = _adapter.ApplyMove(state, move);
            records.Add(new MoveRecord(encoded, text));
        }
        // 最优性：每只猪必须至少被点击一次才能离场（点击是唯一位移/离场途径），
        // 因此解长 == 初始猪数时该解就是最少动作解；否则只声明"有解，最优性未证明"
        var optimality = records.Count == initial.Pigs.Count ? Optimality.UnitCostShortest : Optimality.NotProven;
        return new SolveResult(
            SolveStatus.Solved, optimality, false,
            TerminationReason.GoalFound, MetricsScope.ExpandedPrefix,
            records, context.Metrics(records.Count), null);
    }

    private static SolveResult Failure(SolveStatus status, TerminationReason reason, SearchMetrics metrics, string message)
    {
        return new SolveResult(status, Optimality.NotApplicable, false, reason,
            MetricsScope.ExpandedPrefix, [], metrics, message);
    }

    #endregion

    #region 预算与统计

    private enum BudgetKind { Time, Node }

    private sealed class SearchBudgetException(BudgetKind kind) : Exception
    {
        public BudgetKind Kind { get; } = kind;
    }

    private sealed class SearchCancelledException : Exception
    {
    }

    private sealed class PhaseCapReachedException : Exception
    {
    }

    // 搜索上下文：统一预算判定与统计（各阶段共用同一份预算）
    private sealed class SearchContext(TimeSpan timeLimit, long nodeLimit, Stopwatch watch, CancellationToken ct)
    {
        private int _sinceCheck;

        public long PhaseNodeCap { get; set; } = long.MaxValue;
        public long Nodes { get; private set; }
        public long Unique { get; private set; }
        public long PeakPath { get; private set; }
        public long ExpandedMoves { get; private set; }

        // 每个状态入口调用：统计 + 预算判定（每 128 步检查一次时钟，降低 Stopwatch 开销）
        public void Step(int pathLength)
        {
            Nodes++;
            ExpandedMoves++;
            if (pathLength > PeakPath)
            {
                PeakPath = pathLength;
            }
            if (Nodes > nodeLimit)
            {
                throw new SearchBudgetException(BudgetKind.Node);
            }
            if (Nodes > PhaseNodeCap)
            {
                throw new PhaseCapReachedException();
            }
            _sinceCheck++;
            if (_sinceCheck < 128)
            {
                return;
            }
            _sinceCheck = 0;
            if (ct.IsCancellationRequested)
            {
                throw new SearchCancelledException();
            }
            if (watch.Elapsed > timeLimit)
            {
                throw new SearchBudgetException(BudgetKind.Time);
            }
        }

        public void MarkUnique()
        {
            Unique++;
        }

        public SearchMetrics Metrics(int solutionLength)
        {
            var branching = Nodes == 0 ? 0 : (double)ExpandedMoves / Nodes;
            return new SearchMetrics(Nodes, Unique, PeakPath, watch.ElapsedMilliseconds, solutionLength, branching);
        }
    }

    #endregion
}
