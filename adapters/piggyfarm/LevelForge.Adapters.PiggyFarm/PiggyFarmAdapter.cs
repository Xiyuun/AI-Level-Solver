using System.Text;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.PiggyFarm;

// 单只猪的规则状态：类型/朝向/当前占格；Cells 按 (y,x) 规范化排序，永不被原地修改
public sealed record PiggyFarmPig(
    int Id, string Type, PigFacing Facing, IReadOnlyList<GridCell> Cells, int Counter = 0);

// 关卡状态：地图尺寸 + 场上全部猪 + 是否已触发失败（爆炸计数归零）。
// 空列表且 !Failed = 已清场（胜利）；Failed 状态没有合法动作，求解器按死路处理。
public sealed record PiggyFarmState(
    int Width, int Height, IReadOnlyList<PiggyFarmPig> Pigs, bool Failed = false);

// 结构化动作：点击一只猪（字段足以无歧义定位目标；不从描述文本反解析）
public sealed record PiggyFarmMove(int PigId);

// piggyfarm 适配器：ClassicMode 滑动解谜的 v1.1 规则子集
//   - 支持 Normal / Long / RichPig（规则相同、脚印不同）+ Boom（碰撞计数，归零判负）+ Duck（自动离场）
//   - 规则：点击 → 沿朝向逐格滑动；前方撞到其它猪的脚印即停；有格子越过地图边界即整只离场；
//     被挡的猪也可点击 = 原地顶撞（起步即碰撞，触发碰撞事件但不位移）
//   - 小黄鸭（Duck.cs）：不可点击；任一离场/碰撞事件后，站立鸭子各自 BFS 寻路到边界（鸭不挡鸭），
//     可达即全部离场（离场时穿场而过，不与任何猪碰撞）
//   - 胜利：场上猪清空；无合法操作 = 死局（平台侧按搜索结论表达，不在这里判负）
// 不支持的机制（其余特殊猪/升降梯/动态兔子）在关卡校验阶段如实拒绝，不静默降级
public sealed class PiggyFarmAdapter :
    IGameAdapter<PiggyFarmLevel, PiggyFarmState, PiggyFarmMove>,
    IExecutionProtocol<PiggyFarmState, PiggyFarmMove>
{
    #region IGameAdapter

    // 原生关卡 → 初始状态；非法数据抛异常（禁止静默修复）
    public PiggyFarmState LoadLevel(PiggyFarmLevel level)
    {
        // 门面已完成校验；这里再守一道，防止绕过门面直接喂入非法数据
        if (level.Pigs.Count == 0)
        {
            throw new ArgumentException("关卡没有猪：没有可操作对象");
        }
        var pigs = level.Pigs
            .Select(p => new PiggyFarmPig(p.Id, p.Type, p.Facing, Normalize(p.Cells), p.Counter))
            .OrderBy(p => p.Id)
            .ToArray();
        return new PiggyFarmState(level.MapWidth, level.MapHeight, pigs);
    }

    // 当前局面全部合法操作（顺序稳定：按猪 Id 升序；空列表 = 无合法操作）：
    //   - 小黄鸭不可点击（游戏中 SkipWalkAfterClick，点击只广播不移动）
    //   - 普通猪两种合法操作：能前进 = 滑行；被挡 = 原地顶撞（游戏里点击起步后立刻碰撞，同样触发碰撞事件）
    public IReadOnlyList<PiggyFarmMove> GetLegalMoves(PiggyFarmState state)
    {
        if (state.Failed) return Array.Empty<PiggyFarmMove>();
        return state.Pigs
            .Where(pig => pig.Type != "Duck")
            .Select(pig => new PiggyFarmMove(pig.Id))
            .ToArray();
    }

    // 纯函数：返回新状态；入参状态与其中的数组一律不改动
    public PiggyFarmState ApplyMove(PiggyFarmState state, PiggyFarmMove move)
    {
        var pig = state.Pigs.FirstOrDefault(p => p.Id == move.PigId)
            ?? throw new ArgumentException($"非法动作：关卡中没有猪 #{move.PigId}");
        if (pig.Type == "Duck")
        {
            throw new ArgumentException("非法动作：小黄鸭不可点击（游戏中点击只广播事件，不产生移动）");
        }
        if (!CanAdvance(state, pig))
        {
            // 原地顶撞：起步即与阻挡猪碰撞（游戏侧同样触发碰撞回调），随后结算鸭子离场
            return ApplyBump(state, pig);
        }
        return ApplySlide(state, pig).Next;
    }

    // 滑行并返回新状态（求解器直接用，避免重复模拟）：离场 = 移除该猪；否则更新占格。
    // 无论离场还是碰撞，结算后都触发小黄鸭自动离场检查（游戏中离场/碰撞事件都会广播给鸭子）
    public static (PiggyFarmState Next, SlideResult Slide) ApplySlide(PiggyFarmState state, PiggyFarmPig pig)
    {
        var slide = SimulateSlide(state, pig);
        var failed = state.Failed;
        PiggyFarmState next;
        if (slide.Exited)
        {
            next = state with { Pigs = state.Pigs.Where(p => p.Id != pig.Id).ToArray() };
        }
        else
        {
            // 撞到阻挡猪：移动方和被撞方都要结算 BoomPig 的爆炸计数（与游戏 NormalPig 碰撞回调一致）
            var moved = pig with { Cells = slide.FinalCells };
            if (pig.Type == "Boom")
            {
                moved = moved with { Counter = pig.Counter - 1 };
                if (moved.Counter <= 0) failed = true;
            }

            var pigs = new List<PiggyFarmPig>(state.Pigs.Count);
            foreach (var other in state.Pigs)
            {
                if (other.Id == pig.Id)
                {
                    pigs.Add(moved);
                    continue;
                }
                if (other.Id == slide.BlockerId && other.Type == "Boom")
                {
                    var hit = other with { Counter = other.Counter - 1 };
                    if (hit.Counter <= 0) failed = true;
                    pigs.Add(hit);
                    continue;
                }
                pigs.Add(other);
            }
            next = ApplyInfection(state with { Pigs = pigs.OrderBy(p => p.Id).ToArray() }, pig.Id, slide.BlockerId ?? -1);
        }
        // 结算小黄鸭自动离场（离场/碰撞事件都会触发）；最后统一落 Failed 标记
        return (ResolveDucks(next) with { Failed = failed }, slide);
    }

    // 原地顶撞：起步即碰撞阻挡猪（无位移）。爆炸计数双方结算 + 丧尸感染判定；随后结算鸭子离场。
    public static PiggyFarmState ApplyBump(PiggyFarmState state, PiggyFarmPig pig)
    {
        var slide = SimulateSlide(state, pig);   // 只为取 BlockerId（Steps 必为 0）
        var failed = state.Failed;
        var pigs = new List<PiggyFarmPig>(state.Pigs.Count);
        foreach (var other in state.Pigs)
        {
            if (other.Type == "Boom" && (other.Id == pig.Id || other.Id == slide.BlockerId))
            {
                var hit = other with { Counter = other.Counter - 1 };
                if (hit.Counter <= 0) failed = true;
                pigs.Add(hit);
                continue;
            }
            pigs.Add(other);
        }
        var next = ApplyInfection(state with { Pigs = pigs.OrderBy(p => p.Id).ToArray() }, pig.Id, slide.BlockerId ?? -1);
        var result = ResolveDucks(next) with { Failed = failed };
        return result;
    }

    // 丧尸感染（游戏 PigManager.FindConvertibleNormal/ConvertNormalToZombie）：每次已结算接触中，
    // 普通猪与丧尸猪相触 → 普通猪转为丧尸（同控制器同占格，行为与普通猪完全一致，仅影响结算奖励）。
    // 无接触不感染；丧尸撞丧尸、普通撞普通均不感染。
    private static PiggyFarmState ApplyInfection(PiggyFarmState state, int idA, int idB)
    {
        if (idA == idB) return state;
        var a = state.Pigs.FirstOrDefault(p => p.Id == idA);
        var b = state.Pigs.FirstOrDefault(p => p.Id == idB);
        if (a is null || b is null) return state;
        var normalId = a.Type == "Zombie" && b.Type == "Normal" ? b.Id
            : b.Type == "Zombie" && a.Type == "Normal" ? a.Id
            : -1;
        if (normalId < 0) return state;
        return state with
        {
            Pigs = state.Pigs.Select(p => p.Id == normalId ? p with { Type = "Zombie" } : p).OrderBy(p => p.Id).ToArray()
        };
    }

    // 小黄鸭自动离场（游戏 Duck.cs：任一离场/碰撞事件后，站立的鸭子各自 BFS 寻路到边界，可达即离场）：
    //   - BFS 把其它鸭子的占格视为空（鸭不挡鸭）→ 每只鸭的可达性与其它鸭无关，单趟判定即可
    //   - 离场的鸭子穿场而过（ShouldIgnorePigCollision），不与任何猪碰撞
    //   - 被铁笼圈住的鸭子不寻路（游戏 line 76/153）——铁笼属后续批次，当前无笼时全部参与
    public static PiggyFarmState ResolveDucks(PiggyFarmState state)
    {
        var ducks = state.Pigs.Where(p => p.Type == "Duck").ToArray();
        if (ducks.Length == 0) return state;
        var occupied = OccupancyOf(state.Pigs.Where(p => p.Type != "Duck"));
        var leaving = new List<int>();
        foreach (var duck in ducks)
        {
            if (DuckHasPathToEdge(state, duck.Cells[0], occupied))
            {
                leaving.Add(duck.Id);
            }
        }
        if (leaving.Count == 0) return state;
        return state with { Pigs = state.Pigs.Where(p => !leaving.Contains(p.Id)).OrderBy(p => p.Id).ToArray() };
    }

    // 小黄鸭 BFS 寻路（游戏 Duck.cs:236-289 的可达性判定；邻居顺序 Up/Right/Down/Left 只影响路径形状，
    // 不影响可达结论——离场鸭子互相穿透，可达即全部离场）：从鸭子格出发，4 邻接扩展到任意边界外即可达
    private static bool DuckHasPathToEdge(
        PiggyFarmState state, GridCell start, Dictionary<GridCell, int> occupied)
    {
        var visited = new HashSet<GridCell> { start };
        var queue = new Queue<GridCell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })   // Up, Right, Down, Left
            {
                var next = new GridCell(cell.X + dx, cell.Y + dy);
                if (!InBounds(state, next)) return true;   // 到边界外 = 可离场
                if (!visited.Add(next)) continue;
                if (occupied.ContainsKey(next)) continue;  // 其它猪挡路；鸭子占格视为空（鸭不挡鸭）
                queue.Enqueue(next);
            }
        }
        return false;
    }

    // 胜利：场上猪清空（与游戏本体 TrySettleIfCleared 的可判定部分一致；地形层已由子集声明排除）
    public bool IsGoal(PiggyFarmState state)
    {
        return !state.Failed && state.Pigs.Count == 0;
    }

    // 规范化状态键：地图尺寸 + 每只猪（Id/类型/朝向/规范化占格）
    // 状态维度覆盖：位置、朝向、类型、在场集合——v1 没有剩余次数/冷却/道具等额外维度
    public string StateKey(PiggyFarmState state)
    {
        var sb = new StringBuilder();
        sb.Append(state.Width).Append('x').Append(state.Height).Append(state.Failed ? ":F" : ":O");
        foreach (var pig in state.Pigs.OrderBy(p => p.Id))
        {
            sb.Append("|#").Append(pig.Id).Append(':').Append(pig.Type).Append(':').Append(pig.Facing);
            if (pig.Type == "Boom") sb.Append(":c").Append(pig.Counter);
            foreach (var cell in pig.Cells)
            {
                sb.Append(',').Append(cell.X).Append('.').Append(cell.Y);
            }
        }
        return sb.ToString();
    }

    // 人类可读动作描述（只用于日志/磁带展示，不作为执行协议）
    public string DescribeMove(PiggyFarmState state, PiggyFarmMove move)
    {
        var pig = state.Pigs.FirstOrDefault(p => p.Id == move.PigId)
            ?? throw new ArgumentException($"非法动作：关卡中没有猪 #{move.PigId}");
        if (pig.Type == "Duck")
        {
            return $"小黄鸭 #{pig.Id} 不可点击（点击只广播事件）";
        }
        var facing = PiggyFarmLevels.FacingName(pig.Facing);
        var slide = SimulateSlide(state, pig);
        if (slide.Exited)
        {
            return $"点击 {pig.Type} #{pig.Id}（朝{facing}）→ 前方畅通，从{facing}边界离场";
        }
        if (slide.Steps == 0)
        {
            return $"点击 {pig.Type} #{pig.Id}（朝{facing}）→ 原地顶撞 #{slide.BlockerId}（触发碰撞事件）";
        }
        return $"点击 {pig.Type} #{pig.Id}（朝{facing}）→ 滑行 {slide.Steps} 格后被 #{slide.BlockerId} 阻挡停下";
    }

    // 场景快照：每个占格一个实体（头格类型 pigHead 带朝向，其余 pigBody）；id 由猪号+坐标组成，执行内稳定
    public SceneSnapshot Snapshot(PiggyFarmState state)
    {
        var entities = new List<Entity>();
        foreach (var pig in state.Pigs.OrderBy(p => p.Id))
        {
            var head = HeadCell(pig);
            foreach (var cell in pig.Cells)
            {
                var isHead = cell == head;
                var props = new Dictionary<string, JsonNode?>();
                if (isHead)
                {
                    props["facing"] = JsonValue.Create(pig.Facing.ToString());
                }
                props["pigType"] = JsonValue.Create(pig.Type);
                if (pig.Type == "Boom") props["counter"] = JsonValue.Create(pig.Counter);
                entities.Add(new Entity(
                    $"pig-{pig.Id}-{cell.X}-{cell.Y}",
                    (isHead ? "pigHead" : "pigBody") + pig.Type,
                    cell.X,
                    cell.Y,
                    props));
            }
        }
        var globals = new Dictionary<string, JsonNode?>
        {
            ["remainingPigs"] = JsonValue.Create(state.Pigs.Count)
        };
        return new SceneSnapshot(
            new BoardSize(state.Width, state.Height),
            Array.Empty<Cell>(),
            entities,
            globals);
    }

    #endregion

    #region IExecutionProtocol

    public int MoveSchemaVersion => 1;

    public int SemanticSchemaVersion => 1;

    public JsonNode EncodeMove(PiggyFarmMove move)
    {
        return new JsonObject { ["pigId"] = move.PigId };
    }

    public PiggyFarmMove DecodeMove(JsonNode encoded)
    {
        if (encoded is not JsonObject obj || obj["pigId"] is not JsonValue value
            || !value.TryGetValue<int>(out var pigId))
        {
            throw new ArgumentException("动作编码非法：需要 {\"pigId\": <整数>}");
        }
        return new PiggyFarmMove(pigId);
    }

    // 语义投影：对拍比较的唯一来源；覆盖规则矩阵声明的全部状态维度（地图/猪号/类型/朝向/占格）
    public JsonNode ProjectSemanticState(PiggyFarmState state)
    {
        var pigs = new JsonArray();
        foreach (var pig in state.Pigs.OrderBy(p => p.Id))
        {
            var cells = new JsonArray(pig.Cells
                .Select(c => (JsonNode?)new JsonObject { ["x"] = c.X, ["y"] = c.Y })
                .ToArray());
            var pigNode = new JsonObject
            {
                ["id"] = pig.Id,
                ["type"] = pig.Type,
                ["facing"] = pig.Facing.ToString(),
                ["cells"] = cells
            };
            if (pig.Type == "Boom") pigNode["counter"] = pig.Counter;
            pigs.Add(pigNode);
        }
        return new JsonObject
        {
            ["mapWidth"] = state.Width,
            ["mapHeight"] = state.Height,
            ["pigs"] = pigs
        };
    }

    #endregion

    #region 几何工具（求解器共用）

    // 前进一步是否畅通：整块脚印前移一格；越界格视为可离场（不阻挡）；界内格被其它猪占用即被挡
    public static bool CanAdvance(PiggyFarmState state, PiggyFarmPig pig)
    {
        var (dx, dy) = PiggyFarmLevels.Delta(pig.Facing);
        var occupied = OccupancyOf(state.Pigs.Where(p => p.Id != pig.Id));
        foreach (var cell in pig.Cells)
        {
            var next = new GridCell(cell.X + dx, cell.Y + dy);
            if (InBounds(state, next) && occupied.ContainsKey(next))
            {
                return false;
            }
        }
        return true;
    }

    // 滑行模拟：返回是否离场、最终占格、滑行步数与阻挡猪（未离场时有效）
    public static SlideResult SimulateSlide(PiggyFarmState state, PiggyFarmPig pig)
    {
        var (dx, dy) = PiggyFarmLevels.Delta(pig.Facing);
        var occupied = OccupancyOf(state.Pigs.Where(p => p.Id != pig.Id));
        var cells = pig.Cells.ToArray();
        var steps = 0;
        while (true)
        {
            var next = cells.Select(c => new GridCell(c.X + dx, c.Y + dy)).ToArray();
            int? blocker = null;
            foreach (var cell in next)
            {
                if (InBounds(state, cell) && occupied.TryGetValue(cell, out var owner))
                {
                    blocker = owner;
                    break;
                }
            }
            if (blocker is not null)
            {
                return new SlideResult(false, Normalize(cells), steps, blocker);
            }
            if (next.Any(c => !InBounds(state, c)))
            {
                return new SlideResult(true, Array.Empty<GridCell>(), steps, null);
            }
            cells = next;
            steps++;
        }
    }

    // 完整滑行到边界所需路径是否完全畅通（求解器的"可直接离场"判定）
    public static bool CanExitNow(PiggyFarmState state, PiggyFarmPig pig)
    {
        return SimulateSlide(state, pig).Exited;
    }

    public static GridCell HeadCell(PiggyFarmPig pig)
    {
        var (dx, dy) = PiggyFarmLevels.Delta(pig.Facing);
        // 头格 = 沿朝向前伸最远的格子；同排取 x/y 最小的一个，保证确定性
        return pig.Cells
            .OrderByDescending(c => c.X * dx + c.Y * dy)
            .ThenBy(c => c.X)
            .ThenBy(c => c.Y)
            .First();
    }

    public static (int Dx, int Dy) Delta(PigFacing facing) => PiggyFarmLevels.Delta(facing);

    public static bool InBounds(PiggyFarmState state, GridCell cell)
    {
        return cell.X >= 0 && cell.X < state.Width && cell.Y >= 0 && cell.Y < state.Height;
    }

    public static IReadOnlyList<GridCell> Normalize(IEnumerable<GridCell> cells)
    {
        return cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
    }

    private static Dictionary<GridCell, int> OccupancyOf(IEnumerable<PiggyFarmPig> pigs)
    {
        var map = new Dictionary<GridCell, int>();
        foreach (var pig in pigs)
        {
            foreach (var cell in pig.Cells)
            {
                map[cell] = pig.Id;
            }
        }
        return map;
    }

    #endregion
}

// 滑行模拟结论：Exited=true 时 FinalCells 为空；否则 BlockerId 为阻挡猪（Steps=0 表示原地被挡）
public sealed record SlideResult(bool Exited, IReadOnlyList<GridCell> FinalCells, int Steps, int? BlockerId);
