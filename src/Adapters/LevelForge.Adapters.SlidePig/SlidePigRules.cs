namespace LevelForge.Adapters.SlidePig;

// 滑动猪 · 纯规则引擎（网格模型；语义来源与证据见 SlidePigArtifacts.RuleMatrix）
// 模型口径：顺序点击——一次点击完全结算（滑行→碰撞/离场→级联）后才进行下一次点击
// 移动 = 脚印整块逐格前移；前缘目标格全越界 → 整猪离场；任一格被占 → 停止（贴靠格）
// 性能：热路径用布尔网格占用表（真实关 24 万+ 状态实测触发内核 §10 优化路径）
public static class SlidePigRules
{
    // specialInfo 计数默认值（与游戏 Controller 序列化字段一致）
    public const int DefaultBoomCount = 3;             // BoomPig.initialBoomCount
    public const int DefaultUnlockCount = 10;          // LockPig/Cage.initialUnlockCount

    #region 朝向与占格

    // 朝向的单位步进（y=0 在上）
    public static (int Dx, int Dy) Delta(SlideFacing facing)
    {
        return facing switch
        {
            SlideFacing.Up => (0, -1),
            SlideFacing.Down => (0, 1),
            SlideFacing.Left => (-1, 0),
            _ => (1, 0)
        };
    }

    // 脚印规范形：按 x 再 y 排序，保证逻辑相等的脚印序列化一致
    public static (int X, int Y)[] Normalize(IEnumerable<(int X, int Y)> cells)
    {
        return cells.OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
    }

    // 该类型是否可移动（铁笼永不可；锁猪计数>0 不可；笼内猪被锁——按状态判定用 IsMovable(state)）
    public static bool IsMovable(SlidePigEntity e)
    {
        if (e.Type is SlidePigType.Cage or SlidePigType.CageL) return false;
        if (e.Type == SlidePigType.Lock && e.Counter > 0) return false;
        return true;
    }

    #endregion

    #region 前缘判定

    // 前缘判定结论：Step=可前移一格；Exit=前缘目标全越界（整猪离场）；Blocked=被占（停）
    public enum FrontVerdict { Step, Exit, Blocked }

    // 判定指定猪在布尔占用表下的前缘结论（占用表含全部在场实体；自己格子已被调用方临时清除）
    public static FrontVerdict JudgeFront(SlidePigEntity e, bool[] occ, int width, int height)
    {
        var (dx, dy) = Delta(e.Facing);
        // 前缘格 = 沿移动轴坐标最大的格
        var axisMax = int.MinValue;
        for (var i = 0; i < e.Cells.Length; i++)
        {
            var v = dx != 0 ? e.Cells[i].X * dx : e.Cells[i].Y * dy;
            if (v > axisMax) axisMax = v;
        }
        var anyOccupied = false;
        var anyInBounds = false;
        for (var i = 0; i < e.Cells.Length; i++)
        {
            var c = e.Cells[i];
            var v = dx != 0 ? c.X * dx : c.Y * dy;
            if (v != axisMax) continue;                          // 非前缘格
            var tx = c.X + dx;
            var ty = c.Y + dy;
            if (tx < 0 || tx >= width || ty < 0 || ty >= height) continue;   // 越界格不构成阻挡
            anyInBounds = true;
            if (occ[ty * width + tx]) anyOccupied = true;
        }
        // 前缘目标格沿轴同坐标：要么全越界要么全在内——被占即阻挡，全越界即离场，否则可前移
        if (anyOccupied) return FrontVerdict.Blocked;
        return anyInBounds ? FrontVerdict.Step : FrontVerdict.Exit;
    }

    #endregion

    #region 计数解析（specialInfo → Counter）

    // Boom 计数：specialInfo 为正整数则用之，否则默认 3（BoomPig.ResolveBoomCount）
    public static int ParseBoomCount(string specialInfo)
    {
        if (int.TryParse(specialInfo?.Trim(), out var v) && v > 0) return v;
        return DefaultBoomCount;
    }

    // Lock/Cage 计数：specialInfo 为非负整数则用之（0=已解锁），否则默认 10（ResolveUnlockCount）
    public static int ParseUnlockCount(string specialInfo)
    {
        if (int.TryParse(specialInfo?.Trim(), out var v) && v >= 0) return v;
        return DefaultUnlockCount;
    }

    #endregion

    #region 状态推进

