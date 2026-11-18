using System.Text.Json.Nodes;

namespace LevelForge.Adapters.PiggyFarm;

// 关卡合成器（去模板化）：从终局倒推一条合法解，构造出"天生可解"的关卡。
//
// 核心思想：正向解是 S0 --m1--> S1 --m2--> ... --mk--> 空盘。
// 从空盘出发倒着建 S_{k-1} … S0，每一步只做两种逆向操作：
//   Insert   —— 放一只"点一下就沿朝向离场"的新猪（正向：它就是该步离场的猪）；
//   PullBack —— 把一只"当前被挡住"的猪沿反朝向回拉 k 格（正向：它先滑 k 格、停在被挡处）。
// 逆向操作倒过来就是一条合法点击序列（见证解），因此可解性由构造保证，不靠求解器筛选。
//
// 为什么还要"结构化铺排"：
//   每只新猪插入时，它的正前方（到边界为止）必须没有已放置的猪——否则它点一下出不去。
//   纯随机撒点会在 60~70 只时就无处可放（射线全被挡住）；真实关卡密度能到 0.85，
//   靠的是"同一行/列的猪朝同一方向排成链"：后放的猪挡在前面，前放的猪射线是空的。
//   所以合成器先按行铺横链、再按列铺纵向通道（通道的猪会挡住横链，形成跨行依赖），
//   剩余空隙才用随机互锁布点补齐。
//
// 稠密关的尾巴：逆向最后插入的猪 = 正向最先离场的猪，它们要在几乎填满的棋盘上"点一下就走"，
// 只有"贴边朝外"的位置满足。因此开局就预留若干边界槽位，内层布点一律避开，最后再放尾巴猪。
public static class PiggyFarmSynthesizer
{
    public const int Width = 12;
    public const int Height = 19;

    private const int RandomProbeLimit = 48;

    // 成型计划：由 composer 按目标档位 + 风格采样给出
    public sealed record Plan(
        int PigCount,        // 目标猪数（放不下时按实际数量返回，由调用方判定是否够档）
        int Slides,          // 目标滑步数（解长 − 猪数），即"先挪再走"的强制步
        int TailCount,       // 预留的边界槽位数（正向最先离场的那批）
        int LaneCount,       // 纵向通道列数（跨行依赖的来源）
        double LongRatio,    // 大长猪占比（0~1）
        double RichRatio,    // 土豪猪占比（0~1）
        int Seed,
        int ReserveRich = 0, // 给"土豪猪 2×3"需求预留的空块数（合成完的棋盘很满，事后再找未必有）
        int ReserveLong = 0, // 给"大长猪 1×3"需求预留的空块数
        bool UseSpines = false); // 双边界"封口脊"：把每行横链的出口挡住，做出真实关卡的受阻比例

    // 产物：猪列表（Id 已按见证解首次点击顺序编号）+ 见证解（猪 Id 序列，可被适配层原样回放）
    public sealed record Result(IReadOnlyList<PiggyFarmLevelPig> Pigs, IReadOnlyList<int> Witness, int Slides)
    {
        public int PigCount => Pigs.Count;
    }

