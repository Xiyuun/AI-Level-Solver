using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Tests.TestDoubles;

// 合成玩具适配层：纯有向图游走，无任何游戏语义，全部行为可手工穷举（求解内核 §9.5）
// 专用于内核自回归——内核的测试不依赖任何真项目；区别于产品内置的推箱子玩具
// 适配器无状态：一个实例可解任意多个关卡（与执行池批量用法一致）

// 合成关卡：起点 + 目标 + 有向边表
public sealed record SyntheticLevel(int Start, int Goal, IReadOnlyList<(int From, int To)> Edges);

// 状态 = 当前所在节点 + 所属关卡（同一适配器可服务多个关卡）
public sealed record SyntheticState(int Node, SyntheticLevel Level);

// 操作 = 走到相邻节点
public sealed record SyntheticMove(int To);

// 可注入的故障模式：验证内核对适配层异常与跨次运行不稳定性的处理路径（故障注入实验同源）
public enum SyntheticFailure
{
    None,                  // 正常
    ThrowOnLoad,           // LoadLevel 抛非参数异常
    ThrowOnApply,          // ApplyMove 抛异常
    FlipMoveOrderPerLoad   // 第二次加载起合法操作顺序翻转（模拟跨次运行的隐藏状态泄漏）
}

// 有向图适配器：邻接顺序 = 边声明顺序（保序），保证搜索确定性
public sealed class SyntheticGraphAdapter :
    IGameAdapter<SyntheticLevel, SyntheticState, SyntheticMove>,
    IExecutionProtocol<SyntheticState, SyntheticMove>
{
    private readonly ConditionalWeakTable<SyntheticLevel, Dictionary<int, List<int>>> _adjCache
        = new();                                                 // 按关卡实例缓存的邻接表（引用键，线程安全）
    private readonly SyntheticFailure _failure;                 // 注入的故障模式
    private int _loadCount;                                      // LoadLevel 调用计数（FlipMoveOrderPerLoad 用）

    public SyntheticGraphAdapter(SyntheticFailure failure = SyntheticFailure.None)
    {
        _failure = failure;
    }

    #region IGameAdapter 七成员

    // 解析关卡并校验：起点/目标必须出现在图的节点集中（起点==目标除外），否则按契约抛 ArgumentException
    public SyntheticState LoadLevel(SyntheticLevel level)
    {
        _loadCount++;
        if (_failure == SyntheticFailure.ThrowOnLoad)
        {
            throw new InvalidOperationException("注入：LoadLevel 失败");
        }
        var nodes = new HashSet<int>();
        foreach (var (from, to) in level.Edges)
        {
            nodes.Add(from);
            nodes.Add(to);
        }
        // 起点或目标孤立且非"初始即目标"即视为非法关卡
        if (level.Start != level.Goal && (!nodes.Contains(level.Start) || !nodes.Contains(level.Goal)))
        {
            throw new ArgumentException($"起点 {level.Start} 或目标 {level.Goal} 不在图的节点集中");
        }
        return new SyntheticState(level.Start, level);
    }

    // 邻接表按声明序给出全部合法操作；无后继返回空列表
    public IReadOnlyList<SyntheticMove> GetLegalMoves(SyntheticState state)
    {
        var adj = AdjacencyOf(state.Level);
        if (!adj.TryGetValue(state.Node, out var next))
        {
            return Array.Empty<SyntheticMove>();
        }
        var moves = next.Select(n => new SyntheticMove(n)).ToList();
        // 注入的跨次不稳定：偶数次加载后翻转顺序（同一次运行内仍稳定，双跑才暴露）
        if (_failure == SyntheticFailure.FlipMoveOrderPerLoad && _loadCount % 2 == 0)
        {
            moves.Reverse();
        }
        return moves;
    }

    // 纯函数：非法操作（非相邻）抛异常；支持故障注入
    public SyntheticState ApplyMove(SyntheticState state, SyntheticMove move)
    {
        if (_failure == SyntheticFailure.ThrowOnApply)
        {
            throw new InvalidOperationException("注入：ApplyMove 失败");
        }
        var legal = AdjacencyOf(state.Level).TryGetValue(state.Node, out var next) && next.Contains(move.To);
        if (!legal)
        {
            throw new InvalidOperationException($"非法操作：{state.Node} 不可达 {move.To}");
        }
        return state with { Node = move.To };
    }

    // 目标判定：当前节点 == 所属关卡的目标
    public bool IsGoal(SyntheticState state)
    {
        return state.Node == state.Level.Goal;
    }

    // 规范化键：节点号即全部状态维度
    public string StateKey(SyntheticState state)
    {
        return "n" + state.Node;
    }

    // 展示文本：形如 0->1
    public string DescribeMove(SyntheticState state, SyntheticMove move)
    {
        return $"{state.Node}->{move.To}";
    }

    // 场景快照：单格棋盘 + 游标实体 + node 全局量（仅满足契约形状）
    public SceneSnapshot Snapshot(SyntheticState state)
    {
        return new SceneSnapshot(
            new BoardSize(1, 1),
            Array.Empty<Cell>(),
            new[] { new Entity("cursor", "cursor", 0, 0, null) },
            new Dictionary<string, JsonNode?> { ["node"] = state.Node });
    }

    #endregion

    #region IExecutionProtocol

    // 动作 schema 版本
    public int MoveSchemaVersion => 1;

    // 动作编码：{"to": 目标节点}
    public JsonNode EncodeMove(SyntheticMove move)
    {
        return new JsonObject { ["to"] = move.To };
    }

    // 动作解码：读取 to 字段；缺失抛参数异常，值非整数由 GetValue 抛异常——均为"非法编码抛异常"
    public SyntheticMove DecodeMove(JsonNode encoded)
    {
        if (encoded is JsonObject obj && obj["to"] is JsonValue to)
        {
            return new SyntheticMove(to.GetValue<int>());
        }
        throw new ArgumentException($"非法动作编码：{encoded.ToJsonString()}");
    }

    // 语义投影 schema 版本
    public int SemanticSchemaVersion => 1;

    // 语义投影：{"node": 当前节点}——仅一个维度，足够内核测试消费
    public JsonNode ProjectSemanticState(SyntheticState state)
    {
        return new JsonObject { ["node"] = state.Node };
    }

    #endregion

    #region 内部工具

    // 取（或构建）关卡邻接表：引用键缓存，避免每次展开重复建图
    private Dictionary<int, List<int>> AdjacencyOf(SyntheticLevel level)
    {
        return _adjCache.GetValue(level, static l => l.Edges
            .GroupBy(e => e.From)
            .ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList()));
    }

    // 便捷工厂：构造适配层包（Game 与 Execution 为同一实例；适配器无状态，可复用解任意关卡）
    public static AdapterBundle<SyntheticLevel, SyntheticState, SyntheticMove> CreateBundle(
        SyntheticFailure failure = SyntheticFailure.None)
    {
        var adapter = new SyntheticGraphAdapter(failure);
        return new AdapterBundle<SyntheticLevel, SyntheticState, SyntheticMove>("synthetic", "test", adapter, adapter);
    }

    #endregion
}
