using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Tests.TestDoubles;

// 纯函数性/确定性检查用的缺陷适配器：链式图（0→1→…→N）+ 可注入的三种典型违规
// 专供合规检查测试验证"检查器抓得住违规"，本身不是合规适配器

// 链式关卡：长度 + 目标
public sealed record PurityLevel(int Length, int Goal);

// 可变状态：节点 + 操作日志（日志进语义投影，让入参变异可被投影察觉）
public sealed record PurityState(int Node, List<string> Log);

// 操作：走到相邻节点
public sealed record PurityMove(int To);

// 可注入的缺陷模式
public enum PurityDefect
{
    None,              // 完全合规（链式图，结果稳定）
    MutateInput,       // ApplyMove 向入参的日志追加记录（入参变异）
    UnstableResult,    // ApplyMove 结果随调用次数交替变化（隐藏状态）
    UnstableMoveOrder  // GetLegalMoves 顺序随调用次数翻转（顺序不稳定）
}

public sealed class PurityAdapter :
    IGameAdapter<PurityLevel, PurityState, PurityMove>,
    IExecutionProtocol<PurityState, PurityMove>
{
    private readonly PurityDefect _defect;           // 注入的缺陷
    private int _callCount;                          // 违规实现用的隐藏计数（本身就是反例）

    public PurityAdapter(PurityDefect defect)
    {
        _defect = defect;
    }

    #region IGameAdapter 七成员

    // 解析关卡：仅做目标越界校验
    public PurityState LoadLevel(PurityLevel level)
    {
        if (level.Goal < 0 || level.Goal >= level.Length)
        {
            throw new ArgumentException("目标越界");
        }
        return new PurityState(0, new List<string>());
    }

    // 左右相邻两格；UnstableMoveOrder 模式下每次调用翻转顺序
    public IReadOnlyList<PurityMove> GetLegalMoves(PurityState s)
    {
        _callCount++;
        var moves = new List<PurityMove>();
        if (s.Node > 0) moves.Add(new PurityMove(s.Node - 1));
        if (s.Node < 7) moves.Add(new PurityMove(s.Node + 1));   // 上界用常量避免依赖 Level
        if (_defect == PurityDefect.UnstableMoveOrder && _callCount % 2 == 0)
        {
            moves.Reverse();                                     // 偶数次调用翻转顺序
        }
        return moves;
    }

    // 纯函数转移；三种缺陷分别在此或其上注入
    public PurityState ApplyMove(PurityState s, PurityMove m)
    {
        if (_defect == PurityDefect.MutateInput)
        {
            s.Log.Add($"mutated@{m.To}");                        // 反例：直接改入参
        }
        if (_defect == PurityDefect.UnstableResult)
        {
            _callCount++;
            // 反例：结果依赖隐藏计数，交替返回不同节点
            return new PurityState(m.To + (_callCount % 2), new List<string>());
        }
        // 合规路径：新状态 + 日志副本（不共享入参引用）
        return new PurityState(m.To, new List<string>(s.Log));
    }

    // 到达目标即胜
    public bool IsGoal(PurityState s)
    {
        return s.Node == 5;                                      // 链目标固定 5，与 Level.Goal 一致的用法
    }

    // 节点+日志共同构成键（日志不同 = 不同状态，与投影一致）
    public string StateKey(PurityState s)
    {
        return $"n{s.Node}|{string.Join(",", s.Log)}";
    }

    public string DescribeMove(PurityState s, PurityMove m)
    {
        return $"{s.Node}->{m.To}";
    }

    public SceneSnapshot Snapshot(PurityState s)
    {
        return new SceneSnapshot(new BoardSize(8, 1), [],
            [new Entity("slider", "slider", s.Node, 0, null)],
            new Dictionary<string, JsonNode?> { ["node"] = s.Node });
    }

    #endregion

    #region IExecutionProtocol

    public int MoveSchemaVersion => 1;

    public JsonNode EncodeMove(PurityMove m)
    {
        return new JsonObject { ["to"] = m.To };
    }

    public PurityMove DecodeMove(JsonNode n)
    {
        return new PurityMove(((JsonObject)n)["to"]!.GetValue<int>());
    }

    public int SemanticSchemaVersion => 1;

    // 语义投影暴露日志：入参变异因此可被"再投影原状态"察觉
    public JsonNode ProjectSemanticState(PurityState s)
    {
        return new JsonObject
        {
            ["node"] = s.Node,
            ["log"] = new JsonArray(s.Log.Select(l => (JsonNode)l!).ToArray())
        };
    }

    #endregion
}