    public static Result? Synthesize(Plan plan)
    {
        if (plan.PigCount <= 0) return null;
        var rng = new Random(plan.Seed);
        var board = new Board();
        var ops = new List<Op>();

        // ① 预留边界槽位：这些格子内层布点不得占用（封口脊自己就是边界出口，不需要尾巴槽位）
        var tail = plan.UseSpines ? [] : PickTailSlots(rng, Math.Min(plan.TailCount, plan.PigCount));
        var reserved = new HashSet<GridCell>(tail.SelectMany(s => s.Cells));

        // ①b 需求预留块：土豪猪要 2×3、大长猪要 1×3，合成完的棋盘很满，事后再找未必有空块
        ReserveBlocks(rng, reserved, plan.ReserveRich, 2, 3);
        ReserveBlocks(rng, reserved, plan.ReserveLong, 1, 3);

        // ② 纵向通道：这些列的格子留给"竖着排"的猪，横排不占用。
        //    封口脊模式直接把两侧边列当通道：每行横链的出口被脊上的猪挡住，只有脊读空的行才可直接离场。
        var lanes = plan.UseSpines ? new HashSet<int> { 0, Width - 1 } : PickLanes(rng, plan.LaneCount);

        var interiorTarget = Math.Max(0, plan.PigCount - tail.Count);

        // ③ 结构化铺排（横链 → 纵链/封口脊）→ 随机互锁补齐。
        //    横链只吃掉 85% 预算，剩下的留给纵链与随机布点——否则小关会被横链填满，
        //    朝向塌成两种（质量门要求 ≥3 种），而且通道根本轮不到铺。
        var rowBudget = Math.Max(0, interiorTarget * (plan.UseSpines ? 88 : 85) / 100);
        FillRows(board, rng, reserved, lanes, plan, rowBudget, ops);
        var rowPigs = board.Pigs.Count;
        if (plan.UseSpines)
        {
            FillSpines(board, rng, reserved, plan, interiorTarget, ops);
        }
        else
        {
            FillLanes(board, rng, reserved, lanes, plan, interiorTarget, ops);
        }
        var lanePigs = board.Pigs.Count - rowPigs;
        FillRandomly(board, rng, reserved, plan, interiorTarget, ops);
        var randomPigs = board.Pigs.Count - rowPigs - lanePigs;
        if (Environment.GetEnvironmentVariable("PIGGYFARM_SYNTH_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[phase] rows={rowPigs} lanes={lanePigs} random={randomPigs}");
        }

        // ③b 朝向兜底：小关铺两行横链后可能只剩两种朝向，质量门要求 ≥3 种——补一只缺朝向的猪
        EnsureFacings(board, rng, reserved, plan, ops);

        // ④ 滑步：把若干"被挡住"的猪回拉一格，制造"先挪再走"的次序
        var slides = 0;
        for (var i = 0; i < plan.Slides * 4 && slides < plan.Slides; i++)
        {
            if (!TryPullBack(board, rng, reserved, out var pulled)) continue;
            ops.Add(new Op(pulled, Exits: false));
            slides++;
        }

        // ⑤ 尾巴：贴边朝外的猪，视线长度为零，无论棋盘多满都放得下
        foreach (var slot in tail)
        {
            if (slot.Cells.Any(c => board.Owner.ContainsKey(c))) continue;
            var index = board.Add("Normal", slot.Facing, slot.Cells);
            ops.Add(new Op(index, Exits: true));
        }

        if (board.Pigs.Count == 0) return null;
        Debug(board, plan, tail.Count, reserved, slides);

        // ⑥ 见证解 = 逆向操作倒序；猪 Id 按"首次被点击"的先后编号（让求解器的贪心沿见证走）
        var witness = new List<int>(ops.Count);
        for (var i = ops.Count - 1; i >= 0; i--) witness.Add(ops[i].PigIndex);

        var idOf = new int[board.Pigs.Count];
        Array.Fill(idOf, -1);
        var nextId = 0;
        foreach (var index in witness)
        {
            if (idOf[index] < 0) idOf[index] = nextId++;
        }
        for (var i = 0; i < idOf.Length; i++)
        {
            if (idOf[i] < 0) idOf[i] = nextId++;   // 未被点击的猪（理论上不存在）补号，避免 id 空洞
        }

        var pigs = new PiggyFarmLevelPig[board.Pigs.Count];
        for (var i = 0; i < board.Pigs.Count; i++)
        {
            var pig = board.Pigs[i];
            pigs[idOf[i]] = new PiggyFarmLevelPig(
                idOf[i],
                pig.Facing,
                pig.Type,
                Sorted(pig.Cells),
                Counter: pig.Type == "Boom" ? 3 : 0,
                Color: -1);
        }

        return new Result(pigs, witness.Select(i => idOf[i]).ToArray(), slides);
    }

    #region 结构化铺排

