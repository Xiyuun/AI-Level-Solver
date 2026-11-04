using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.SlidePig;

// 故障注入模式（第17步实验专用）：三种典型适配层缺陷，供验证门"抓得住错误"的演示与测试
// 不属于生产代码路径——只在故障注入实验/测试/演示中引用
public enum SlidePigFault
{
    None = 0,                   // 正常（透传）
    StateKeyIgnoresCounters = 1,// 键漏计数维度：不同爆炸/解锁计数的局面产生相同键（门1键判别应红）
    OddPigsFrozen = 2,          // 漏枚举操作：奇数 id 的猪永远不可点击（门3基准diff应红）
    BoomNeverDecrements = 3     // 规则反转：爆炸猪碰撞不扣计数（门2对拍应在首次爆炸碰撞步分歧）
}

// 故障装饰适配器：包装正常适配器，按注入模式篡改对应成员，其余透传
// 用途：给验证门喂"已知带缺陷的适配层"，证明防线能抓住（故障注入反向验证习惯的实战形态）
public sealed class SlidePigFaultAdapter :
    IGameAdapter<SlidePigLevel, SlidePigState, SlidePigMove>,
    IExecutionProtocol<SlidePigState, SlidePigMove>,
    IHeuristicAdapter<SlidePigState>
{
    private readonly SlidePigAdapter _inner;        // 被包装的正常适配器
    private readonly SlidePigFault _fault;          // 注入的故障模式

    public SlidePigFaultAdapter(SlidePigFault fault)
    {
        _inner = new SlidePigAdapter();
        _fault = fault;
    }

    #region IGameAdapter 七成员（故障注入点：LoadLevel/GetLegalMoves/ApplyMove/StateKey）

    // 关卡解析：透传
    public SlidePigState LoadLevel(SlidePigLevel level)
    {
        return _inner.LoadLevel(level);
    }

    // 合法操作枚举：OddPigsFrozen 故障下过滤掉奇数 id 猪（漏枚举——求解器永远看不到这些操作）
    public IReadOnlyList<SlidePigMove> GetLegalMoves(SlidePigState state)
    {
        var moves = _inner.GetLegalMoves(state);
        return _fault == SlidePigFault.OddPigsFrozen
            ? moves.Where(m => m.PigId % 2 == 0).ToList()
            : moves;
    }

    // 状态转移：BoomNeverDecrements 故障下把本步被扣减的爆炸计数恢复（规则反转——爆炸永不归零也永不判负）
    public SlidePigState ApplyMove(SlidePigState state, SlidePigMove move)
    {
        var after = _inner.ApplyMove(state, move);
        if (_fault != SlidePigFault.BoomNeverDecrements)
        {
            return after;
        }
        // 对比前后：把所有爆炸猪被扣的计数加回去，并清除因此触发的失败态
        var pigs = after.Pigs.Select(p =>
        {
            var before = state.Pigs.FirstOrDefault(q => q.Id == p.Id);
            if (p.Type == SlidePigType.Boom && before is not null && p.Counter < before.Counter)
            {
                return p with { Counter = before.Counter };
            }
            return p;
        }).ToList();
        return after with { Pigs = pigs, Failed = false };
    }

    // 胜利判定：透传
    public bool IsGoal(SlidePigState state)
    {
        return _inner.IsGoal(state);
    }

    // 规范化键：StateKeyIgnoresCounters 故障下从键里剔除计数字段（不同计数的局面合并为同一状态）
    public string StateKey(SlidePigState state)
    {
        if (_fault != SlidePigFault.StateKeyIgnoresCounters)
        {
            return _inner.StateKey(state);
        }
        // 与正常键同构但去掉 counter 段：F..|id:type:facing:cells
        var sb = new System.Text.StringBuilder(64 + state.Pigs.Count * 32);
        sb.Append('F').Append(state.Failed ? '1' : '0');
        foreach (var p in state.Pigs)
        {
            sb.Append('|').Append(p.Id).Append(':').Append((int)p.Type).Append(':').Append((int)p.Facing)
                .Append(':').Append(string.Join(";", p.Cells.Select(c => $"{c.X},{c.Y}")));
        }
        return sb.ToString();
    }

    // 展示文本：透传
    public string DescribeMove(SlidePigState state, SlidePigMove move)
    {
        return _inner.DescribeMove(state, move);
    }

    // 场景快照：透传
    public SceneSnapshot Snapshot(SlidePigState state)
    {
        return _inner.Snapshot(state);
    }

    #endregion

    #region IExecutionProtocol（透传）

    // 动作 schema 版本：透传
    public int MoveSchemaVersion => _inner.MoveSchemaVersion;

    // 动作编码：透传
    public JsonNode EncodeMove(SlidePigMove move)
    {
        return _inner.EncodeMove(move);
    }

    // 动作解码：透传
    public SlidePigMove DecodeMove(JsonNode n)
    {
        return _inner.DecodeMove(n);
    }

    // 语义投影 schema 版本：透传
    public int SemanticSchemaVersion => _inner.SemanticSchemaVersion;

    // 语义投影：透传（故障在规则层，投影如实反映被篡改后的状态——对拍正是靠它发现分歧）
    public JsonNode ProjectSemanticState(SlidePigState state)
    {
        return _inner.ProjectSemanticState(state);
    }

    #endregion

    #region IHeuristicAdapter（透传）

    // 启发式：透传（剩余非笼实体数）
    public int EstimateToGoal(SlidePigState state)
    {
        return _inner.EstimateToGoal(state);
    }

    #endregion

    // 便捷工厂：构造带故障的适配层包（Game 与 Execution 为同一装饰实例）
    public static AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> CreateBundle(SlidePigFault fault)
    {
        var adapter = new SlidePigFaultAdapter(fault);
        return new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>(
            "slide-pig" + (fault == SlidePigFault.None ? "" : $"-fault{(int)fault}"), "0.1.0-fault", adapter, adapter);
    }
}
