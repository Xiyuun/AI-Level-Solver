using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Adapters.SokobanToy;

// 玩具推箱子 · 类型定义（开源默认参考实现，接入套件skill详设 §8）
// 玩法：玩家四方向移动一步；前方有箱子则推（箱后格须为空）；
// 全部箱子归位（目标格）即胜；无道具、无随机、无步数限制

// 原生关卡格式（项目格式直通，无平台标准格式）：
// {"id":"toy-001","grid":["#####","#@$.#","#####"]}
// 字符：# 墙  空格 地板  @ 玩家  $ 箱子  . 目标  * 箱在目标上  + 玩家在目标上
public sealed record SokobanLevel(string Id, string[] Grid);

// 规则状态：玩家位置 + 箱子位置（数组下标即箱子身份，id 稳定 box-0/1/…）
public sealed record SokobanState(int Px, int Py, (int X, int Y)[] Boxes, SokobanLevel Level);

// 操作：四方向单位向量（上/下/左/右，顺序稳定）
public sealed record SokobanMove(int Dx, int Dy);

// 推箱子适配器：零引擎依赖的纯逻辑实现（铁律三条全满足）
public sealed class SokobanToyAdapter :
    IGameAdapter<SokobanLevel, SokobanState, SokobanMove>,
    IExecutionProtocol<SokobanState, SokobanMove>
{
    // 四方向固定顺序：顺序稳定契约
    private static readonly (int Dx, int Dy, string Name)[] Directions =
    {
        (0, -1, "上"), (0, 1, "下"), (-1, 0, "左"), (1, 0, "右")
    };

    #region IGameAdapter 七成员

    // 解析关卡：完整格式校验后提取玩家与箱子；非法数据抛 ArgumentException（禁止静默修复）
    public SokobanState LoadLevel(SokobanLevel level)
    {
        var errors = SokobanToyValidator.Validate(level.Id, level.Grid);
        if (errors.Count > 0)
        {
            throw new ArgumentException($"关卡 {level.Id} 非法：{string.Join("；", errors)}");
        }
        (int px, int py) = (-1, -1);
        var boxes = new List<(int X, int Y)>();
        // 扫描顺序：从左到右、从上到下——箱子编号因此确定
        for (var y = 0; y < level.Grid.Length; y++)
        {
            for (var x = 0; x < level.Grid[y].Length; x++)
            {
                var c = level.Grid[y][x];
                if (c is '@' or '+') (px, py) = (x, y);
                if (c is '$' or '*') boxes.Add((x, y));
            }
        }
        return new SokobanState(px, py, boxes.ToArray(), level);
    }

    // 枚举合法操作：目标格非墙、推箱时箱后格非墙非箱
    public IReadOnlyList<SokobanMove> GetLegalMoves(SokobanState s)
    {
        var moves = new List<SokobanMove>();
        foreach (var (dx, dy, _) in Directions)
        {
            if (IsLegal(s, dx, dy)) moves.Add(new SokobanMove(dx, dy));
        }
        return moves;
    }

    // 纯函数：执行移动/推箱；非法操作抛 InvalidOperationException（契约：move 须来自本状态 GetLegalMoves）
    public SokobanState ApplyMove(SokobanState s, SokobanMove m)
    {
        var (dx, dy) = (m.Dx, m.Dy);
        // 仅接受四方向单位向量
        if (Math.Abs(dx) + Math.Abs(dy) != 1)
        {
            throw new InvalidOperationException($"非法操作向量：({dx},{dy})");
        }
        if (!IsLegal(s, dx, dy))
        {
            throw new InvalidOperationException($"非法操作：({s.Px},{s.Py}) 方向({dx},{dy})");
        }
        var tx = s.Px + dx;
        var ty = s.Py + dy;
        var boxes = s.Boxes;
        // 判断是否推箱：目标格有箱子则连推
        var boxIndex = IndexOfBox(s, tx, ty);
        if (boxIndex >= 0)
        {
            var next = ((int X, int Y)[])boxes.Clone();       // 复制数组，绝不改入参
            next[boxIndex] = (tx + dx, ty + dy);
            return s with { Px = tx, Py = ty, Boxes = next };
        }
        return s with { Px = tx, Py = ty };
    }

    // 胜利判定：全部箱子在目标格上
    public bool IsGoal(SokobanState s)
    {
        return s.Boxes.All(b => IsGoalCell(s.Level, b.X, b.Y));
    }

    // 规范化键：玩家位置 + 排序后的箱子位置——箱子数组顺序差异不影响逻辑相等
    public string StateKey(SokobanState s)
    {
        var boxes = s.Boxes.Select(b => $"{b.X},{b.Y}").Order(StringComparer.Ordinal);
        return $"P{s.Px},{s.Py};B{string.Join(";", boxes)}";
    }

    // 展示文本：方向 + 是否推箱
    public string DescribeMove(SokobanState s, SokobanMove m)
    {
        var name = Directions.First(d => d.Dx == m.Dx && d.Dy == m.Dy).Name;
        var pushing = IndexOfBox(s, s.Px + m.Dx, s.Py + m.Dy) >= 0;
        return pushing ? $"{name}移（推箱）" : $"{name}移";
    }

    // 场景快照：墙/目标进格子层；玩家与箱子进实体层；归位进度进 globals 信息栏
    public SceneSnapshot Snapshot(SokobanState s)
    {
        var height = s.Level.Grid.Length;
        var width = s.Level.Grid[0].Length;
        var cells = new List<Cell>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = s.Level.Grid[y][x];
                if (c == '#') cells.Add(new Cell(x, y, "wall"));
                if (c is '.' or '*' or '+') cells.Add(new Cell(x, y, "goal"));
            }
        }
        var entities = new List<Entity>
        {
            new("player", "player", s.Px, s.Py, null)
        };
        for (var i = 0; i < s.Boxes.Length; i++)
        {
            var (bx, by) = s.Boxes[i];
            // onGoal 作为展示属性（视觉映射可着色提示），不参与规则判定
            var props = new Dictionary<string, JsonNode?> { ["onGoal"] = IsGoalCell(s.Level, bx, by) };
            entities.Add(new Entity($"box-{i}", "box", bx, by, props));
        }
        var onGoal = s.Boxes.Count(b => IsGoalCell(s.Level, b.X, b.Y));
        var globals = new Dictionary<string, JsonNode?>
        {
            ["boxesOnGoal"] = onGoal,
            ["totalBoxes"] = s.Boxes.Length
        };
        return new SceneSnapshot(new BoardSize(width, height), cells, entities, globals);
    }

    #endregion

    #region IExecutionProtocol

    // 动作 schema 版本
    public int MoveSchemaVersion => 1;

    // 动作编码：{"dir":"up|down|left|right"}
    public JsonNode EncodeMove(SokobanMove m)
    {
        var dir = Directions.First(d => d.Dx == m.Dx && d.Dy == m.Dy).Name switch
        {
            "上" => "up", "下" => "down", "左" => "left", _ => "right"
        };
        return new JsonObject { ["dir"] = dir };
    }

    // 动作解码：dir 字段 → 方向向量；非法编码抛异常
    public SokobanMove DecodeMove(JsonNode n)
    {
        if (n is JsonObject o && o["dir"] is JsonValue v && v.GetValue<string>() is { } dir)
        {
            return dir switch
            {
                "up" => new SokobanMove(0, -1),
                "down" => new SokobanMove(0, 1),
                "left" => new SokobanMove(-1, 0),
                "right" => new SokobanMove(1, 0),
                _ => throw new ArgumentException($"未知方向：{dir}")
            };
        }
        throw new ArgumentException($"非法动作编码：{n.ToJsonString()}");
    }

    // 语义投影 schema 版本
    public int SemanticSchemaVersion => 1;

    // 语义投影：玩家 + 箱子（按 id 序）——对拍比较的唯一来源
    public JsonNode ProjectSemanticState(SokobanState s)
    {
        return new JsonObject
        {
            ["player"] = new JsonObject { ["x"] = s.Px, ["y"] = s.Py },
            ["boxes"] = new JsonArray(s.Boxes.Select((b, i) => (JsonNode)new JsonObject
            {
                ["id"] = $"box-{i}",
                ["x"] = b.X,
                ["y"] = b.Y
            }).ToArray())
        };
    }

    #endregion

    #region 内部工具

    // 移动合法性：目标非墙；推箱时箱后格非墙非箱
    private static bool IsLegal(SokobanState s, int dx, int dy)
    {
        var tx = s.Px + dx;
        var ty = s.Py + dy;
        if (IsWall(s.Level, tx, ty)) return false;
        if (IndexOfBox(s, tx, ty) >= 0)
        {
            // 推箱：箱后格须为空地板/目标（非墙且无别的箱子）
            var bx = tx + dx;
            var by = ty + dy;
            return !IsWall(s.Level, bx, by) && IndexOfBox(s, bx, by) < 0;
        }
        return true;
    }

    // 找到指定格上的箱子下标；无则 -1
    private static int IndexOfBox(SokobanState s, int x, int y)
    {
        for (var i = 0; i < s.Boxes.Length; i++)
        {
            if (s.Boxes[i].X == x && s.Boxes[i].Y == y) return i;
        }
        return -1;
    }

    // 墙判定：越界视为墙（棋盘有边界墙时通常不会触发）
    private static bool IsWall(SokobanLevel level, int x, int y)
    {
        if (y < 0 || y >= level.Grid.Length) return true;
        if (x < 0 || x >= level.Grid[y].Length) return true;
        return level.Grid[y][x] == '#';
    }

    // 目标格判定
    private static bool IsGoalCell(SokobanLevel level, int x, int y)
    {
        var c = level.Grid[y][x];
        return c is '.' or '*' or '+';
    }

    #endregion
}