    // 横链：每行铺一串同朝向的猪。按朝向的"后方"往"前方"铺，
    // 保证每只新猪的正前方此刻还是空的（射线畅通），后放的猪正好挡住先放的猪。
    private static void FillRows(
        Board board, Random rng, HashSet<GridCell> reserved, HashSet<int> lanes, Plan plan, int stopAt, List<Op> ops)
    {
        var (placed, attempts, blocked) = (0, 0, 0);
        foreach (var y in Shuffled(Enumerable.Range(0, Height), rng))
        {
            if (board.Pigs.Count >= stopAt) break;
            if (rng.NextDouble() < 0.06) continue;   // 偶尔整行留空，避免千篇一律的条纹
            if (rng.NextDouble() < 0.22 && Width >= 8)
            {
                // 分叉行：以随机分界把一行拆成两段相背的链（左段朝左、右段朝右），
                // 两段的射线各自朝外互不干扰——比整行同向更像真实关卡的朝向混合
                var split = rng.Next(2, Width - 2);
                var (p1, a1, b1) = FillRowSegment(board, rng, reserved, lanes, y, PigFacing.Left, split - 1, -1, stopAt, ops);
                var (p2, a2, b2) = FillRowSegment(board, rng, reserved, lanes, y, PigFacing.Right, split, 1, stopAt, ops);
                placed += p1 + p2;
                attempts += a1 + a2;
                blocked += b1 + b2;
            }
            else
            {
                var right = rng.NextDouble() < 0.5;
                var (p, a, b) = FillRowSegment(
                    board, rng, reserved, lanes, y,
                    right ? PigFacing.Right : PigFacing.Left,
                    right ? 0 : Width - 1,
                    right ? 1 : -1,
                    stopAt, ops);
                placed += p;
                attempts += a;
                blocked += b;
            }
        }
        if (Environment.GetEnvironmentVariable("PIGGYFARM_SYNTH_DEBUG") == "1")
        {
            Console.Error.WriteLine($"[rows] placed={placed} attempts={attempts} blocked={blocked} lanes=[{string.Join(",", lanes.OrderBy(v => v))}]");
        }
    }

    // 沿一行从 startX 按 step 方向铺同朝向的猪：每只新猪的正前方此刻都还是空的
    private static (int Placed, int Attempts, int Blocked) FillRowSegment(
        Board board, Random rng, HashSet<GridCell> reserved, HashSet<int> lanes, int y,
        PigFacing facing, int startX, int step, int stopAt, List<Op> ops)
    {
        var placed = 0;
        var attempts = 0;
        var blocked = 0;
        var x = startX;
        while (x + step >= 0 && x + step < Width)
        {
            if (board.Pigs.Count >= stopAt) break;
            var a = new GridCell(x, y);
            var b = new GridCell(x + step, y);
            if (!Usable(board, reserved, lanes, a) || !Usable(board, reserved, lanes, b))
            {
                x += step;
                continue;
            }
            attempts++;
            var cells = Footprint("Normal", facing, b);
            if (!IsPlacementValid(board, cells, reserved) || !RayClear(board, cells, facing))
            {
                blocked++;
                x += step;
                continue;
            }
            // 随机断链：偶尔空一格，让整行不是一条笔直的链（口袋/短链更像真实关卡，不是条形码）
            if (rng.NextDouble() < 0.04)
            {
                x += step;
                continue;
            }
            placed++;
            ops.Add(new Op(board.Add("Normal", facing, cells), Exits: true));
            x += step * 2;
        }
        return (placed, attempts, blocked);
    }

    // 纵链：通道列里从一端铺到另一端；这些猪会挡住横链的射线，形成跨行依赖
    private static void FillLanes(
        Board board, Random rng, HashSet<GridCell> reserved, HashSet<int> lanes, Plan plan, int stopAt, List<Op> ops)
    {
        foreach (var x in Shuffled(lanes, rng))
        {
            if (board.Pigs.Count >= stopAt) return;
            var down = rng.NextDouble() < 0.5;
            var facing = down ? PigFacing.Down : PigFacing.Up;
            var step = down ? 1 : -1;
            var y = down ? 0 : Height - 1;
            while (y + step >= 0 && y + step < Height)
            {
                if (board.Pigs.Count >= stopAt) return;
                var a = new GridCell(x, y);
                var b = new GridCell(x, y + step);
                // 通道列本身要允许占用（其它列不参与），只排除已占格与保留槽位
                if (board.Owner.ContainsKey(a) || board.Owner.ContainsKey(b)
                    || reserved.Contains(a) || reserved.Contains(b))
                {
                    y += step;
                    continue;
                }
                var head = b;
                var cells = Footprint("Normal", facing, head);
                if (!IsPlacementValid(board, cells, reserved) || !RayClear(board, cells, facing))
                {
                    y += step;
                    continue;
                }
                ops.Add(new Op(board.Add("Normal", facing, cells), Exits: true));
                y += step * 2;
            }
        }
    }

