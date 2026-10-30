using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;

namespace LevelForge.Kernel.Hosting;

// 内核自测：用内置合成玩具适配层（无游戏语义、可手工穷举）对内核本身做回归（求解内核 §9.5）
// 与测试工程里的合成适配器职责相同但独立成份——平台进程内（门1第5项）可跑，不依赖测试框架
// 内核的信任在工具开发期一次性建立，由"内核升级 = 平台级事件 + 全项目全量回归"持续守护
public static class KernelSelfTest
{
    #region 自测入口

    // 运行四组用例：最短路 / 零步解 / 可证无解 / 预算诚实性；任一失败即 Fail
    public static ComplianceResult Run()
    {
        var issues = new List<string>();

        // 用例1：菱形图最短路 0->1->3
        var diamond = new SelfLevel(0, 3, new (int, int)[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var r1 = Solve(diamond);
        if (r1.Status != SolveStatus.Solved || r1.Solution.Count != 2)
        {
            issues.Add($"[最短路] 期望 solved/2步，实际 {r1.Status}/{r1.Solution.Count}步");
        }

        // 用例2：初始即目标 → 零步解
        var zero = new SelfLevel(0, 0, new (int, int)[] { (0, 1) });
        var r2 = Solve(zero);
        if (r2.Status != SolveStatus.Solved || r2.Solution.Count != 0)
        {
            issues.Add($"[零步解] 期望 solved/0步，实际 {r2.Status}/{r2.Solution.Count}步");
        }

        // 用例3：目标不可达 → 完整遍历后可证无解
        var disconnected = new SelfLevel(0, 6, new (int, int)[] { (0, 1), (1, 2), (2, 0), (5, 6) });
        var r3 = Solve(disconnected);
        if (r3.Status != SolveStatus.UnsolvableProven || !r3.GraphComplete)
        {
            issues.Add($"[可证无解] 期望 unsolvable_proven/graphComplete，实际 {r3.Status}/{r3.GraphComplete}");
        }

        // 用例4：节点预算耗尽 → 未知，绝不冒充无解
        var chain = new SelfLevel(0, 99, Enumerable.Range(0, 99).Select(i => (i, i + 1)).ToArray());
        var r4 = new BfsSolver<SelfLevel, SelfState, SelfMove>().Solve(
            CreateBundle(), new SolveRequest<SelfLevel>(chain) { NodeBudget = 5 });
        if (r4.Status != SolveStatus.ResourceLimit || r4.TerminationReason != TerminationReason.NodeLimit)
        {
            issues.Add($"[预算诚实] 期望 resource_limit/node_limit，实际 {r4.Status}/{r4.TerminationReason}");
        }

        return issues.Count == 0
            ? ComplianceResult.Pass(ComplianceCheckKind.KernelSelfTest, ["四组用例全部符合预期"])
            : ComplianceResult.Fail(ComplianceCheckKind.KernelSelfTest, issues);
    }

    #endregion

    #region 自测专用合成适配层

    // 自测关卡：图 + 起点 + 目标
    private sealed record SelfLevel(int Start, int Goal, (int From, int To)[] Edges);

    // 自测状态：当前节点
    private sealed record SelfState(int Node, SelfLevel Level);

    // 自测操作：走到目标节点
    private sealed record SelfMove(int To);

    // 自测适配器：纯图游走，与测试工程的合成适配器同构但互不依赖
    private sealed class SelfAdapter :
        IGameAdapter<SelfLevel, SelfState, SelfMove>,
        IExecutionProtocol<SelfState, SelfMove>
    {
        // 解析关卡：节点合法性校验
        public SelfState LoadLevel(SelfLevel level)
        {
            var nodes = new HashSet<int>();
            foreach (var (f, t) in level.Edges)
            {
                nodes.Add(f);
                nodes.Add(t);
            }
            if (level.Start != level.Goal && (!nodes.Contains(level.Start) || !nodes.Contains(level.Goal)))
            {
                throw new ArgumentException("起点或目标不在节点集中");
            }
            return new SelfState(level.Start, level);
        }

        // 邻接保序枚举
        public IReadOnlyList<SelfMove> GetLegalMoves(SelfState s)
        {
            return s.Level.Edges.Where(e => e.From == s.Node).Select(e => new SelfMove(e.To)).ToList();
        }

        // 纯函数转移
        public SelfState ApplyMove(SelfState s, SelfMove m)
        {
            return s with { Node = m.To };
        }

        // 到达目标即胜
        public bool IsGoal(SelfState s)
        {
            return s.Node == s.Level.Goal;
        }

        // 节点号即全部维度
        public string StateKey(SelfState s)
        {
            return "n" + s.Node;
        }

        public string DescribeMove(SelfState s, SelfMove m)
        {
            return $"{s.Node}->{m.To}";
        }

        public SceneSnapshot Snapshot(SelfState s)
        {
            return new SceneSnapshot(new BoardSize(1, 1), [], [new Entity("c", "c", 0, 0, null)],
                new Dictionary<string, JsonNode?> { ["node"] = s.Node });
        }

        public int MoveSchemaVersion => 1;

        public JsonNode EncodeMove(SelfMove m)
        {
            return new JsonObject { ["to"] = m.To };
        }

        public SelfMove DecodeMove(JsonNode n)
        {
            return new SelfMove(((JsonObject)n)["to"]!.GetValue<int>());
        }

        public int SemanticSchemaVersion => 1;

        public JsonNode ProjectSemanticState(SelfState s)
        {
            return new JsonObject { ["node"] = s.Node };
        }
    }

    // 默认请求求解
    private static SolveResult Solve(SelfLevel level)
    {
        return new BfsSolver<SelfLevel, SelfState, SelfMove>().Solve(
            CreateBundle(), new SolveRequest<SelfLevel>(level));
    }

    // 构造自测适配层包
    private static AdapterBundle<SelfLevel, SelfState, SelfMove> CreateBundle()
    {
        var adapter = new SelfAdapter();
        return new AdapterBundle<SelfLevel, SelfState, SelfMove>("kernel-selftest", "0", adapter, adapter);
    }

    #endregion
}
