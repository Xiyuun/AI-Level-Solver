using System.Text.Json;

namespace LevelForge.Adapters.PiggyFarm;

// 朝向：与关卡 JSON 的 facing 字符串一致；网格 y 向下增大（Up = y-1、Down = y+1）
public enum PigFacing
{
    Up,     // 朝上（世界 +Z）
    Down,   // 朝下（世界 -Z）
    Left,   // 朝左
    Right   // 朝右
}

// 网格坐标：x 列、y 行，原点左上角；与关卡 JSON occupiedCells 一致
public readonly record struct GridCell(int X, int Y);

// 单只猪的静态定义（地面层）：occupiedCells 是该项目原生脚印，长条猪/土豪猪是多格
public sealed record PiggyFarmLevelPig(
    int Id,
    PigFacing Facing,
    string Type,
    IReadOnlyList<GridCell> Cells,
    int Counter = 0);   // Boom 剩余爆炸次数；其它类型为 0

// 原生关卡：ClassicMode 编辑源 JSON（Assets/Main/LevelEditor/Luban~/Datas/Levels/*.json）
// 平台直通项目格式，不引入中间格式；本类型只保留规则需要的字段
public sealed record PiggyFarmLevel(
    int LevelId,
    int Difficulty,
    string LevelName,
    int MapWidth,
    int MapHeight,
    IReadOnlyList<PiggyFarmLevelPig> Pigs);

// 解析/校验结论：Errors 为空才允许进入规则层；Level 为空表示无法构造状态
public sealed record PiggyFarmParseResult(PiggyFarmLevel? Level, IReadOnlyList<string> Errors);

// 关卡解析与静态校验（接入产物 #2 的校验器部分）
// 诚实语义：非法数据一律报错，禁止静默修复；v1 子集外的机制单独标注，供平台导入器按"跳过"统计
public static class PiggyFarmLevels
{
    public const int MinMapSize = 4;            // 地图过小没有可玩性，按非法数据拒绝
    public const int MaxMapSize = 64;           // 状态空间上界防呆

    // v1 支持子集：普通猪 / 大长猪 / 土豪猪——三者的规则行为完全相同（NormalPig），
    // 只是脚印由关卡 occupiedCells 决定（2 / 3 / 6 格）；见 ClassicMode 的 LongPig/RichPig 空实现
    // v1 支持子集：Normal / Long / RichPig / Boom（其余类型在校验阶段如实拒绝）
    public static readonly IReadOnlySet<string> SupportedPigTypes = PiggyFarmPigTypes.Supported;

    // 子集外原因统一前缀：LevelImporter 以此把"范围外"与"数据坏"分开统计
    public const string UnsupportedPrefix = "v1 支持子集之外：";

    // 校验入口（门面 FormatErrors 直接消费）：返回全部错误；空列表 = 可导入/可求解
    public static IReadOnlyList<string> Validate(string json)
    {
        return Parse(json).Errors;
    }

    // 解析入口（LoadLevel 消费）：有错误直接抛，禁止带着错误构造状态
    public static PiggyFarmLevel Load(string json)
    {
        var result = Parse(json);
        if (result.Level is null || result.Errors.Count > 0)
        {
            throw new ArgumentException("关卡数据未通过静态校验：" + string.Join("；", result.Errors));
        }
        return result.Level;
    }

    // 从 JSON 提取关卡编号：解析失败或无该字段返回 null（门面 TryGetLevelId 用）
    public static int? TryReadLevelId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("levelId", out var id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt32(out var value))
            {
                return value;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #region 解析主体

    private static PiggyFarmParseResult Parse(string json)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new PiggyFarmParseResult(null, [$"关卡 JSON 解析失败：{ex.Message}"]);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new PiggyFarmParseResult(null, ["关卡 JSON 根节点必须是对象"]);
            }