    // 封口脊：把左右两条边列铺成竖链，横链的出口就被脊上的猪挡住——
    // 这一步把"初始可直接离场"从 ~30% 压到真实关卡量级（8~19%）。
    // 每行按 capProbability 决定封不封口：留空的行，横链头猪可以直接离场（这就是难度旋钮）。
    private static void FillSpines(
        Board board, Random rng, HashSet<GridCell> reserved, Plan plan, int stopAt, List<Op> ops)
    {
        const double capProbability = 0.65;
        foreach (var x in Shuffled(new[] { 0, Width - 1 }, rng))
        {
            if (board.Pigs.Count >= stopAt) break;
            var down = rng.NextDouble() < 0.5;
            var facing = down ? PigFacing.Down : PigFacing.Up;
            var step = down ? 1 : -1;
            var y = down ? 0 : Height - 1;
            while (y + step >= 0 && y + step < Height)
            {
                if (board.Pigs.Count >= stopAt) return;
                if (rng.NextDouble() > capProbability)
                {
                    y += step * 2;   // 这一行不封口：横链头猪保持可走
                    continue;
                }
                var a = new GridCell(x, y);
                var b = new GridCell(x, y + step);
                if (board.Owner.ContainsKey(a) || board.Owner.ContainsKey(b)
                    || reserved.Contains(a) || reserved.Contains(b))
                {
                    y += step;
                    continue;
                }
                var cells = Footprint("Normal", facing, b);
                if (!IsPlacementValid(board, cells, reserved) || !RayClear(board, cells, facing))
                {
                    y += step;
                    continue;
                }
                ops.Add(new Op(board.Add("Normal", facing, cells), Exits: true));
                y += step * 2;
            }
        }
    }

    // 剩余空隙：随机互锁布点（优先放进已有猪的射线里，制造依赖）
    private static void FillRandomly(        Board board, Random rng, HashSet<GridCell> reserved, Plan plan, int stopAt, List<Op> ops)
    {
        var stall = 0;
        while (board.Pigs.Count < stopAt && stall < 200)
        {
            if (TryInsert(board, rng, reserved, plan, out var index))
            {
                ops.Add(new Op(index, Exits: true));
                stall = 0;
                continue;
            }
            stall++;
        }
    }

    // 朝向兜底：质量门要求 ≥3 种朝向。缺哪种就用哪种朝向补一只（小关横链只铺出两种朝向时兜底）
    private static void EnsureFacings(Board board, Random rng, HashSet<GridCell> reserved, Plan plan, List<Op> ops)
    {
        var present = board.Pigs.Select(p => p.Facing).Distinct().ToHashSet();
        foreach (var facing in Facings)
        {
            if (present.Contains(facing)) continue;
            if (!TryInsertWithFacing(board, rng, reserved, plan, facing, out var index)) continue;
            ops.Add(new Op(index, Exits: true));
            present.Add(facing);
        }
    }

    // 指定朝向放一只普通猪（先按"互锁优先"打分，取最优合法位置）
    // 只补普通猪：朝向兜底不能顺手塞进"没被要求的特殊猪"（需求即契约）
    private static bool TryInsertWithFacing(
        Board board, Random rng, HashSet<GridCell> reserved, Plan plan, PigFacing facing, out int index)
    {
        index = -1;
        Candidate? best = null;
        var bestScore = double.MinValue;
        foreach (var head in board.FreeCells(reserved))
        {
            var cells = Footprint("Normal", facing, head);
            if (!IsPlacementValid(board, cells, reserved)) continue;
            if (!RayClear(board, cells, facing)) continue;
            var score = Score(board, cells, facing, rng);
            if (score <= bestScore) continue;
            bestScore = score;
            best = new Candidate("Normal", facing, cells);
        }
        if (best is null) return false;
        index = board.Add(best.Type, best.Facing, best.Cells);
        return true;
    }

