using System.Text.Json;
using System.Text.Json.Nodes;

namespace LevelForge.Adapters.Mahjong;

// 关卡 JSON 单只棋子配置（LevelData.ts ILevelChessConfig 同构）；type=0 表示空位
public sealed record MahjongLevelChess(int Id, int X, int Y, int Type, int SpecialType, int SpecialInfo);

// 关卡 JSON 障碍物（墙放在格子夹缝上，dir 与 ChessDir/ObstacleDir 取值一致）
public sealed record MahjongLevelObstacle(int Id, int X, int Y, int Dir);

// 原生关卡（LevelData.ts ILevelConfig 同构；平台直通项目格式，不引入中间格式）
public sealed record MahjongLevel(
    int Level,
    int Difficulty,
    int ChessCount,
    int MapWidth,
    int MapHeight,
    IReadOnlyList<MahjongLevelChess> Chesses,
    IReadOnlyList<MahjongLevelObstacle> Obstacles);

// 关卡解析与静态校验（接入产物 #2 校验器；非法数据如实报错，禁止静默修复）
// v1 求解对象 = 全预设（flattened）关卡：chesses 数量必须等于 chessCount——
// 生成型关卡规格（chesses 少于 chessCount，依赖运行时随机填充）以"v1 支持子集之外"前缀如实拒绝
public static class MahjongLevels
{
    public const int MinMapSize = 2;
    public const int MaxMapSize = 32;      // 状态空间防呆上界
    public const int SpecialNormal = 0;
    public const int SpecialIce = 1;
    public const int SpecialPop = 2;
    public const int SpecialCont = 3;
    public const int SpecialWall = 4;
    public const int SpecialFlip = 5;
    public const int PopFaceType = 43;     // 爆竹棋牌面（超出麻将牌面枚举）

    public const string UnsupportedPrefix = "v1 支持子集之外：";

    // 校验入口（门面 FormatErrors 直接消费）：返回全部错误；空列表 = 可导入/可求解
    public static IReadOnlyList<string> Validate(string json)
    {
        return Parse(json).Errors;
    }

    // 解析入口（LoadLevel 消费）：有错误直接抛，禁止带着错误构造状态
    public static MahjongLevel Load(string json)
    {
        var result = Parse(json);
        if (result.Level is null || result.Errors.Count > 0)
        {
            throw new ArgumentException("关卡数据未通过静态校验：" + string.Join("；", result.Errors));
        }
        return result.Level;
    }

    // 从 JSON 提取关卡编号：level 字段（JSON 坏掉时返回 null，门面 TryGetLevelId 用）
    public static int? TryReadLevelId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("level", out var id)
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

    // 解析结论：Level 为空表示无法构造（结构性错误）
    public sealed record ParseResult(MahjongLevel? Level, IReadOnlyList<string> Errors);

    internal static ParseResult Parse(string json)
    {
        var errors = new List<string>();
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new ParseResult(null, [$"关卡 JSON 解析失败：{ex.Message}"]);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ParseResult(null, ["关卡 JSON 根节点必须是对象"]);
            }

            var level = ReadInt(root, "level", errors);
            if (level is { } lv && lv < 1)
            {
                errors.Add($"level 必须为正整数（实际 {lv}）");
            }
            var difficulty = ReadInt(root, "difficulty", errors);
            if (difficulty is { } diff && diff < 0)
            {
                errors.Add($"difficulty 不能为负（实际 {diff}）");
            }
            var chessCount = ReadInt(root, "chessCount", errors);
            if (chessCount is { } cc && cc < 0)
            {
                errors.Add($"chessCount 不能为负（实际 {cc}）");
            }
            var width = ReadInt(root, "mapWidth", errors);
            var height = ReadInt(root, "mapHeight", errors);
            if (width is { } w && w is < MinMapSize or > MaxMapSize)
            {
                errors.Add($"mapWidth 超出范围 {MinMapSize}~{MaxMapSize}（实际 {w}）");
            }
            if (height is { } h && h is < MinMapSize or > MaxMapSize)
            {
                errors.Add($"mapHeight 超出范围 {MinMapSize}~{MaxMapSize}（实际 {h}）");
            }

