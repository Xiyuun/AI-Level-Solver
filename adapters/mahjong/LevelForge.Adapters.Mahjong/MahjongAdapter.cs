using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.Mahjong;

// 七成员游戏适配器 + 执行协议 + 可采纳启发式（一个实例实现三接口，门面组装为 AdapterBundle）
//
// 求解语义 = 游戏 LevelSolvabilitySolver 的官方抽象（唯一权威来源，逐字移植）：
//   - POP / FLIP 当普通棋参与消除（无视爆竹随机连锁、无视翻面正反面与全局翻转）
//   - ICE 未解冻（specialInfo>0）不可拖/不可消/不可推；每次消除事件全体冰块计数 -2，归零解冻转普通
//   - CONT 同 specialInfo 为一组，整组刚性同位移；推动链遇 CONT 整组入列并按组前沿延伸
//   - WALL 不可拖/不可消/不可推，不计入清盘胜利条件
//   - 走法 = {sourceId, targetId, landingX, landingY}：源棋可达落点 × 虚拟盘消除候选（排除本链棋子）
//   - 胜利 = 非墙棋子清空（WallChessController.countVictoryRemaining == 0）
// 覆盖边界（如实声明，详见规则矩阵）：运行时的爆竹随机连锁、翻面全局翻转与倒计时不在求解语义内。
public sealed class MahjongAdapter
    : IGameAdapter<MahjongLevel, MahjongState, MahjongMove>,
      IExecutionProtocol<MahjongState, MahjongMove>,
      IHeuristicAdapter<MahjongState>
{
    public const string AdapterVersionValue = "1.0.0";

    // ─── 七成员 ───

    // 项目原生关卡 → 初始状态（非法数据抛异常，禁止静默修复）
    public MahjongState LoadLevel(MahjongLevel level)
    {
        var pieces = level.Chesses
            .Where(c => c.Type > 0)   // type=0 = 永久空位（initFromPreloaded 同口径：不生成棋子）
            .Select(c => new MahjongPiece(c.Id, c.X, c.Y, c.Type, c.SpecialType, c.SpecialInfo))
            .OrderBy(p => p.Id)
            .ToList();
        var walls = level.Obstacles
            .Select(o => new MahjongWall(o.X, o.Y, o.Dir))
            .OrderBy(w => w.Y).ThenBy(w => w.X).ThenBy(w => w.Dir)
            .ToList();
        return new MahjongState(level.Level, level.Difficulty, level.MapWidth, level.MapHeight, pieces, walls);
    }

    // 枚举当前局面全部消除走法（顺序稳定：按棋子 id、方向序、落点距离序，与游戏官方 enumerateMoves 一致）
    public IReadOnlyList<MahjongMove> GetLegalMoves(MahjongState state)
    {
        var moves = new List<MahjongMove>();
        var seen = new HashSet<string>();
        var view = BoardView.Build(state);
        foreach (var chess in state.Pieces)
        {
            if (!CanMatch(chess))
            {
                continue;
            }
            foreach (var pos in ReachablePositions(state, view, chess))
            {
                var dx = pos.X - chess.X;
                var dy = pos.Y - chess.Y;
                var steps = Math.Abs(dx) + Math.Abs(dy);
                var moveDir = DirFromDelta(dx, dy);
                var movedPlacements = steps > 0 && moveDir is not null
                    ? BuildDragPlacements(state, view, chess, pos.X, pos.Y, moveDir.Value)
                    : null;
                var matches = ReleaseMatchCandidates(state, view, chess, pos.X, pos.Y, movedPlacements);
                foreach (var target in matches)
                {
                    var key = $"{chess.Id}>{target.Id}@{pos.X},{pos.Y}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }
                    moves.Add(new MahjongMove(chess.Id, target.Id, pos.X, pos.Y));
                }
            }
        }
        return moves;
    }

    // 纯函数：placements 应用（连排组与推动链同位移，目标不参与位移）→ 移除对 → 冰块计数递减
    public MahjongState ApplyMove(MahjongState state, MahjongMove move)
    {
        var source = state.Pieces.FirstOrDefault(p => p.Id == move.SourceId);
        var target = state.Pieces.FirstOrDefault(p => p.Id == move.TargetId);
        if (source is null || target is null)
        {
            throw new ArgumentException($"操作引用了场上不存在的棋子：source={move.SourceId}, target={move.TargetId}");
        }

        var preView = BoardView.Build(state);
        var dx = move.LandingX - source.X;
        var dy = move.LandingY - source.Y;
        var steps = Math.Abs(dx) + Math.Abs(dy);
        var moveDir = DirFromDelta(dx, dy);

        var removed = new HashSet<int> { move.SourceId, move.TargetId };
        var pieces = new List<MahjongPiece>();
        if (steps > 0 && moveDir is not null)
        {
            var placements = BuildDragPlacements(state, preView, source, move.LandingX, move.LandingY, moveDir.Value);
            var landingById = placements.ToDictionary(p => p.Piece.Id, p => (p.LandingX, p.LandingY));
            foreach (var piece in state.Pieces)
            {
                if (removed.Contains(piece.Id))
                {
                    continue;
                }
                if (landingById.TryGetValue(piece.Id, out var landing))
                {
                    pieces.Add(piece with { X = landing.Item1, Y = landing.Item2 });
                }
                else
                {
                    pieces.Add(piece);
                }
            }
        }
        else
        {
            pieces.AddRange(state.Pieces.Where(p => !removed.Contains(p.Id)));
        }

        // 消除事件（枚数恒为 2）：全体未解冻冰块计数 -2，归零解冻转普通（游戏 applyMove 同口径）
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.SpecialType == MahjongSpecial.Ice && piece.SpecialInfo > 0)
            {
                var info = Math.Max(0, piece.SpecialInfo - 2);
                pieces[i] = info <= 0
                    ? piece with { SpecialType = MahjongSpecial.Normal, SpecialInfo = 0 }
                    : piece with { SpecialInfo = info };
            }
        }

        return state.With(pieces.OrderBy(p => p.Id).ToList());
    }

    // 胜利判定：非墙棋子清空（与 WallChessController.countVictoryRemaining 一致）
    public bool IsGoal(MahjongState state)
    {
        return VictoryRemaining(state) == 0;
    }

    // 规范化状态键：与游戏官方 hashState 同口径（按 id 排序拼接；FLIP/POP 的特殊维度归并为普通）
    // 另带棋盘尺寸与墙体（同关内墙体恒定，兜底防跨关撞键）
    public string StateKey(MahjongState state)
    {
        var parts = new List<string>(state.Pieces.Count);
        foreach (var p in state.Pieces)
        {
            var special = p.SpecialType is MahjongSpecial.Flip or MahjongSpecial.Pop ? MahjongSpecial.Normal : p.SpecialType;
            var info = p.SpecialType is MahjongSpecial.Flip or MahjongSpecial.Pop ? 0 : p.SpecialInfo;
            parts.Add($"{p.Id}:{p.X},{p.Y},{p.Type},{special},{info}");
        }
        var walls = string.Join(";", state.Walls.Select(w => $"{w.X},{w.Y},{w.Dir}"));
        return $"{state.Width}x{state.Height};w={walls};{string.Join("|", parts)}";
    }

    // 人类可读描述（磁带展示用；禁止反向解析）
    public string DescribeMove(MahjongState state, MahjongMove move)
    {
        var source = state.Pieces.FirstOrDefault(p => p.Id == move.SourceId);
        var target = state.Pieces.FirstOrDefault(p => p.Id == move.TargetId);
        var sourceName = source is null ? $"#{move.SourceId}" : $"#{source.Id}(牌面{source.Type})";
        var targetName = target is null ? $"#{move.TargetId}" : $"#{target.Id}(牌面{target.Type})";
        var steps = source is null ? 0 : Math.Abs(move.LandingX - source.X) + Math.Abs(move.LandingY - source.Y);
        var chain = 0;
        if (source is not null && steps > 0)
        {
            var view = BoardView.Build(state);
            var dir = DirFromDelta(move.LandingX - source.X, move.LandingY - source.Y);
            if (dir is not null)
            {
                chain = BuildDragPlacements(state, view, source, move.LandingX, move.LandingY, dir.Value).Count - 1;
            }
        }
        var via = steps == 0
            ? "原位点击"
            : $"拖动至 ({move.LandingX},{move.LandingY})（带动 {chain} 枚）";
        return $"消除 {sourceName} 与 {targetName}：{via}";
    }

    // 可渲染场景快照：格子层（全盘网格）+ 实体层（棋子，id 稳定）+ 全局量（剩余/对数）
    public SceneSnapshot Snapshot(MahjongState state)
    {
        var cells = new List<Cell>();
        for (var y = 0; y < state.Height; y++)
        {
            for (var x = 0; x < state.Width; x++)
            {
                cells.Add(new Cell(x, y, "grid"));
            }
        }
        var entities = new List<Entity>();
        foreach (var piece in state.Pieces)
        {
            var type = piece.SpecialType switch
            {
                MahjongSpecial.Ice => "chess_ice",
                MahjongSpecial.Pop => "chess_pop",
                MahjongSpecial.Cont => "chess_cont",
                MahjongSpecial.Wall => "chess_wall",
                MahjongSpecial.Flip => "chess_flip",
                _ => "chess_normal",
            };
            // props 只发与视觉相关的最小集（face 恒有；specialInfo 非零才发；冻结仅冰冻棋），
            // 未声明的 prop 会在渲染告警刷屏——语义细节已在 specialType 区分
            var props = new Dictionary<string, JsonNode?> { ["face"] = JsonValue.Create(piece.Type) };
            if (piece.SpecialInfo != 0)
            {
                props["specialInfo"] = JsonValue.Create(piece.SpecialInfo);
            }
            if (IsFrozen(piece))
            {
                props["frozen"] = JsonValue.Create(true);
            }
            entities.Add(new Entity($"c{piece.Id}", type, piece.X, piece.Y, props));
        }
        var globals = new Dictionary<string, JsonNode?>
        {
            ["remaining"] = JsonValue.Create(VictoryRemaining(state)),
            ["pairs"] = JsonValue.Create((VictoryRemaining(state) + 1) / 2),
            ["levelId"] = JsonValue.Create(state.LevelId),
            ["difficulty"] = JsonValue.Create(state.Difficulty),
        };
        return new SceneSnapshot(
            new BoardSize(state.Width, state.Height), cells, entities, globals);
    }

    // ─── 执行协议 ───

    public int MoveSchemaVersion => 1;

    // 结构化动作 → JSON（执行器机器输入；四字段无歧义定位消除对与落点）
    public JsonNode EncodeMove(MahjongMove move)
    {
        return new JsonObject
        {
            ["sourceId"] = move.SourceId,
            ["targetId"] = move.TargetId,
            ["landingX"] = move.LandingX,
            ["landingY"] = move.LandingY,
        };
    }

    // JSON → 动作：非法编码抛异常
    public MahjongMove DecodeMove(JsonNode encoded)
    {
        var sourceId = RequireInt(encoded, "sourceId");
        var targetId = RequireInt(encoded, "targetId");
        var landingX = RequireInt(encoded, "landingX");
        var landingY = RequireInt(encoded, "landingY");
        return new MahjongMove(sourceId, targetId, landingX, landingY);
    }

    public int SemanticSchemaVersion => 1;

    // 语义投影：求解抽象的全部玩法字段（棋子归并口径与 StateKey 一致；对拍双方按同一 schema 提取）
    public JsonNode ProjectSemanticState(MahjongState state)
    {
        var pieces = new JsonArray();
        foreach (var p in state.Pieces)
        {
            var special = p.SpecialType is MahjongSpecial.Flip or MahjongSpecial.Pop ? MahjongSpecial.Normal : p.SpecialType;
            var info = p.SpecialType is MahjongSpecial.Flip or MahjongSpecial.Pop ? 0 : p.SpecialInfo;
            pieces.Add(new JsonObject
            {
                ["id"] = p.Id,
                ["x"] = p.X,
                ["y"] = p.Y,
                ["type"] = p.Type,
                ["special"] = special,
                ["info"] = info,
            });
        }
        return new JsonObject
        {
            ["width"] = state.Width,
            ["height"] = state.Height,
            ["victoryRemaining"] = VictoryRemaining(state),
            ["walls"] = new JsonArray(state.Walls
                .Select(w => (JsonNode?)new JsonObject
                {
                    ["x"] = w.X,
                    ["y"] = w.Y,
                    ["dir"] = w.Dir,
                })
                .ToArray()),
            ["pieces"] = pieces,
        };
    }

    // ─── 可采纳启发式（GreedyDfs 排序用）───

    // 每次操作恰消除 2 只非墙棋子 → 剩余对数即代价下界（可采纳，且在无推动策略下近乎精确）
    public int EstimateToGoal(MahjongState state)
    {
        return (VictoryRemaining(state) + 1) / 2;
    }

    // ─── 棋盘查询（LevelSolvabilitySolver / ChessQueryHelper 语义移植）───

    private static bool IsFrozen(MahjongPiece piece)
        => piece.SpecialType == MahjongSpecial.Ice && piece.SpecialInfo > 0;

    private static bool CanDrag(MahjongPiece piece)
        => piece.SpecialType != MahjongSpecial.Wall && !IsFrozen(piece);

    private static bool CanMatch(MahjongPiece piece)
        => piece.SpecialType != MahjongSpecial.Wall && !IsFrozen(piece);

    private static bool CanBePushed(MahjongPiece piece)
        => piece.SpecialType != MahjongSpecial.Wall && !IsFrozen(piece);

    private static int VictoryRemaining(MahjongState state)
        => state.Pieces.Count(p => p.SpecialType != MahjongSpecial.Wall);

    // 虚拟棋盘视图： placements 中的棋子落在 landing，其余按原位（getChessAt 闭包的移植）
    private sealed class BoardView
    {
        private readonly Dictionary<(int X, int Y), MahjongPiece> _byPos;

        private BoardView(Dictionary<(int X, int Y), MahjongPiece> byPos)
        {
            _byPos = byPos;
        }

        public static BoardView Build(MahjongState state)
        {
            var byPos = new Dictionary<(int X, int Y), MahjongPiece>();
            foreach (var piece in state.Pieces)
            {
                byPos[(piece.X, piece.Y)] = piece;
            }
            return new BoardView(byPos);
        }

        public static BoardView BuildWith(MahjongState state, IReadOnlyList<DragPlacement> placements)
        {
            var movedIds = new HashSet<int>(placements.Select(p => p.Piece.Id));
            var byPos = new Dictionary<(int X, int Y), MahjongPiece>();
            foreach (var piece in state.Pieces)
            {
                if (movedIds.Contains(piece.Id))
                {
                    continue;
                }
                byPos[(piece.X, piece.Y)] = piece;
            }
            foreach (var p in placements)
            {
                byPos[(p.LandingX, p.LandingY)] = p.Piece;
            }
            return new BoardView(byPos);
        }

        public MahjongPiece? At(int x, int y)
            => _byPos.TryGetValue((x, y), out var piece) ? piece : null;
    }

    private readonly record struct DragPlacement(MahjongPiece Piece, int LandingX, int LandingY);

    private static bool HasWallBetween(MahjongState state, int x1, int y1, int x2, int y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (Math.Abs(dx) + Math.Abs(dy) != 1)
        {
            return false;
        }
        return dx == 1 ? HasObstacle(state, x1, y1, 4) || HasObstacle(state, x2, y2, 3)
            : dx == -1 ? HasObstacle(state, x1, y1, 3) || HasObstacle(state, x2, y2, 4)
            : dy == -1 ? HasObstacle(state, x1, y1, 1) || HasObstacle(state, x2, y2, 2)
            : dy == 1 && (HasObstacle(state, x1, y1, 2) || HasObstacle(state, x2, y2, 1));
    }

    private static bool HasObstacle(MahjongState state, int x, int y, int dir)
        => state.Walls.Any(w => w.X == x && w.Y == y && w.Dir == dir);

    // 相邻消除：四邻、同 type、中间无墙（ChessQueryHelper.canMatchChess）
    private static bool CanMatchChess(BoardView view, MahjongState state, int ax, int ay, int atype, MahjongPiece b)
        => b.Type == atype && Math.Abs(ax - b.X) + Math.Abs(ay - b.Y) == 1
            && !HasWallBetween(state, ax, ay, b.X, b.Y);

    // 同轴无遮挡消除：同行/列、距离>1、中间全空且无墙（ChessQueryHelper.canMatchLineOfSight）
    private static bool CanMatchLineOfSight(BoardView view, MahjongState state, int ax, int ay, int atype, MahjongPiece b)
    {
        var dx = b.X - ax;
        var dy = b.Y - ay;
        if (dx != 0 && dy != 0)
        {
            return false;
        }
        if (Math.Abs(dx) + Math.Abs(dy) <= 1)
        {
            return false;
        }
        var stepX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
        var stepY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
        var x = ax + stepX;
        var y = ay + stepY;
        var prevX = ax;
        var prevY = ay;
        while (x != b.X || y != b.Y)
        {
            if (HasWallBetween(state, prevX, prevY, x, y))
            {
                return false;
            }
            if (view.At(x, y) is not null)
            {
                return false;   // 中间格有棋子 → 遮挡
            }
            prevX = x;
            prevY = y;
            x += stepX;
            y += stepY;
        }
        return !HasWallBetween(state, prevX, prevY, b.X, b.Y);
    }

    // 点击消除候选：相邻 + 无遮挡，按 id 去重（ChessQueryHelper.getClickMatchCandidates）
    private static List<MahjongPiece> ClickMatchCandidates(BoardView view, MahjongState state, int sx, int sy, int type)
    {
        var seen = new HashSet<int>();
        var result = new List<MahjongPiece>();
        // 相邻：四方向逐一查（越界/墙/异 type 跳过）
        foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            var nx = sx + dx;
            var ny = sy + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                continue;
            }
            if (HasWallBetween(state, sx, sy, nx, ny))
            {
                continue;
            }
            var neighbor = view.At(nx, ny);
            if (neighbor is not null && neighbor.Type == type && CanMatch(neighbor) && seen.Add(neighbor.Id))
            {
                result.Add(neighbor);
            }
        }
        // 无遮挡：相邻格必须为空才沿该方向查找第一个棋子（无论 type 是否相同都停）
        foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            var nx = sx + dx;
            var ny = sy + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                continue;
            }
            if (HasWallBetween(state, sx, sy, nx, ny))
            {
                continue;
            }
            if (view.At(nx, ny) is not null)
            {
                continue;
            }
            var px = nx;
            var py = ny;
            var prevX = sx;
            var prevY = sy;
            while (px >= 0 && py >= 0 && px < state.Width && py < state.Height)
            {
                if (HasWallBetween(state, prevX, prevY, px, py))
                {
                    break;
                }
                var target = view.At(px, py);
                if (target is not null)
                {
                    if (target.Type == type && CanMatch(target) && seen.Add(target.Id))
                    {
                        result.Add(target);
                    }
                    break;
                }
                prevX = px;
                prevY = py;
                px += dx;
                py += dy;
            }
        }
        return result;
    }

    // 拖动松手消除候选：虚拟盘上以落点为探针做点击查询，排除本链棋子
    private static List<MahjongPiece> ReleaseMatchCandidates(
        MahjongState state, BoardView preView, MahjongPiece dragged, int landingX, int landingY,
        IReadOnlyList<DragPlacement>? movedPlacements)
    {
        if (dragged is null)
        {
            return [];
        }
        var placements = movedPlacements ?? [new DragPlacement(dragged, landingX, landingY)];
        var view = BoardView.BuildWith(state, placements);
        var movedIds = new HashSet<int>(placements.Select(p => p.Piece.Id));
        return ClickMatchCandidates(view, state, landingX, landingY, dragged.Type)
            .Where(m => !movedIds.Contains(m.Id))
            .ToList();
    }

    // 方向偏移（ChessDir：1 上 2 下 3 左 4 右；y 向下增）
    private static (int Dx, int Dy) Offset(int? dir) => dir switch
    {
        1 => (0, -1),
        2 => (0, 1),
        3 => (-1, 0),
        4 => (1, 0),
        _ => (0, 0),
    };

    private static int? DirFromDelta(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return null;
        }
        if (dx > 0) return 4;
        if (dx < 0) return 3;
        if (dy > 0) return 2;
        return 1;
    }

    // 连排组：全局同 specialInfo 的 CONT 棋子（Chess.ts getConnectedGroup）
    private static List<MahjongPiece> DragMoveGroup(MahjongState state, MahjongPiece chess)
    {
        if (chess.SpecialType != MahjongSpecial.Cont)
        {
            return [chess];
        }
        return state.Pieces
            .Where(c => c.SpecialType == MahjongSpecial.Cont && c.SpecialInfo == chess.SpecialInfo)
            .ToList();
    }

    private static HashSet<int> DragMoveGroupIds(MahjongState state, MahjongPiece chess)
        => new(DragMoveGroup(state, chess).Select(c => c.Id));

    // 沿方向找连续相邻棋子链末端（ChessQueryHelper.getChainEndInDir）
    private static (int X, int Y) ChainEndInDir(MahjongState state, BoardView view, MahjongPiece chess, int dir)
    {
        var x = chess.X;
        var y = chess.Y;
        while (true)
        {
            var (dx, dy) = Offset(dir);
            var nx = x + dx;
            var ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                break;
            }
            if (HasWallBetween(state, x, y, nx, ny))
            {
                break;
            }
            var next = view.At(nx, ny);
            if (next is null || !CanBePushed(next))
            {
                break;
            }
            x = nx;
            y = ny;
        }
        return (x, y);
    }

    // 从指定格沿方向数空格数，遇棋子/墙/边界停止（ChessQueryHelper.countEmptySlotsFrom）
    private static int CountEmptySlotsFrom(MahjongState state, BoardView view, int x, int y, int dir)
    {
        var count = 0;
        var (dx, dy) = Offset(dir);
        var px = x;
        var py = y;
        while (true)
        {
            var nx = px + dx;
            var ny = py + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                break;
            }
            if (HasWallBetween(state, px, py, nx, ny))
            {
                break;
            }
            if (view.At(nx, ny) is not null)
            {
                break;
            }
            count++;
            px = nx;
            py = ny;
        }
        return count;
    }

    // 该方向移动起算点：相邻有棋子时从推动链末端起算（ChessQueryHelper.getMoveStartInDir）
    private static (int X, int Y) MoveStartInDir(MahjongState state, BoardView view, MahjongPiece chess, int dir)
    {
        var (dx, dy) = Offset(dir);
        var nx = chess.X + dx;
        var ny = chess.Y + dy;
        if (nx >= 0 && ny >= 0 && nx < state.Width && ny < state.Height
            && !HasWallBetween(state, chess.X, chess.Y, nx, ny)
            && view.At(nx, ny) is not null)
        {
            return ChainEndInDir(state, view, chess, dir);
        }
        return (chess.X, chess.Y);
    }

    // 单枚棋子在方向上可移动格数（含推动链末端起算）
    private static int CalcSoloMaxEmptySlots(MahjongState state, BoardView view, MahjongPiece chess, int dir)
    {
        var start = MoveStartInDir(state, view, chess, dir);
        return CountEmptySlotsFrom(state, view, start.X, start.Y, dir);
    }

    // 末端棋子可移动格数：仅从自身起算
    private static int CalcEndPieceMaxEmptySlots(MahjongState state, BoardView view, MahjongPiece chess, int dir)
        => CountEmptySlotsFrom(state, view, chess.X, chess.Y, dir);

    // 该方向下一格是否为同组连排棋子
    private static bool HasGroupMateInDir(MahjongState state, BoardView view, MahjongPiece chess, int dir, HashSet<int> groupIds)
    {
        var (dx, dy) = Offset(dir);
        var nx = chess.X + dx;
        var ny = chess.Y + dy;
        if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
        {
            return false;
        }
        if (HasWallBetween(state, chess.X, chess.Y, nx, ny))
        {
            return false;
        }
        var neighbor = view.At(nx, ny);
        return neighbor is not null && groupIds.Contains(neighbor.Id);
    }

    // 连排组在 dir 方向的前沿棋子（下一格无同组成员）
    private static List<MahjongPiece> GroupFrontierInDir(MahjongState state, BoardView view, List<MahjongPiece> group, int dir)
    {
        var groupIds = new HashSet<int>(group.Select(c => c.Id));
        return group.Where(m => !HasGroupMateInDir(state, view, m, dir, groupIds)).ToList();
    }

    // 拖动连排组的距离上限：前沿棋子 solo 格数取 min（规则 2）
    private static int CalcGroupMaxEmptySlots(MahjongState state, BoardView view, MahjongPiece chess, int dir)
    {
        var group = DragMoveGroup(state, chess);
        if (group.Count == 0)
        {
            return 0;
        }
        if (group.Count == 1)
        {
            return CalcSoloMaxEmptySlots(state, view, group[0], dir);
        }
        var frontier = GroupFrontierInDir(state, view, group, dir);
        if (frontier.Count == 0)
        {
            return 0;
        }
        return frontier.Min(m => CalcSoloMaxEmptySlots(state, view, m, dir));
    }

    // 连排组在 moveDir 方向上的行/列末端棋子
    private static List<MahjongPiece> GroupEndPiecesInDir(List<MahjongPiece> group, int moveDir)
    {
        var ends = new List<MahjongPiece>();
        if (moveDir is 3 or 4)   // 左/右：按行分组取行末
        {
            foreach (var row in group.GroupBy(c => c.Y))
            {
                ends.Add(moveDir == 4
                    ? row.Aggregate((a, b) => a.X > b.X ? a : b)
                    : row.Aggregate((a, b) => a.X < b.X ? a : b));
            }
        }
        else   // 上/下：按列分组取列末
        {
            foreach (var col in group.GroupBy(c => c.X))
            {
                ends.Add(moveDir == 2
                    ? col.Aggregate((a, b) => a.Y > b.Y ? a : b)
                    : col.Aggregate((a, b) => a.Y < b.Y ? a : b));
            }
        }
        return ends;
    }

    // CONT 组整组入推动映射
    private static void AddToPushMap(MahjongState state, MahjongPiece chess, Dictionary<int, MahjongPiece> pushMap)
    {
        foreach (var m in DragMoveGroup(state, chess))
        {
            pushMap[m.Id] = m;
        }
    }

    // 同行/列前沿中沿 moveDir 最靠前的棋子
    private static MahjongPiece PickLeadingInDir(List<MahjongPiece> pieces, int dir)
        => pieces.Aggregate((a, b) =>
            dir == 4 ? a.X >= b.X ? a : b
            : dir == 3 ? a.X <= b.X ? a : b
            : dir == 2 ? a.Y >= b.Y ? a : b
            : a.Y <= b.Y ? a : b);

    // 连排组入链后，从与当前链同行/列的方向前沿继续延伸
    private static MahjongPiece AdvanceCurrentToGroupPushFront(
        MahjongState state, BoardView view, MahjongPiece current, int moveDir)
    {
        if (current.SpecialType != MahjongSpecial.Cont)
        {
            return current;
        }
        var group = DragMoveGroup(state, current);
        var frontier = GroupFrontierInDir(state, view, group, moveDir);
        if (frontier.Count == 0)
        {
            return current;
        }
        var onPushLine = moveDir is 3 or 4
            ? frontier.Where(c => c.Y == current.Y).ToList()
            : frontier.Where(c => c.X == current.X).ToList();
        var candidates = onPushLine.Count > 0 ? onPushLine : frontier;
        return candidates.Count == 1 ? candidates[0] : PickLeadingInDir(candidates, moveDir);
    }

    // 从 start 沿 moveDir 延续线性推动链（不含拖动组内棋子）
    private static void ExtendLinearPushChain(
        MahjongState state, BoardView view, MahjongPiece start, int moveDir,
        HashSet<int> dragGroupIds, Dictionary<int, MahjongPiece> pushMap)
    {
        var current = start;
        while (true)
        {
            if (!dragGroupIds.Contains(current.Id))
            {
                AddToPushMap(state, current, pushMap);
                current = AdvanceCurrentToGroupPushFront(state, view, current, moveDir);
            }
            var (dx, dy) = Offset(moveDir);
            var nx = current.X + dx;
            var ny = current.Y + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                break;
            }
            if (HasWallBetween(state, current.X, current.Y, nx, ny))
            {
                break;
            }
            var next = view.At(nx, ny);
            if (next is null || dragGroupIds.Contains(next.Id) || !CanBePushed(next) || pushMap.ContainsKey(next.Id))
            {
                break;
            }
            current = next;
        }
    }

    // 扩展推动集合：遇 CONT 整组入列，否则线性链（ConnectedChessController.getExpandedPushFollowers）
    private static List<MahjongPiece> ExpandedPushFollowers(MahjongState state, BoardView view, MahjongPiece dragged, int moveDir)
    {
        var dragGroupIds = DragMoveGroupIds(state, dragged);
        var pushMap = new Dictionary<int, MahjongPiece>();
        var processedStarts = new HashSet<int>();
        foreach (var member in DragMoveGroup(state, dragged))
        {
            var (dx, dy) = Offset(moveDir);
            var nx = member.X + dx;
            var ny = member.Y + dy;
            if (nx < 0 || ny < 0 || nx >= state.Width || ny >= state.Height)
            {
                continue;
            }
            if (HasWallBetween(state, member.X, member.Y, nx, ny))
            {
                continue;
            }
            var occupant = view.At(nx, ny);
            if (occupant is null || dragGroupIds.Contains(occupant.Id) || !processedStarts.Add(occupant.Id))
            {
                continue;
            }
            ExtendLinearPushChain(state, view, occupant, moveDir, dragGroupIds, pushMap);
        }
        return pushMap.Values.ToList();
    }

    // 行/列末端的可移动格数；前方紧邻同批推动链棋子时返回 null（由链前方棋子决定）
    private static int? CalcRowColEndPushLimit(
        MahjongState state, BoardView view, MahjongPiece end, int moveDir,
        HashSet<int> groupIds, HashSet<int> pushFollowerIds, HashSet<int> dragGroupIds)
    {
        var (dx, dy) = Offset(moveDir);
        var nx = end.X + dx;
        var ny = end.Y + dy;
        if (nx >= 0 && ny >= 0 && nx < state.Width && ny < state.Height
            && !HasWallBetween(state, end.X, end.Y, nx, ny))
        {
            var neighbor = view.At(nx, ny);
            if (neighbor is not null && !groupIds.Contains(neighbor.Id)
                && (pushFollowerIds.Contains(neighbor.Id) || dragGroupIds.Contains(neighbor.Id)))
            {
                return null;
            }
        }
        return CalcEndPieceMaxEmptySlots(state, view, end, moveDir);
    }

    // 收集连排组各行/列末端在规则 3 下的位移上限
    private static void CollectConnectedGroupEndLimits(
        MahjongState state, BoardView view, List<MahjongPiece> group, int moveDir,
        HashSet<int> pushFollowerIds, HashSet<int> dragGroupIds,
        List<int> limits, HashSet<int> countedEndIds)
    {
        var groupIds = new HashSet<int>(group.Select(c => c.Id));
        foreach (var end in GroupEndPiecesInDir(group, moveDir))
        {
            if (!countedEndIds.Add(end.Id))
            {
                continue;
            }
            var limit = CalcRowColEndPushLimit(state, view, end, moveDir, groupIds, pushFollowerIds, dragGroupIds);
            if (limit is not null)
            {
                limits.Add(limit.Value);
            }
        }
    }

    // 被推动/拖动连排组各行/列末端格数取 min（规则 3）
    private static int CalcPushLimitFromConnectedGroups(MahjongState state, BoardView view, MahjongPiece dragged, int moveDir)
    {
        var pushFollowers = ExpandedPushFollowers(state, view, dragged, moveDir);
        var pushFollowerIds = new HashSet<int>(pushFollowers.Select(f => f.Id));
        var dragGroupIds = DragMoveGroupIds(state, dragged);
        var seenGroupInfo = new HashSet<int>();
        var countedEndIds = new HashSet<int>();
        var limits = new List<int>();

        foreach (var follower in pushFollowers)
        {
            if (follower.SpecialType != MahjongSpecial.Cont || !seenGroupInfo.Add(follower.SpecialInfo))
            {
                continue;
            }
            var group = DragMoveGroup(state, follower);
            CollectConnectedGroupEndLimits(state, view, group, moveDir, pushFollowerIds, dragGroupIds, limits, countedEndIds);
        }

        if (dragged.SpecialType == MahjongSpecial.Cont)
        {
            CollectConnectedGroupEndLimits(
                state, view, DragMoveGroup(state, dragged), moveDir, pushFollowerIds, dragGroupIds, limits, countedEndIds);
        }

        return limits.Count == 0 ? int.MaxValue : limits.Min();
    }

    // 综合规则 2 + 3 的方向最大可移动格数
    private static int CalcMaxEmptySlotsInDir(MahjongState state, BoardView view, MahjongPiece chess, int dir)
    {
        var baseSlots = chess.SpecialType == MahjongSpecial.Cont
            ? CalcGroupMaxEmptySlots(state, view, chess, dir)
            : CalcSoloMaxEmptySlots(state, view, chess, dir);
        return Math.Min(baseSlots, CalcPushLimitFromConnectedGroups(state, view, chess, dir));
    }

    // 拖动落点 placements：连排组全组 + 推动链同位移（ConnectedChessController.buildDragPlacements）
    private static List<DragPlacement> BuildDragPlacements(
        MahjongState state, BoardView view, MahjongPiece dragged, int landingX, int landingY, int moveDir)
    {
        var dx = landingX - dragged.X;
        var dy = landingY - dragged.Y;
        var moveGroupIds = DragMoveGroupIds(state, dragged);
        var placements = new List<DragPlacement>();
        foreach (var member in DragMoveGroup(state, dragged))
        {
            placements.Add(new DragPlacement(member, member.X + dx, member.Y + dy));
        }
        foreach (var follower in ExpandedPushFollowers(state, view, dragged, moveDir))
        {
            if (moveGroupIds.Contains(follower.Id))
            {
                continue;
            }
            placements.Add(new DragPlacement(follower, follower.X + dx, follower.Y + dy));
        }
        return placements;
    }

    // 可达落点：原位（可消除时）+ 四方向各 1..maxSlots（ConnectedChessController.getReachableGridPositions）
    private static List<(int X, int Y)> ReachablePositions(MahjongState state, BoardView view, MahjongPiece chess)
    {
        var canDrag = CanDrag(chess);
        var canMatch = CanMatch(chess);
        if (!canDrag && !canMatch)
        {
            return [];
        }
        var seen = new HashSet<string>();
        var result = new List<(int X, int Y)>();
        void Add(int x, int y)
        {
            if (seen.Add($"{x},{y}"))
            {
                result.Add((x, y));
            }
        }
        if (canMatch)
        {
            Add(chess.X, chess.Y);
        }
        if (!canDrag)
        {
            return result;
        }
        foreach (var dir in new[] { 1, 2, 3, 4 })
        {
            var slots = CalcMaxEmptySlotsInDir(state, view, chess, dir);
            var (dx, dy) = Offset(dir);
            for (var k = 1; k <= slots; k++)
            {
                Add(chess.X + dx * k, chess.Y + dy * k);
            }
        }
        return result;
    }

    // JSON 整数读取：缺失/类型不对抛异常（执行协议解码用）
    private static int RequireInt(JsonNode node, string name)
    {
        if (node is not JsonObject obj || obj[name] is not JsonValue value || !value.TryGetValue<int>(out var number))
        {
            throw new ArgumentException($"动作编码缺少合法整数字段 {name}");
        }
        return number;
    }
}