    private static bool Usable(Board board, HashSet<GridCell> reserved, HashSet<int> lanes, GridCell cell) =>
        !board.Owner.ContainsKey(cell) && !reserved.Contains(cell) && !lanes.Contains(cell.X);

    private static HashSet<int> PickLanes(Random rng, int count)
    {
        var lanes = new HashSet<int>();
        var candidates = Enumerable.Range(2, Math.Max(0, Width - 4)).OrderBy(_ => rng.Next()).ToArray();
        foreach (var column in candidates)
        {
            if (lanes.Count >= count) break;
            if (lanes.Any(c => Math.Abs(c - column) <= 1)) continue;   // 通道不相邻，避免把棋盘切得太碎
            lanes.Add(column);
        }
        return lanes;
    }

    #endregion

    #region 布点（Insert）

    // 放一只"点一下就离场"的猪：脚印在界内且空格、且朝向直线到边界为止没有已放置的猪。
    // 布点偏好：① 互锁（挡住已有猪）② 朝向均衡 ③ 贴邻成块
    private static bool TryInsert(Board board, Random rng, HashSet<GridCell> reserved, Plan plan, out int index)
    {
        index = -1;
        var free = board.FreeCells(reserved);
        if (free.Count == 0) return false;

        Candidate? best = null;
        var bestScore = double.MinValue;

        for (var probe = 0; probe < RandomProbeLimit; probe++)
        {
            var type = PickType(rng, plan);
            var facing = (PigFacing)rng.Next(4);
            var head = free[rng.Next(free.Count)];
            var cells = Footprint(type, facing, head);
            if (!IsPlacementValid(board, cells, reserved)) continue;
            if (!RayClear(board, cells, facing)) continue;
            var score = Score(board, cells, facing, rng);
            if (score <= bestScore) continue;
            bestScore = score;
            best = new Candidate(type, facing, cells);
        }

        if (best is null)
        {
            foreach (var head in free)
            {
                foreach (var facing in Facings)
                {
                    var type = PickType(rng, plan);
                    var cells = Footprint(type, facing, head);
                    if (!IsPlacementValid(board, cells, reserved)) continue;
                    if (!RayClear(board, cells, facing)) continue;
                    var score = Score(board, cells, facing, rng);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    best = new Candidate(type, facing, cells);
                }
            }
        }

        if (best is null) return false;
        index = board.Add(best.Type, best.Facing, best.Cells);
        return true;
    }

    private sealed record Candidate(string Type, PigFacing Facing, IReadOnlyList<GridCell> Cells);

    // 布点评分：互锁（挡住几只已有猪）为主，朝向均衡与贴邻为辅
    private static double Score(Board board, IReadOnlyList<GridCell> cells, PigFacing facing, Random rng)
    {
        var cellsSet = new HashSet<GridCell>(cells);
        var blocks = 0;
        var facingCount = 0;
        foreach (var pig in board.Pigs)
        {
            if (pig.Facing == facing) facingCount++;
            if (RayHits(board, pig, cellsSet)) blocks++;
        }
        var neighbors = 0;
        foreach (var cell in cells)
        {
            foreach (var (dx, dy) in Neighbors)
            {
                if (board.Owner.ContainsKey(new GridCell(cell.X + dx, cell.Y + dy))) neighbors++;
            }
        }
        var balance = -6.0 * facingCount;   // 该朝向用得越多分越低，保证 ≥3 种朝向
        return blocks * 100.0 + neighbors * 3.0 + balance + rng.NextDouble() * 4.0;
    }

    // 已有猪 pig 的正向射线是否穿过给定格子集合
    private static bool RayHits(Board board, Board.Pig pig, HashSet<GridCell> targets)
    {
        var (dx, dy) = Delta(pig.Facing);
        var current = pig.Cells.ToArray();
        for (var step = 0; step < Width + Height; step++)
        {
            var next = new GridCell[current.Length];
            var exits = false;
            for (var i = 0; i < current.Length; i++)
            {
                next[i] = new GridCell(current[i].X + dx, current[i].Y + dy);
                if (!InBounds(next[i])) exits = true;
            }
            foreach (var cell in next)
            {
                if (InBounds(cell) && targets.Contains(cell)) return true;
            }
            if (exits) return false;
            current = next;
        }
        return false;
    }