    // 枚举全部合法操作：可移动且前缘不阻挡（Step 或 Exit）的猪
    public static IReadOnlyList<SlidePigMove> LegalMoves(SlidePigState state)
    {
        if (state.Failed) return Array.Empty<SlidePigMove>();
        var width = state.Level.MapWidth;
        var height = state.Level.MapHeight;
        var occ = BuildOccupancy(state, width, height, out var cageGrid);
        var moves = new List<SlidePigMove>(state.Pigs.Count);
        foreach (var p in state.Pigs)
        {
            if (!IsMovableInState(p, cageGrid, width)) continue;
            // 自身格子不构成阻挡：临时清除后再判前缘
            foreach (var c in p.Cells) occ[c.Y * width + c.X] = false;
            var verdict = JudgeFront(p, occ, width, height);
            foreach (var c in p.Cells) occ[c.Y * width + c.X] = true;
            if (verdict != FrontVerdict.Blocked) moves.Add(new SlidePigMove(p.Id));
        }
        return moves;
    }

    // 应用一次点击：完整结算滑行/碰撞/离场/级联；非法操作（不存在/不可动/被挡）抛 InvalidOperationException
    public static SlidePigState Apply(SlidePigState state, SlidePigMove move)
    {
        if (state.Failed) throw new InvalidOperationException("已处于失败终态，无合法操作");
        var mover = state.Find(move.PigId) ?? throw new InvalidOperationException($"猪 #{move.PigId} 不在场");
        var width = state.Level.MapWidth;
        var height = state.Level.MapHeight;
        var occ = BuildOccupancy(state, width, height, out var cageGrid);
        if (mover.Type is SlidePigType.Cage or SlidePigType.CageL)
        {
            throw new InvalidOperationException($"猪 #{mover.Id} 是铁笼，不可点击");
        }
        if (mover.Type == SlidePigType.Lock && mover.Counter > 0)
        {
            throw new InvalidOperationException($"猪 #{mover.Id} 未解锁（剩 {mover.Counter}）");
        }
        if (IsCaged(mover, cageGrid, width))
        {
            throw new InvalidOperationException($"猪 #{mover.Id} 被铁笼关住，不可起步");
        }
        // 临时清除自身占用再判定/滑行
        foreach (var c in mover.Cells) occ[c.Y * width + c.X] = false;

        // 被挡点击排除：前缘被占时点击在游戏里为原地反馈/弹回（净零或负收益），不构成合法操作
        if (JudgeFront(mover, occ, width, height) == FrontVerdict.Blocked)
        {
            throw new InvalidOperationException($"猪 #{mover.Id} 前缘被阻挡，点击不产生状态变化");
        }

        // 逐格滑行：前缘全越界 → 整猪离场；被占 → 停在贴靠格
        var cells = new (int X, int Y)[mover.Cells.Length];
        for (var i = 0; i < cells.Length; i++) cells[i] = mover.Cells[i];
        var (dx, dy) = Delta(mover.Facing);
        var probe = mover;                                        // 复用 record 更新脚印
        SlidePigEntity? blocker = null;
        var exited = false;
        while (true)
        {
            // 脚印初始已规范形，逐格平移不改变相对顺序，直接复制即可
            probe = probe with { Cells = cells.ToArray() };
            var verdict = JudgeFront(probe, occ, width, height);
            if (verdict == FrontVerdict.Exit) { exited = true; break; }
            if (verdict == FrontVerdict.Blocked)
            {
                blocker = FindBlocker(probe, state, mover.Id, dx, dy);
                break;
            }
            for (var i = 0; i < cells.Length; i++) cells[i] = (cells[i].X + dx, cells[i].Y + dy);
        }

        var finalCells = probe.Cells;
        if (exited)
        {
            // 离场：移除该猪并对其余 Lock/Cage 扣解锁数；铁笼归零自删（不触发离场事件，无级联）
            var remaining = new List<SlidePigEntity>(state.Pigs.Count);
            foreach (var p in state.Pigs)
            {
                if (p.Id == mover.Id) continue;
                if ((p.Type is SlidePigType.Lock or SlidePigType.Cage or SlidePigType.CageL) && p.Counter > 0)
                {
                    var c = p.Counter - 1;
                    // 铁笼归零即自删；锁猪归零仅变为可动
                    if ((p.Type is SlidePigType.Cage or SlidePigType.CageL) && c == 0) continue;
                    remaining.Add(p with { Counter = c });
                }
                else
                {
                    remaining.Add(p);
                }
            }
            remaining.Sort((a, b) => a.Id - b.Id);
            return new SlidePigState(remaining, false, state.Level);
        }

        // 停止（撞到 blocker）：碰撞回调——双方各自扣自己的爆炸计数（同帧等价）
        var moverBoomDelta = 0;
        var blockerBoomDelta = 0;
        if (blocker is not null)
        {
            if (mover.Type == SlidePigType.Boom) moverBoomDelta = -1;
            if (blocker.Type == SlidePigType.Boom) blockerBoomDelta = -1;
        }
        // 弹簧猪：撞猪后弹回原位（净位置不变，回调已生效）
        var restingCells = mover.Type == SlidePigType.Spring ? mover.Cells : finalCells;

        // 应用计数变化并判定失败（任一 Boom 归零即全局失败终态）
        var failed = false;
        var pigs = new List<SlidePigEntity>(state.Pigs.Count);
        foreach (var p in state.Pigs)
        {
            if (p.Id == mover.Id)
            {
                var counter = p.Counter + moverBoomDelta;
                if (p.Type == SlidePigType.Boom && counter <= 0) failed = true;
                pigs.Add(p with { Cells = restingCells, Counter = counter });
            }
            else if (p.Id == blocker?.Id)
            {
                var counter = p.Counter + blockerBoomDelta;
                if (p.Type == SlidePigType.Boom && counter <= 0) failed = true;
                pigs.Add(p with { Counter = counter });
            }
            else
            {
                pigs.Add(p);
            }
        }
        pigs.Sort((a, b) => a.Id - b.Id);
        return new SlidePigState(pigs, failed, state.Level);
    }