            // 基础字段：编号 / 难度 / 名称 / 地图尺寸
            var levelId = ReadInt(root, "levelId", errors);
            if (levelId is { } id && id <= 0)
            {
                errors.Add($"levelId 必须为正整数（实际 {id}）");
            }
            var difficulty = ReadInt(root, "difficulty", errors);
            if (difficulty is { } diff && diff is < 1 or > 3)
            {
                errors.Add($"difficulty 只能是 1/2/3（实际 {diff}）");
            }
            var levelName = root.TryGetProperty("levelName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? ""
                : "";
            var mapWidth = ReadInt(root, "mapWidth", errors);
            var mapHeight = ReadInt(root, "mapHeight", errors);
            if (mapWidth is { } w && w is < MinMapSize or > MaxMapSize)
            {
                errors.Add($"mapWidth 超出范围 {MinMapSize}~{MaxMapSize}（实际 {w}）");
            }
            if (mapHeight is { } h && h is < MinMapSize or > MaxMapSize)
            {
                errors.Add($"mapHeight 超出范围 {MinMapSize}~{MaxMapSize}（实际 {h}）");
            }
            var width = mapWidth ?? 0;
            var height = mapHeight ?? 0;

            // 子集外机制：升降梯地形与动态兔子（先收集，结构性错误之后一起返回）
            var unsupported = new List<string>();
            if (root.TryGetProperty("terrainElements", out var terrain)
                && terrain.ValueKind == JsonValueKind.Array && terrain.GetArrayLength() > 0)
            {
                unsupported.Add("升降梯地形（terrainElements 非空）未建模");
            }
            if (root.TryGetProperty("specialData", out var special)
                && special.ValueKind == JsonValueKind.Object
                && special.TryGetProperty("rabbit", out var rabbit)
                && rabbit.ValueKind == JsonValueKind.Object)
            {
                var first = ReadOptionalInt(rabbit, "firstAppearStep");
                var interval = ReadOptionalInt(rabbit, "subsequentAppearStep");
                var perWave = ReadOptionalInt(rabbit, "rabbitCountPerWave");
                if ((first ?? 0) != 0 || (interval ?? 0) != 0 || (perWave ?? 0) != 0)
                {
                    unsupported.Add("动态兔子（specialData.rabbit 非零）未建模");
                }
            }

            // 猪列表：地面层 pigs[]；升降梯下层猪在 specialInfo[] 内，已按地形整体拒绝
            var pigs = new List<PiggyFarmLevelPig>();
            if (!root.TryGetProperty("pigs", out var pigsEl) || pigsEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add("缺少 pigs 数组");
            }
            else
            {
                var pigCount = ReadOptionalInt(root, "pigCount");
                if (pigCount is { } declared && declared != pigsEl.GetArrayLength())
                {
                    errors.Add($"pigCount={declared} 与 pigs 实际数量 {pigsEl.GetArrayLength()} 不一致");
                }
                var ids = new HashSet<int>();
                var index = 0;
                foreach (var pigEl in pigsEl.EnumerateArray())
                {
                    var prefix = $"pigs[{index}]";
                    if (pigEl.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"{prefix} 必须是对象");
                        index++;
                        continue;
                    }
                    var pig = ParsePig(pigEl, prefix, width, height, ids, unsupported, errors);
                    if (pig is not null)
                    {
                        pigs.Add(pig);
                    }
                    index++;
                }
            }

            // 地面猪之间不得占同一格（铁笼叠放属于子集外类型，已整体拒绝）
            if (errors.Count == 0 && unsupported.Count == 0)
            {
                CheckNoOverlap(pigs, errors);
            }

            foreach (var reason in unsupported)
            {
                // 同一种子集外原因只报一次，避免 90 只特殊猪刷屏
                if (!errors.Any(e => e.EndsWith(reason, StringComparison.Ordinal)))
                {
                    errors.Add(UnsupportedPrefix + reason);
                }
            }