    private static readonly (int Dx, int Dy)[] Neighbors = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    // 脚印逐格落位：全在界内、全空、且不占用保留槽位
    private static bool IsPlacementValid(Board board, IReadOnlyList<GridCell> cells, HashSet<GridCell> reserved)
    {
        foreach (var cell in cells)
        {
            if (!InBounds(cell) || board.Owner.ContainsKey(cell) || reserved.Contains(cell)) return false;
        }
        return true;
    }

    // 正向"点一下"必须一路走到离场（中途不被任何已放置的猪挡住）
    private static bool RayClear(Board board, IReadOnlyList<GridCell> cells, PigFacing facing)
    {
        var (dx, dy) = Delta(facing);
        var current = cells.ToArray();
        for (var step = 0; step < Width + Height; step++)
        {
            var next = new GridCell[current.Length];
            for (var i = 0; i < current.Length; i++)
            {
                next[i] = new GridCell(current[i].X + dx, current[i].Y + dy);
            }
            // 顺序与适配层 SimulateSlide 一致：先判"界内格被占"（挡住），再判"任一格越界"（离场）
            var blocked = false;
            var exits = false;
            foreach (var cell in next)
            {
                if (InBounds(cell))
                {
                    if (board.Owner.ContainsKey(cell)) blocked = true;
                }
                else
                {
                    exits = true;
                }
            }
            if (blocked) return false;
            if (exits) return true;
            current = next;
        }
        return false;
    }

    #endregion

    #region 回拉（PullBack）

    // 把一只"当前被挡住"的猪沿反朝向回拉 k 格：正向它就会先滑 k 格、停回原位（多一次点击）
    private static bool TryPullBack(Board board, Random rng, HashSet<GridCell> reserved, out int pigIndex)
    {
        pigIndex = -1;
        var blocked = board.Pigs.Where(p => !CanAdvance(board, p)).ToList();
        if (blocked.Count == 0) return false;
        Shuffle(blocked, rng);

        foreach (var pig in blocked.Take(10))
        {
            var (dx, dy) = Delta(pig.Facing);
            var options = new List<(int K, List<GridCell> Cells)>();
            for (var k = 1; k <= 8; k++)
            {
                var cells = pig.Cells.Select(c => new GridCell(c.X - dx * k, c.Y - dy * k)).ToList();
                var ok = true;
                foreach (var cell in cells)
                {
                    if (!InBounds(cell) || reserved.Contains(cell)) { ok = false; break; }
                    if (board.Owner.TryGetValue(cell, out var owner) && owner != pig.Index) { ok = false; break; }
                }
                if (!ok) break;   // 再往后必然越界/重叠
                options.Add((k, cells));
            }
            if (options.Count == 0) continue;

            var (_, target) = options[rng.Next(options.Count)];
            if (!SlideStopsAt(board, pig, target, pig.Cells)) continue;

            board.Move(pig, target);
            pigIndex = pig.Index;
            return true;
        }
        return false;
    }

    // 从 from 出发按朝向滑行，是否恰好停在 stop（未离场）
    private static bool SlideStopsAt(Board board, Board.Pig pig, IReadOnlyList<GridCell> from, IReadOnlyList<GridCell> stop)
    {
        var (dx, dy) = Delta(pig.Facing);
        var current = from.ToArray();
        for (var step = 0; step < Width + Height; step++)
        {
            var next = current.Select(c => new GridCell(c.X + dx, c.Y + dy)).ToArray();
            var blocked = false;
            var exits = false;
            foreach (var cell in next)
            {
                if (InBounds(cell))
                {
                    if (board.Owner.TryGetValue(cell, out var owner) && owner != pig.Index) blocked = true;
                }
                else
                {
                    exits = true;
                }
            }
            if (blocked) return SameCells(current, stop);
            if (exits) return false;
            current = next;
        }
        return false;
    }