    // 胜利判定：未失败且场上无猪（铁笼归零自删后不在场）
    public static bool IsWin(SlidePigState state)
    {
        return !state.Failed && state.Pigs.Count == 0;
    }

    #endregion

    #region 内部工具

    // 线程级复用暂存：消除逐状态分配占用表造成的 GC 压力（Apply/LegalMoves 均无重入，并行批解下按线程隔离）
    [ThreadStatic]
    private static bool[]? _occScratch;                // 占用表暂存
    [ThreadStatic]
    private static bool[]? _cageScratch;               // 铁笼格暂存

    // 构建占用网格 + 铁笼格网格（笼与猪可叠放：笼格同时进两张表）；返回复用缓冲，用完即弃不得外传
    private static bool[] BuildOccupancy(SlidePigState state, int width, int height, out bool[] cageGrid)
    {
        var size = width * height;
        var occ = _occScratch;
        if (occ is null || occ.Length < size) occ = _occScratch = new bool[size];
        var cage = _cageScratch;
        if (cage is null || cage.Length < size) cage = _cageScratch = new bool[size];
        Array.Clear(occ, 0, size);
        Array.Clear(cage, 0, size);
        foreach (var p in state.Pigs)
        {
            foreach (var c in p.Cells)
            {
                occ[c.Y * width + c.X] = true;
                if (p.Type is SlidePigType.Cage or SlidePigType.CageL) cage[c.Y * width + c.X] = true;
            }
        }
        cageGrid = cage;
        return occ;
    }

    // 状态内可移动：基础规则 + 未被铁笼关住（脚印与任一铁笼格重叠即被关）
    private static bool IsMovableInState(SlidePigEntity e, bool[] cageGrid, int width)
    {
        if (!IsMovable(e)) return false;
        return !IsCaged(e, cageGrid, width);
    }

    // 是否被铁笼关住（任一脚印格与铁笼格重叠）
    private static bool IsCaged(SlidePigEntity e, bool[] cageGrid, int width)
    {
        foreach (var c in e.Cells)
        {
            if (cageGrid[c.Y * width + c.X]) return true;
        }
        return false;
    }

    // 在前缘目标格中找到被撞猪（多格前缘时任一被占格即命中）
    private static SlidePigEntity? FindBlocker(SlidePigEntity e, SlidePigState state, int moverId, int dx, int dy)
    {
        var axisMax = int.MinValue;
        for (var i = 0; i < e.Cells.Length; i++)
        {
            var v = dx != 0 ? e.Cells[i].X * dx : e.Cells[i].Y * dy;
            if (v > axisMax) axisMax = v;
        }
        for (var i = 0; i < e.Cells.Length; i++)
        {
            var c = e.Cells[i];
            var v = dx != 0 ? c.X * dx : c.Y * dy;
            if (v != axisMax) continue;
            var tx = c.X + dx;
            var ty = c.Y + dy;
            foreach (var p in state.Pigs)
            {
                if (p.Id == moverId) continue;
                for (var k = 0; k < p.Cells.Length; k++)
                {
                    if (p.Cells[k].X == tx && p.Cells[k].Y == ty) return p;
                }
            }
        }
        return null;
    }

    #endregion
}