            if (errors.Count > 0 || levelId is null || difficulty is null || mapWidth is null || mapHeight is null)
            {
                return new PiggyFarmParseResult(null, errors);
            }

            var level = new PiggyFarmLevel(levelId.Value, difficulty.Value, levelName, mapWidth.Value, mapHeight.Value, pigs);
            return new PiggyFarmParseResult(level, errors);
        }
    }

    // 解析单只猪：朝向、类型、脚印（矩形 + 尺寸与类型匹配 + 界内 + 无重复格）
    private static PiggyFarmLevelPig? ParsePig(
        JsonElement element, string prefix, int width, int height,
        HashSet<int> ids, List<string> unsupported, List<string> errors)
    {
        var id = ReadInt(element, "id", errors, prefix);
        if (id is { } pigId)
        {
            if (pigId < 0)
            {
                errors.Add($"{prefix}.id 必须是非负整数（实际 {pigId}）");
            }
            else if (!ids.Add(pigId))
            {
                errors.Add($"{prefix}.id={pigId} 与其它猪重复");
            }
        }

        var facingText = element.TryGetProperty("facing", out var facingEl) && facingEl.ValueKind == JsonValueKind.String
            ? facingEl.GetString() ?? ""
            : "";
        if (!TryParseFacing(facingText, out var facing))
        {
            errors.Add($"{prefix}.facing 非法：\"{facingText}\"（只允许 Up/Down/Left/Right）");
        }

        var type = element.TryGetProperty("pigType", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString() ?? ""
            : "";
        if (type.Length == 0)
        {
            errors.Add($"{prefix}.pigType 缺失");
        }
        else if (!SupportedPigTypes.Contains(type))
        {
            unsupported.Add($"猪类型 {type} 未建模");
        }

        // Boom 的 specialInfo 是剩余爆炸次数；空=默认 3，非法值如实报错（不静默修复）
        var counter = 0;
        if (type == "Boom")
        {
            var info = element.TryGetProperty("specialInfo", out var infoEl) && infoEl.ValueKind == JsonValueKind.String
                ? infoEl.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(info))
            {
                counter = 3;
            }
            else if (!int.TryParse(info.Trim(), out counter) || counter <= 0)
            {
                errors.Add($"{prefix}.specialInfo 非法：Boom 需要正整数剩余爆炸次数（实际 \"{info}\"）");
            }
        }

        var cells = new List<GridCell>();
        if (!element.TryGetProperty("occupiedCells", out var cellsEl) || cellsEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{prefix}.occupiedCells 缺失或不是数组");
        }
        else
        {
            var seen = new HashSet<GridCell>();
            var cellIndex = 0;
            foreach (var cellEl in cellsEl.EnumerateArray())
            {
                var cellPrefix = $"{prefix}.occupiedCells[{cellIndex}]";
                if (cellEl.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{cellPrefix} 必须是对象");
                    cellIndex++;
                    continue;
                }
                var x = ReadInt(cellEl, "x", errors, cellPrefix);
                var y = ReadInt(cellEl, "y", errors, cellPrefix);
                if (x is { } cx && y is { } cy)
                {
                    if (cx < 0 || cx >= width || cy < 0 || cy >= height)
                    {
                        errors.Add($"{cellPrefix} ({cx},{cy}) 越界：地图 {width}×{height}");
                    }
                    else if (!seen.Add(new GridCell(cx, cy)))
                    {
                        errors.Add($"{cellPrefix} ({cx},{cy}) 在同一只猪内重复");
                    }
                    else
                    {
                        cells.Add(new GridCell(cx, cy));
                    }
                }
                cellIndex++;
            }
            if (cells.Count == 0)
            {
                errors.Add($"{prefix}.occupiedCells 不能为空");
            }
        }

        if (cells.Count == 0 || errors.Any(e => e.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return null;
        }

        // 脚印必须是实心矩形，且"长边沿朝向轴"——这是游戏侧 NormalPig 的脚印几何前提
        var minX = cells.Min(c => c.X);
        var maxX = cells.Max(c => c.X);
        var minY = cells.Min(c => c.Y);
        var maxY = cells.Max(c => c.Y);
        var boxWidth = maxX - minX + 1;
        var boxHeight = maxY - minY + 1;
        if (boxWidth * boxHeight != cells.Count)
        {
            errors.Add($"{prefix}.occupiedCells 不是实心矩形（{boxWidth}×{boxHeight}，{cells.Count} 格）");
            return null;
        }
        var expected = type switch
        {
            "Normal" => (1, 2),
            "Boom" => (1, 2),
            "Long" => (1, 3),
            "RichPig" => (2, 3),
            "Duck" => (1, 1),
            _ => (0, 0)
        };
        if (expected.Item1 > 0)
        {
            var alongX = facing is PigFacing.Left or PigFacing.Right;
            var expectedWidth = alongX ? expected.Item2 : expected.Item1;
            var expectedHeight = alongX ? expected.Item1 : expected.Item2;
            if (boxWidth != expectedWidth || boxHeight != expectedHeight)
            {
                errors.Add($"{prefix} 脚印 {boxWidth}×{boxHeight} 与类型 {type}（朝 {facingText}）应有的 {expectedWidth}×{expectedHeight} 不一致");
                return null;
            }
        }

        return new PiggyFarmLevelPig(
            id ?? -1, facing, type,
            cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray(),
            counter);
    }

    // 地面猪占格互斥：同格出现两只猪 = 关卡数据错误（铁笼叠放已在子集外整体拒绝）
    private static void CheckNoOverlap(List<PiggyFarmLevelPig> pigs, List<string> errors)
    {
        var occupancy = new Dictionary<GridCell, int>();
        foreach (var pig in pigs)
        {
            foreach (var cell in pig.Cells)
            {
                if (occupancy.TryGetValue(cell, out var other))
                {
                    errors.Add($"pigs[{pig.Id}] 与 pigs[{other}] 占用同一格 ({cell.X},{cell.Y})");
                    return;   // 报一次即可，避免多格重叠刷屏
                }
                occupancy[cell] = pig.Id;
            }
        }
    }

    #endregion

    #region 小工具

    public static bool TryParseFacing(string text, out PigFacing facing)
    {
        switch (text)
        {
            case "Up": facing = PigFacing.Up; return true;
            case "Down": facing = PigFacing.Down; return true;
            case "Left": facing = PigFacing.Left; return true;
            case "Right": facing = PigFacing.Right; return true;
            default: facing = default; return false;
        }
    }

    // 朝向 → 网格步长（与 MapManager.DirectionToGridStep 一致：y 向下增大）
    public static (int Dx, int Dy) Delta(PigFacing facing)
    {
        return facing switch
        {
            PigFacing.Up => (0, -1),
            PigFacing.Down => (0, 1),
            PigFacing.Left => (-1, 0),
            PigFacing.Right => (1, 0),
            _ => (0, 0)
        };
    }

    public static string FacingName(PigFacing facing)
    {
        return facing switch
        {
            PigFacing.Up => "上",
            PigFacing.Down => "下",
            PigFacing.Left => "左",
            PigFacing.Right => "右",
            _ => "?"
        };
    }

    // 读必填整数：缺失/类型不对记错误并返回 null
    private static int? ReadInt(JsonElement obj, string name, List<string> errors, string prefix = "")
    {
        var label = prefix.Length == 0 ? name : $"{prefix}.{name}";
        if (!obj.TryGetProperty(name, out var el))
        {
            errors.Add($"缺少字段 {label}");
            return null;
        }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var value))
        {
            errors.Add($"字段 {label} 必须是整数");
            return null;
        }
        return value;
    }

    // 读可选整数：缺失返回 null（不记错误）
    private static int? ReadOptionalInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        return el.TryGetInt32(out var value) ? value : null;
    }

    #endregion
}