    private static bool CanAdvance(Board board, Board.Pig pig)
    {
        var (dx, dy) = Delta(pig.Facing);
        foreach (var cell in pig.Cells)
        {
            var next = new GridCell(cell.X + dx, cell.Y + dy);
            if (InBounds(next) && board.Owner.TryGetValue(next, out var owner) && owner != pig.Index) return false;
        }
        return true;
    }

    #endregion

    #region 尾巴槽位

    // 预留边界槽位：贴边朝外、一步即离场的 1×2 位置
    private static List<TailSlot> PickTailSlots(Random rng, int count)
    {
        var candidates = new List<TailSlot>();
        for (var x = 0; x < Width; x++)
        {
            candidates.Add(new TailSlot(PigFacing.Up, Footprint("Normal", PigFacing.Up, new GridCell(x, 0))));
            candidates.Add(new TailSlot(PigFacing.Down, Footprint("Normal", PigFacing.Down, new GridCell(x, Height - 1))));
        }
        for (var y = 0; y < Height; y++)
        {
            candidates.Add(new TailSlot(PigFacing.Left, Footprint("Normal", PigFacing.Left, new GridCell(0, y))));
            candidates.Add(new TailSlot(PigFacing.Right, Footprint("Normal", PigFacing.Right, new GridCell(Width - 1, y))));
        }
        Shuffle(candidates, rng);

        var chosen = new List<TailSlot>(count);
        var used = new HashSet<GridCell>();
        foreach (var candidate in candidates)
        {
            if (chosen.Count >= count) break;
            if (candidate.Cells.Any(used.Contains)) continue;
            chosen.Add(candidate);
            foreach (var cell in candidate.Cells) used.Add(cell);
        }
        return chosen;
    }

    private sealed record TailSlot(PigFacing Facing, IReadOnlyList<GridCell> Cells);