            // chesses 数组
            var chesses = new List<MahjongLevelChess>();
            if (!root.TryGetProperty("chesses", out var chessesEl) || chessesEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add("缺少 chesses 数组");
            }
            else
            {
                var ids = new HashSet<int>();
                var cells = new HashSet<(int X, int Y)>();
                var index = 0;
                foreach (var pigEl in chessesEl.EnumerateArray())
                {
                    var prefix = $"chesses[{index}]";
                    if (pigEl.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"{prefix} 必须是对象");
                        index++;
                        continue;
                    }
                    var id = ReadInt(pigEl, "id", errors, prefix);
                    var x = ReadInt(pigEl, "x", errors, prefix);
                    var y = ReadInt(pigEl, "y", errors, prefix);
                    var type = ReadInt(pigEl, "type", errors, prefix);
                    var specialType = ReadOptionalInt(pigEl, "specialType") ?? SpecialNormal;
                    var specialInfo = ReadOptionalInt(pigEl, "specialInfo") ?? 0;
                    if (id is { } pid)
                    {
                        if (pid < 0) errors.Add($"{prefix}.id 必须是非负整数（实际 {pid}）");
                        else if (!ids.Add(pid)) errors.Add($"{prefix}.id={pid} 与其它棋子重复");
                    }
                    if (x is { } cx && y is { } cy && width is { } cw && height is { } ch)
                    {
                        if (cx < 0 || cx >= cw || cy < 0 || cy >= ch)
                        {
                            errors.Add($"{prefix} 坐标 ({cx},{cy}) 越界：地图 {cw}×{ch}");
                        }
                        else if (!cells.Add((cx, cy)))
                        {
                            errors.Add($"{prefix} ({cx},{cy}) 与其它棋子占用同一格");
                        }
                    }
                    if (type is { } t && t < 0)
                    {
                        errors.Add($"{prefix}.type 不能为负（实际 {t}）");
                    }
                    if (specialType is < SpecialNormal or > SpecialFlip)
                    {
                        errors.Add($"{prefix}.specialType 只允许 0~5（实际 {specialType}）");
                    }
                    if (specialInfo < 0)
                    {
                        errors.Add($"{prefix}.specialInfo 不能为负（实际 {specialInfo}）");
                    }
                    if (specialType == SpecialFlip && specialInfo is > 1)
                    {
                        errors.Add($"{prefix}.specialInfo 非法：翻面棋只能为 0（背面）或 1（正面）（实际 {specialInfo}）");
                    }
                    if (id is { } i2 && x is { } x2 && y is { } y2 && type is { } t2
                        && specialType is >= SpecialNormal and <= SpecialFlip && specialInfo >= 0
                        && (specialType != SpecialFlip || specialInfo <= 1))
                    {
                        chesses.Add(new MahjongLevelChess(i2, x2, y2, t2, specialType, specialInfo));
                    }
                    index++;
                }
            }

            // obstacles 数组（墙；语料当前为空，格式照契约支持）
            var obstacles = new List<MahjongLevelObstacle>();
            if (!root.TryGetProperty("obstacles", out var obsEl) || obsEl.ValueKind != JsonValueKind.Array)
            {
                errors.Add("缺少 obstacles 数组");
            }
            else
            {
                var index = 0;
                foreach (var obs in obsEl.EnumerateArray())
                {
                    var prefix = $"obstacles[{index}]";
                    if (obs.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"{prefix} 必须是对象");
                        index++;
                        continue;
                    }
                    var id = ReadOptionalInt(obs, "id") ?? index;
                    var x = ReadInt(obs, "x", errors, prefix);
                    var y = ReadInt(obs, "y", errors, prefix);
                    var dir = ReadInt(obs, "dir", errors, prefix);
                    if (x is { } cx && y is { } cy && width is { } cw && height is { } ch
                        && (cx < 0 || cx >= cw || cy < 0 || cy >= ch))
                    {
                        errors.Add($"{prefix} 依附格 ({cx},{cy}) 越界：地图 {cw}×{ch}");
                    }
                    if (dir is { } d && d is < 1 or > 4)
                    {
                        errors.Add($"{prefix}.dir 只允许 1~4（实际 {d}）");
                    }
                    if (x is { } fx && y is { } fy && dir is { } fd and >= 1 and <= 4)
                    {
                        obstacles.Add(new MahjongLevelObstacle(id, fx, fy, fd));
                    }
                    index++;
                }
            }

            // v1 子集边界：求解对象 = 全预设（flattened）关卡。
            // chesses 条数 ≠ chessCount 即生成型关卡规格（依赖运行时随机填充），须先经游戏
            // LevelLayoutBuilder.flattenConfig 压成全预设形态——如实拒绝，不静默代填。
            // （flattenConfig 的 chessCount = chesses 数组条数，含 type=0 空位条目）
            if (chessCount is { } expect && chesses.Count != expect && errors.Count == 0)
            {
                errors.Add(
                    UnsupportedPrefix + $"生成型关卡规格（chesses {chesses.Count} 条 ≠ chessCount {expect}）：" +
                    "运行时会随机填充与重排，求解语义不可复现；" +
                    "请先经游戏 LevelLayoutBuilder.flattenConfig 压成全预设形态（chesses 数量 = chessCount）后再求解");
            }
            // 生成期填充参数同理：flatten 后应为 0/缺省（独立于结构错误照常报告）
            var fillParam = ReadOptionalInt(root, "fillTypeCount");
            var adjParam = ReadOptionalInt(root, "adjacentPairCount");
            var popParam = ReadOptionalInt(root, "popChessPairCount");
            if (fillParam is > 0 || adjParam is > 0 || popParam is > 0)
            {
                errors.Add(
                    UnsupportedPrefix + "生成型填充参数（fillTypeCount/adjacentPairCount/popChessPairCount > 0）" +
                    "属于运行时随机填充，不在求解语义内；请先经游戏 LevelLayoutBuilder.flattenConfig 压成全预设形态后再求解");
            }

            if (errors.Count > 0 || level is null || difficulty is null || chessCount is null
                || width is null || height is null)
            {
                return new ParseResult(null, errors);
            }

            return new ParseResult(
                new MahjongLevel(level.Value, difficulty.Value, chessCount.Value, width.Value, height.Value, chesses, obstacles),
                errors);
        }
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
}
