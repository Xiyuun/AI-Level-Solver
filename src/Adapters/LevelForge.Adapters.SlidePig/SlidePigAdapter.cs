using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.SlidePig;

// 滑动猪适配器：七成员契约 + 执行协议 + 启发式能力（内部参考实现，兼任接入套件 skill 的"标准答案"）
// 零引擎依赖：全部规则经 SlidePigRules 网格模型实现，与游戏本体语义的对应见 SlidePigArtifacts.RuleMatrix
// IHeuristicAdapter：剩余非铁笼实体数——每个非笼实体至少需一次点击离场，可采纳且一致（A* 最优性成立）
public sealed class SlidePigAdapter :
    IGameAdapter<SlidePigLevel, SlidePigState, SlidePigMove>,
    IExecutionProtocol<SlidePigState, SlidePigMove>,
    IHeuristicAdapter<SlidePigState>
{
    // v1 支持的类型名集合（JSON 原文 → 枚举）；子集外类型在 LoadLevel 明确拒绝
    private static readonly Dictionary<string, SlidePigType> SupportedTypes = new()
    {
        ["Normal"] = SlidePigType.Normal,
        ["Boom"] = SlidePigType.Boom,
        ["Spring"] = SlidePigType.Spring,
        ["Long"] = SlidePigType.Long,
        ["Lock"] = SlidePigType.Lock,
        ["SmallElephant"] = SlidePigType.SmallElephant,
        ["Cage"] = SlidePigType.Cage,
        ["CageL"] = SlidePigType.CageL
    };

    // 实体键片段缓存：未变化的实体在状态间同引用复用（内核 §10 性能路径——真实关 24 万+ 状态实测触发）
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SlidePigEntity, string> KeyFragments = new();

    #region IGameAdapter 七成员

    // 解析关卡：完整静态校验后构建初始状态；子集外类型/非法布局抛 ArgumentException
    public SlidePigState LoadLevel(SlidePigLevel level)
    {
        var errors = SlidePigValidator.Validate(level);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"关卡 {level.LevelId} 非法：{string.Join("；", errors)}");
        }
        var entities = new List<SlidePigEntity>();
        foreach (var p in level.Pigs)
        {
            var type = SupportedTypes[p.PigType];
            var counter = type switch
            {
                SlidePigType.Boom => SlidePigRules.ParseBoomCount(p.SpecialInfo),
                SlidePigType.Lock or SlidePigType.Cage or SlidePigType.CageL => SlidePigRules.ParseUnlockCount(p.SpecialInfo),
                _ => 0
            };
            // specialInfo=0 的铁笼在游戏内初始化即自删（Init 时 _remainUnlockCount<=0 直接解笼），同步排除
            if ((type is SlidePigType.Cage or SlidePigType.CageL) && counter == 0) continue;
            entities.Add(new SlidePigEntity(
                p.Id, type, Enum.Parse<SlideFacing>(p.Facing), SlidePigRules.Normalize(p.OccupiedCells), counter));
        }
        return new SlidePigState(entities.OrderBy(e => e.Id).ToList(), false, level);
    }

    // 全部合法点击（顺序点击模型；被挡/锁住/铁笼不产生操作）
    public IReadOnlyList<SlidePigMove> GetLegalMoves(SlidePigState state)
    {
        return SlidePigRules.LegalMoves(state);
    }

    // 纯函数：完整结算一次点击
    public SlidePigState ApplyMove(SlidePigState state, SlidePigMove move)
    {
        return SlidePigRules.Apply(state, move);
    }

    // 胜利判定：未失败且场上清空
    public bool IsGoal(SlidePigState state)
    {
        return SlidePigRules.IsWin(state);
    }

    // 规范化键：失败标志 + 逐猪（类型/朝向/计数/脚印）——覆盖全部状态维度
    // 实体片段按引用缓存拼接（热路径：BFS 对每个生成后继都调一次本方法）
    public string StateKey(SlidePigState state)
    {
        var sb = new System.Text.StringBuilder(64 + state.Pigs.Count * 32);
        sb.Append('F').Append(state.Failed ? '1' : '0');
        foreach (var p in state.Pigs)
        {
            sb.Append('|').Append(KeyFragments.GetValue(p, static e =>
                $"{e.Id}:{(int)e.Type}:{(int)e.Facing}:{e.Counter}:" +
                string.Join(";", e.Cells.Select(c => $"{c.X},{c.Y}"))));
        }
        return sb.ToString();
    }

    // 展示文本：点击 + 朝向 + 结局（滑格数/弹回）
    public string DescribeMove(SlidePigState state, SlidePigMove move)
    {
        var pig = state.Find(move.PigId);
        var facingName = pig!.Facing.ToString();
        return pig.Type == SlidePigType.Spring ? $"点击猪#{pig.Id}{facingName}移（可能弹回）" : $"点击猪#{pig.Id}{facingName}移";
    }

    // 场景快照：多格猪在 v1 渲染器下取锚点格（脚印最小格）+ 面积/朝向属性；全局量显示剩余与失败态
    public SceneSnapshot Snapshot(SlidePigState state)
    {
        var cells = new List<Cell>();                 // 无地形格子层（边界即棋盘外沿）
        var entities = new List<Entity>();
        foreach (var p in state.Pigs)
        {
            var anchor = p.Cells[0];                  // 规范形首格 = 最小格，作为锚点
            var props = new Dictionary<string, JsonNode?>
            {
                ["facing"] = p.Facing.ToString(),
                ["area"] = p.Cells.Length
            };
            if (p.Type is SlidePigType.Boom or SlidePigType.Lock or SlidePigType.Cage or SlidePigType.CageL)
            {
                props["counter"] = p.Counter;         // 计数入 props 展示（爆炸/解锁剩余）
            }
            entities.Add(new Entity($"pig-{p.Id}", TypeName(p.Type), anchor.X, anchor.Y, props));
        }
        var globals = new Dictionary<string, JsonNode?>
        {
            ["pigsRemaining"] = state.Pigs.Count,
            ["failed"] = state.Failed
        };
        return new SceneSnapshot(new BoardSize(state.Level.MapWidth, state.Level.MapHeight), cells, entities, globals);
    }

    #endregion

    #region IExecutionProtocol

    // 动作 schema 版本
    public int MoveSchemaVersion => 1;

    // 动作编码：{"type":"click","pigId":N}（harness 侧映射为对指定猪的真实点击入口）
    public JsonNode EncodeMove(SlidePigMove move)
    {
        return new JsonObject { ["type"] = "click", ["pigId"] = move.PigId };
    }

    // 动作解码：读取 type=click 与 pigId；非法编码抛异常
    public SlidePigMove DecodeMove(JsonNode n)
    {
        if (n is JsonObject o && o["type"]?.GetValue<string>() == "click" && o["pigId"] is JsonValue v)
        {
            return new SlidePigMove(v.GetValue<int>());
        }
        throw new ArgumentException($"非法动作编码：{n.ToJsonString()}");
    }

    // 语义投影 schema 版本
    public int SemanticSchemaVersion => 1;

    // 语义投影：在场实体全量（id/类型/朝向/计数/脚印）+ 失败标志——对拍比较的唯一来源
    public JsonNode ProjectSemanticState(SlidePigState state)
    {
        return new JsonObject
        {
            ["failed"] = state.Failed,
            ["pigs"] = new JsonArray(state.Pigs.Select(p => (JsonNode)new JsonObject
            {
                ["id"] = p.Id,
                ["type"] = p.Type.ToString(),
                ["facing"] = p.Facing.ToString(),
                ["counter"] = p.Counter,
                ["cells"] = new JsonArray(p.Cells.Select(c => (JsonNode)new JsonObject { ["x"] = c.X, ["y"] = c.Y }).ToArray())
            }).ToArray())
        };
    }

    #endregion

    #region IHeuristicAdapter（A* 启发式）

    // 到目标的代价下界：剩余非铁笼实体数（铁笼可随他猪离场免费消失；每个非笼实体至少一次点击）
    // 可采纳且一致——A* 在单位成本下保证最少动作解
    public int EstimateToGoal(SlidePigState state)
    {
        var count = 0;
        foreach (var p in state.Pigs)
        {
            if (p.Type is not (SlidePigType.Cage or SlidePigType.CageL)) count++;
        }
        return count;
    }

    #endregion

    #region 内部工具

    // 类型 → 快照实体类型名（视觉映射键）
    private static string TypeName(SlidePigType type)
    {
        return type switch
        {
            SlidePigType.Normal => "normal",
            SlidePigType.Boom => "boom",
            SlidePigType.Spring => "spring",
            SlidePigType.Long => "long",
            SlidePigType.Lock => "lock",
            SlidePigType.SmallElephant => "elephant",
            SlidePigType.Cage => "cage",
            _ => "cageL"
        };
    }

    #endregion
}