    // 预留若干空块（w×h，随机旋转），供需求布点（土豪猪/大长猪）落位
    private static void ReserveBlocks(Random rng, HashSet<GridCell> reserved, int count, int w, int h)
    {
        for (var i = 0; i < count; i++)
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                var flip = rng.NextDouble() < 0.5;
                var bw = flip ? h : w;
                var bh = flip ? w : h;
                var x = rng.Next(0, Width - bw + 1);
                var y = rng.Next(0, Height - bh + 1);
                var cells = new List<GridCell>(bw * bh);
                for (var dx = 0; dx < bw; dx++)
                {
                    for (var dy = 0; dy < bh; dy++)
                    {
                        cells.Add(new GridCell(x + dx, y + dy));
                    }
                }
                if (cells.Any(reserved.Contains)) continue;
                foreach (var cell in cells) reserved.Add(cell);
                break;
            }
        }
    }

    #endregion

    #region 几何与序列化

    public static IReadOnlyList<GridCell> Footprint(string type, PigFacing facing, GridCell head)
    {
        var (dx, dy) = Delta(facing);
        var length = type switch { "Long" or "BalloonLong" => 3, "RichPig" => 3, _ => 2 };
        var width = type == "RichPig" ? 2 : 1;
        var (px, py) = (dy, dx);   // 垂直朝向前进方向的一侧：Right → (0,1)，Up → (-1,0)
        var cells = new List<GridCell>(length * width);
        for (var i = 0; i < length; i++)
        {
            for (var j = 0; j < width; j++)
            {
                cells.Add(new GridCell(head.X - dx * i + px * j, head.Y - dy * i + py * j));
            }
        }
        return cells;
    }

    public static string Serialize(IReadOnlyList<PiggyFarmLevelPig> pigs, int levelId, int difficulty, string levelName)
    {
        var array = new JsonArray();
        foreach (var pig in pigs)
        {
            var cells = new JsonArray();
            foreach (var cell in pig.Cells)
            {
                cells.Add(new JsonObject { ["x"] = cell.X, ["y"] = cell.Y });
            }
            array.Add(new JsonObject
            {
                ["id"] = pig.Id,
                ["facing"] = pig.Facing.ToString(),
                ["pigType"] = pig.Type,
                ["specialInfo"] = pig.Type switch
                {
                    "Boom" => "3",
                    "Scissors" or "Balloon" or "BalloonLong" => (pig.Color >= 0 ? pig.Color : 0).ToString(),
                    _ => "",
                },
                ["occupiedCells"] = cells,
            });
        }
        var node = new JsonObject
        {
            ["levelId"] = levelId,
            ["difficulty"] = difficulty,
            ["levelName"] = levelName,
            ["mapWidth"] = Width,
            ["mapHeight"] = Height,
            ["pigCount"] = pigs.Count,
            ["pigs"] = array,
            ["terrainElements"] = new JsonArray(),
            ["specialData"] = new JsonObject
            {
                ["rabbit"] = new JsonObject
                {
                    ["firstAppearStep"] = 0,
                    ["subsequentAppearStep"] = 0,
                    ["rabbitCountPerWave"] = 0,
                },
                ["pigSkin"] = new JsonObject { ["skinType"] = "None", ["skinRatio"] = 0.0 },
            },
        };
        return node.ToJsonString();
    }

    #endregion

    #region 内部类型与工具

    private sealed record Op(int PigIndex, bool Exits);

    private static readonly PigFacing[] Facings = [PigFacing.Up, PigFacing.Down, PigFacing.Left, PigFacing.Right];

    internal static (int Dx, int Dy) Delta(PigFacing facing) => facing switch
    {
        PigFacing.Up => (0, -1),
        PigFacing.Down => (0, 1),
        PigFacing.Left => (-1, 0),
        _ => (1, 0),
    };

    private static bool InBounds(GridCell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

    private static string PickType(Random rng, Plan plan)
    {
        var roll = rng.NextDouble();
        if (roll < plan.RichRatio) return "RichPig";
        if (roll < plan.RichRatio + plan.LongRatio) return "Long";
        return "Normal";
    }

    private static bool SameCells(IReadOnlyList<GridCell> a, IReadOnlyList<GridCell> b)
    {
        if (a.Count != b.Count) return false;
        var setA = new HashSet<GridCell>(a);
        return b.All(setA.Contains);
    }

    private static List<GridCell> Sorted(IEnumerable<GridCell> cells) =>
        cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static IEnumerable<T> Shuffled<T>(IEnumerable<T> source, Random rng)
    {
        var list = source.ToList();
        Shuffle(list, rng);
        return list;
    }

    private static void Debug(Board board, Plan plan, int tailCount, HashSet<GridCell> reserved, int slides)
    {
        if (Environment.GetEnvironmentVariable("PIGGYFARM_SYNTH_DEBUG") != "1") return;
        var free = board.FreeCells(reserved);
        Console.Error.WriteLine(
            $"[synth] pigs={board.Pigs.Count}/{plan.PigCount} tail={tailCount} free={free.Count} slides={slides}");
    }

    // 构造期棋盘：格 → 猪号索引 + 猪列表（顺序即创建顺序，稳定）
    private sealed class Board
    {
        public readonly Dictionary<GridCell, int> Owner = new();

        public readonly List<Pig> Pigs = new();

        public sealed class Pig
        {
            public int Index;
            public string Type = "Normal";
            public PigFacing Facing;
            public List<GridCell> Cells = new();
        }

        public int Add(string type, PigFacing facing, IReadOnlyList<GridCell> cells)
        {
            var pig = new Pig { Index = Pigs.Count, Type = type, Facing = facing, Cells = cells.ToList() };
            Pigs.Add(pig);
            foreach (var cell in pig.Cells) Owner[cell] = pig.Index;
            return pig.Index;
        }

        public void Move(Pig pig, IReadOnlyList<GridCell> cells)
        {
            foreach (var cell in pig.Cells) Owner.Remove(cell);
            pig.Cells = cells.ToList();
            foreach (var cell in pig.Cells) Owner[cell] = pig.Index;
        }

        public List<GridCell> FreeCells(HashSet<GridCell> reserved)
        {
            var free = new List<GridCell>(Width * Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var cell = new GridCell(x, y);
                    if (!Owner.ContainsKey(cell) && !reserved.Contains(cell)) free.Add(cell);
                }
            }
            return free;
        }
    }

    #endregion
}
