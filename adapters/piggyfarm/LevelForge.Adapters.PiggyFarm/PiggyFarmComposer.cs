using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm;

// 结构化出题器 v2（布点式合成）：
//   - 模板只提供基础几何（真实关卡 + 对称变体）；需求的特殊猪通过改写模板上的普通猪来"布点"：
//     1x2 族（普通/爆炸/丧尸）= 原地换型；小黄鸭 = 收缩为 1x1；大长猪 = 脚印延伸一格；土豪猪 = 自由 2x3 落位。
//   - 特殊猪数量不设模板上限——唯一上限是地图物理容量（12×19）；放不下/不可解/判档不符时如实失败。
//   - 需求即契约：有类型需求时，未要求的特殊猪从模板上剥离（该候选若因此跌破质量门则换模板重试）；
//     无类型需求 = 自由生成，出真实关卡原貌（质量门把关）。
//   - 1 档：000001 的 6 猪互锁模块（1~3 个），保持轻量。
//   - 每条都必须过原生校验器 + 质量门，并由平台难度模型命中目标档（流水线最终判定）。
public static class PiggyFarmComposer
{
    public const string Spec = """
        # 输出约定（结构化出题）
        只输出一个 JSON 对象：{}（推荐）或 {"band": 1|2|3}（仅作提示）。
        实际档位以本次目标难度为准；不要输出关卡几何、不要输出 pigs/mapWidth 等字段。
        """;

    private const int MapCapacityPigs = 112;   // 12×19 带多朝向/可解的实际上限（物理裸上限 114）
    private const int MaxAttempts = 24;        // 布点式合成重试次数（模板×对称×布点随机性）

    private static readonly (PigFacing Facing, (int X, int Y)[] Cells)[] Module =
    [
        (PigFacing.Left, [(0, 0), (1, 0)]),
        (PigFacing.Down, [(2, 0), (2, 1)]),
        (PigFacing.Down, [(2, 2), (2, 3)]),
        (PigFacing.Down, [(1, 1), (1, 2)]),
        (PigFacing.Down, [(0, 1), (0, 2)]),
        (PigFacing.Left, [(0, 3), (1, 3)]),
    ];

    public static IReadOnlyList<string> ValidateContext(ComposeContext context)
    {
        var band = context.TargetBand;
        if (band is < 1 or > 3) return ["目标难度必须是 1/2/3"];
        var errors = new List<string>();
        var req = ParseRequirements(context.SpecialRequirements, errors);
        if (errors.Count > 0) return errors;

        var templates = band == 2 ? PiggyFarmTemplates.Band2 : PiggyFarmTemplates.Band3;
        // 物理容量：池内最大猪数（特殊猪数量不设模板上限——布点式合成可改写普通猪的类型与脚印）
        var capacity = band switch
        {
            1 => 18,
            _ => templates.Max(t => CountPigs(t)),
        };
        // 有效总只数 = max(裸"N只猪"，最大单类型需求数)——"500只土豪猪"的 500 计在类型里而非总数里
        var effectiveTotal = Math.Max(req.Total, req.Types.Count > 0 ? req.Types.Values.Max() : 0);
        if (effectiveTotal > capacity)
        {
            errors.Add($"目标 {band} 档物理容量最多 {capacity} 只（3 档最大 {MapCapacityPigs}）；请换档或降低数量");
        }
        return errors;
    }

    // 输出允许的类型集合：普通猪 + 明确要求的类型（需求即契约——未要求的特殊猪不得出现）
    private static HashSet<string> AllowedTypes(Requirements req)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Normal" };
        foreach (var key in req.Types.Keys) allowed.Add(key);
        return allowed;
    }

    public static ComposeResult Compose(string modelOutput, string? levelNumber) =>
        Compose(modelOutput, levelNumber, new ComposeContext(levelNumber, TryReadBand(modelOutput) ?? 0, null, 0));

    public static ComposeResult Compose(string modelOutput, string? levelNumber, ComposeContext context)
    {
        var band = context.TargetBand is >= 1 and <= 3 ? context.TargetBand : TryReadBand(modelOutput);
        if (band is null or < 1 or > 3)
        {
            return ComposeResult.Fail("输出必须包含 band=1|2|3（只输出 {\"band\":N}）");
        }

        var reqErrors = new List<string>();
        var req = ParseRequirements(context.SpecialRequirements, reqErrors);
        if (reqErrors.Count > 0) return ComposeResult.Reject(reqErrors);
        var required = req.Total;

        if (band == 1)
        {
            if (req.Types.Count > 0)
            {
                return ComposeResult.Reject("1 档小关模板只支持普通猪，无法满足指定猪类型");
            }
            return BuildModules(levelNumber, Math.Min(3, Math.Max(1, (required + 5) / 6)), context.Seed);
        }

        var templates = band == 2 ? PiggyFarmTemplates.Band2 : PiggyFarmTemplates.Band3;
        var random = new Random(context.Seed ^ 0x5f3759df);
        var lastObstacle = "未知";
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var chosen = templates[random.Next(templates.Length)];
            var symmetry = random.Next(4);
            // 对称变换 + 重编号（保持可解性的结构骨架）
            var node = JsonNode.Parse(TransformTemplate(chosen, levelNumber, band.Value, symmetry, random))!;

            // 需求布点：剥离未要求特殊猪（纯度）+ 把要求的特殊猪放置到模板上（换型/收缩/延伸/落位）
            var placeError = req.Types.Count > 0 ? PlaceRequirements(node, req, random) : null;
            if (placeError is not null)
            {
                lastObstacle = placeError;
                continue;
            }

            var json = node.ToJsonString();
            var formatErrors = PiggyFarmLevels.Validate(json);
            if (formatErrors.Count > 0)
            {
                lastObstacle = string.Join("；", formatErrors.Take(2));
                continue;
            }
            // 总只数下限核对：产出猪数必须 ≥ 需求（布点只增不减，但模板本身可能小于需求）
            if (PiggyFarmLevels.Load(json).Pigs.Count < required)
            {
                lastObstacle = $"产出 {PiggyFarmLevels.Load(json).Pigs.Count} 只，不足 {required}";
                continue;
            }

            var outcome = new PiggyFarmSolver().Solve(
                PiggyFarmLevels.Load(json), TimeSpan.FromSeconds(5), 200_000, CancellationToken.None);
            if (outcome.Status != SolveStatus.Solved)
            {
                lastObstacle = "布点后不可解";
                continue;
            }
            var weights = PiggyFarmContent.Weights;
            var score = weights.SolutionLengthWeight * outcome.Solution.Count
                      + weights.BranchingWeight * (outcome.Metrics?.AvgBranchingFactor ?? 0);
            var outBand = score < weights.Thresholds[0] ? 1 : score < weights.Thresholds[1] ? 2 : 3;
            if (outBand == band.Value)
            {
                return ComposeResult.Ok(json);
            }
            lastObstacle = $"布点后判档 {outBand} ≠ 目标 {band}";
        }

        if (req.Types.Count > 0)
        {
            // 需求即契约：宁可明确失败（说明几何/判档障碍），也不静默少猪或混入未要求类型
            return ComposeResult.Reject(
                $"{MaxAttempts} 次布点尝试未能生成满足要求的 {band} 档关卡（{string.Join("、", req.Types.Select(kv => $"{PiggyFarmPigTypes.DisplayName(kv.Key)}×{kv.Value}"))}；障碍：{lastObstacle}）；请换档、降低数量或调整类型");
        }

        // 无类型需求兜底：数量要求仍未满足时如实拒绝（不返回不足额的模板）
        var fallbackPigs = CountPigs(templates[0]);
        if (required > fallbackPigs)
        {
            return ComposeResult.Reject($"目标 {band} 档物理容量最多 {templates.Max(t => CountPigs(t))} 只；要求 {required} 只，请换档或降低数量");
        }
        var fallback = TransformTemplate(templates[0], levelNumber, band.Value, 0, random);
        return PiggyFarmLevels.Validate(fallback).Count > 0
            ? ComposeResult.Fail(PiggyFarmLevels.Validate(fallback))
            : ComposeResult.Ok(fallback);
    }

    public static string? DescribeAsCoordinates(string levelJson) => null;

    #region 需求布点

    // 需求布点：剥离未要求类型 → 按"大脚印优先"依次放置要求的特殊猪 → 更新 pigCount。
    // 返回错误文本（该模板本轮不可用，换模板重试）或 null（成功，level 已就地更新）。
    private static string? PlaceRequirements(JsonNode level, Requirements req, Random random)
    {
        var pigs = level["pigs"]!.AsArray();
        var width = level["mapWidth"]!.GetValue<int>();
        var height = level["mapHeight"]!.GetValue<int>();
        var allowed = AllowedTypes(req);

        // ① 纯度剥离：未要求的特殊猪直接移除（普通猪与要求类型保留）
        for (var i = pigs.Count - 1; i >= 0; i--)
        {
            var type = pigs[i]?["pigType"]?.GetValue<string>() ?? "Normal";
            if (!allowed.Contains(type)) pigs.RemoveAt(i);
        }
        if (pigs.Count < req.Total)
        {
            return $"模板剥离未要求类型后只剩 {pigs.Count} 只，不足 {req.Total}";
        }

        // ② 布点：大脚印优先（土豪猪自由落位 → 大长猪延伸 → 1x2 族换型 → 小黄鸭收缩）
        foreach (var (type, count) in req.Types.OrderBy(kv => PlacementOrder(kv.Key)))
        {
            var remaining = count - CountType(pigs, type);
            for (var placed = 0; placed < remaining; placed++)
            {
                if (!TryPlaceOne(pigs, type, width, height, random))
                {
                    return $"模板几何无法放置 {PiggyFarmPigTypes.DisplayName(type)}（已放 {placed + CountType(pigs, type)}，差 {remaining - placed}）";
                }
            }
        }

        // ③ 总只数核对（布点不减少猪数——换型不变、鸭收缩不变、土豪猪 +1；剥离已在上限内核对）
        if (pigs.Count < req.Total)
        {
            return $"布点后只剩 {pigs.Count} 只，不足 {req.Total}";
        }
        level["pigCount"] = pigs.Count;
        return null;
    }

    // 布点顺序：大脚印优先（2x3 自由落位需要整块空地；1x3 需延伸格；1x2/1x1 随处可放）
    private static int PlacementOrder(string type) => type switch
    {
        "RichPig" => 0,
        "Long" => 1,
        "Duck" => 3,
        _ => 2,   // Normal / Boom / Zombie 等 1x2 族
    };

    // 放置一只 type 猪成功返回 true；失败 false（调用方按重试/报错处理）
    private static bool TryPlaceOne(JsonArray pigs, string type, int width, int height, Random random)
    {
        var occupied = OccupiedCells(pigs);
        var maxId = pigs.Max(p => p?["id"]?.GetValue<int>() ?? 0);
        switch (type)
        {
            case "RichPig":
            {
                // 自由 2x3/3x2 空白块落位（土豪猪脚印 2×3，随朝向旋转）
                foreach (var (facing, w, h) in Shuffled([("Up", 2, 3), ("Right", 3, 2)], random))
                {
                    var spots = new List<(int X, int Y)[]>();
                    for (var x = 0; x + w <= width; x++)
                    {
                        for (var y = 0; y + h <= height; y++)
                        {
                            var block = Rectangle(x, y, w, h);
                            if (block.Any(occupied.ContainsKey)) continue;
                            spots.Add(block);
                        }
                    }
                    if (spots.Count == 0) continue;
                    var chosen = spots[random.Next(spots.Count)];
                    AppendPig(pigs, maxId + 1, facing, "RichPig", "", chosen);
                    return true;
                }
                return false;
            }
            case "Long":
            {
                // 大长猪 = 双格猪脚印延伸一格（两端任一端空且界内即可）
                var twoCell = pigs.Where(p => CellCount(p) == 2).Shuffled(random);
                foreach (var pig in twoCell)
                {
                    var cells = CellsOf(pig);
                    var axis = cells[0].X == cells[1].X ? "V" : "H";   // 同列=竖排
                    if (axis == "V")
                    {
                        var x = cells[0].X;
                        var ys = cells.Select(c => c.Y).OrderBy(v => v).ToArray();
                        var candidates = new[] { (x, ys[0] - 1), (x, ys[^1] + 1) };
                        foreach (var (cx, cy) in candidates)
                        {
                            if (cx < 0 || cx >= width || cy < 0 || cy >= height) continue;
                            if (occupied.ContainsKey((cx, cy))) continue;
                            var facing = cy < ys[0] ? "Up" : "Down";
                            ConvertPig(pig, facing, "Long", [(cx, cy), .. cells.OrderBy(c => c.Y)]);
                            return true;
                        }
                    }
                    else
                    {
                        var y = cells[0].Y;
                        var xs = cells.Select(c => c.X).OrderBy(v => v).ToArray();
                        var candidates = new[] { (xs[0] - 1, y), (xs[^1] + 1, y) };
                        foreach (var (cx, cy) in candidates)
                        {
                            if (cx < 0 || cx >= width || cy < 0 || cy >= height) continue;
                            if (occupied.ContainsKey((cx, cy))) continue;
                            var facing = cx < xs[0] ? "Left" : "Right";
                            ConvertPig(pig, facing, "Long", [(cx, cy), .. cells.OrderBy(c => c.X)]);
                            return true;
                        }
                    }
                }
                return false;
            }
            case "Duck":
            {
                // 小黄鸭 = 1x1：把一只双格猪收缩为单格（猪数不变，腾出一格）
                var twoCell = pigs.Where(p => CellCount(p) == 2).Shuffled(random);
                foreach (var pig in twoCell)
                {
                    ShrinkToSingleCell(pig, random);
                    pig["pigType"] = "Duck";
                    return true;
                }
                return false;
            }
            default:
            {
                // 1x2 族（Normal/Boom/Zombie）：原地换型（脚印不变）
                var twoCell = pigs.Where(p => CellCount(p) == 2 && TypeOf(p) != type).Shuffled(random);
                foreach (var pig in twoCell)
                {
                    pig["pigType"] = type;
                    pig["specialInfo"] = type == "Boom" ? "3" : "";
                    return true;
                }
                return false;
            }
        }
    }

    #region 布点小工具

    private static IEnumerable<T> Shuffled<T>(this IEnumerable<T> source, Random random) =>
        source.OrderBy(_ => random.Next());

    private static (string Facing, int W, int H)[] Shuffled((string Facing, int W, int H)[] source, Random random) =>
        source.OrderBy(_ => random.Next()).ToArray();

    private static string TypeOf(JsonNode? pig) => pig?["pigType"]?.GetValue<string>() ?? "Normal";

    private static int CellCount(JsonNode? pig) => pig?["occupiedCells"]?.AsArray().Count ?? 0;

    private static List<(int X, int Y)> CellsOf(JsonNode? pig)
    {
        var result = new List<(int X, int Y)>();
        foreach (var cell in pig?["occupiedCells"]?.AsArray() ?? [])
        {
            result.Add((cell!["x"]!.GetValue<int>(), cell!["y"]!.GetValue<int>()));
        }
        return result;
    }

    private static Dictionary<(int X, int Y), int> OccupiedCells(JsonArray pigs)
    {
        var map = new Dictionary<(int X, int Y), int>();
        foreach (var pig in pigs)
        {
            var id = pig?["id"]?.GetValue<int>() ?? 0;
            foreach (var cell in CellsOf(pig))
            {
                map[cell] = id;
            }
        }
        return map;
    }

    private static (int X, int Y)[] Rectangle(int x, int y, int w, int h)
    {
        var cells = new List<(int X, int Y)>();
        for (var cx = x; cx < x + w; cx++)
        {
            for (var cy = y; cy < y + h; cy++)
            {
                cells.Add((cx, cy));
            }
        }
        return cells.ToArray();
    }

    // 类型换型/改脚印/新建猪统一走这里：写类型 + 朝向 + 脚印 + 清理 specialInfo
    private static void ConvertPig(JsonNode? pig, string facing, string type, List<(int X, int Y)> cells)
    {
        pig!["facing"] = facing;
        pig["pigType"] = type;
        pig["specialInfo"] = type == "Boom" ? "3" : "";
        var arr = new JsonArray();
        foreach (var (x, y) in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            arr.Add(new JsonObject { ["x"] = x, ["y"] = y });
        }
        pig["occupiedCells"] = arr;
    }

    private static void AppendPig(JsonArray pigs, int id, string facing, string type, string specialInfo, (int X, int Y)[] cells)
    {
        var arr = new JsonArray();
        foreach (var (x, y) in cells)
        {
            arr.Add(new JsonObject { ["x"] = x, ["y"] = y });
        }
        pigs.Add(new JsonObject
        {
            ["id"] = id,
            ["facing"] = facing,
            ["pigType"] = type,
            ["specialInfo"] = specialInfo,
            ["occupiedCells"] = arr,
        });
    }

    // 双格猪收缩为单格：保留随机一端（另一端腾空）
    private static void ShrinkToSingleCell(JsonNode? pig, Random random)
    {
        var cells = pig?["occupiedCells"]!.AsArray();
        var keep = random.Next(cells!.Count);
        var keepCell = cells[keep]!.DeepClone();
        pig!["occupiedCells"] = new JsonArray(keepCell);
    }

    private static int CountType(JsonArray pigs, string type)
    {
        var n = 0;
        foreach (var pig in pigs)
        {
            if ((pig?["pigType"]?.GetValue<string>() ?? "Normal") == type) n++;
        }
        return n;
    }

    #endregion

    #endregion

    // 1 档：真实 000001 的 6 猪互锁模块，按种子镜像后平铺 1~3 个槽位并求解验证
    private static ComposeResult BuildModules(string? levelNumber, int count, int seed)
    {
        var random = new Random(seed);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var slots = new[] { (0, 0), (9, 0), (0, 7), (9, 7), (3, 15), (6, 15) }
                .OrderBy(_ => random.Next()).Take(count).ToArray();
            var json = BuildModuleJson(levelNumber, slots, random.Next());
            var errors = PiggyFarmLevels.Validate(json);
            if (errors.Count > 0) continue;
            var level = PiggyFarmLevels.Load(json);
            var solve = new PiggyFarmSolver().Solve(level, TimeSpan.FromSeconds(1), 50_000, CancellationToken.None);
            if (solve.Status == SolveStatus.Solved) return ComposeResult.Ok(json);
        }
        return ComposeResult.Reject("小关模板布局未找到可解组合，请重试");
    }

    private static string BuildModuleJson(string? levelNumber, (int Ox, int Oy)[] slots, int mirrorSeed)
    {
        var pigs = new JsonArray();
        var occupied = new HashSet<(int, int)>();
        var id = 0;
        foreach (var (ox, oy) in slots)
        {
            foreach (var (facing, cells) in Module)
            {
                var outCells = new JsonArray();
                foreach (var (x, y) in cells)
                {
                    var gx = x + ox;
                    var gy = y + oy;
                    if (!occupied.Add((gx, gy))) return "{}";
                    outCells.Add(new JsonObject { ["x"] = gx, ["y"] = gy });
                }
                pigs.Add(new JsonObject
                {
                    ["id"] = id++,
                    ["facing"] = facing.ToString(),
                    ["pigType"] = "Normal",
                    ["specialInfo"] = "",
                    ["occupiedCells"] = outCells,
                });
            }
        }
        return Serialize(levelNumber, 1, pigs, "v1-module-1档");
    }

    // 真实模板：重设 levelId/difficulty/levelName，按种子做四种对称变换（保持 12×19 与可解性）
    private static string TransformTemplate(
        string templateJson, string? levelNumber, int band, int symmetry, Random random)
    {
        var node = JsonNode.Parse(templateJson)!;
        var pigs = node["pigs"]!.AsArray();
        foreach (var pigNode in pigs)
        {
            var pig = pigNode!.AsObject();
            var cells = pig["occupiedCells"]!.AsArray();
            foreach (var cellNode in cells)
            {
                var cell = cellNode!.AsObject();
                var x = cell["x"]!.GetValue<int>();
                var y = cell["y"]!.GetValue<int>();
                if (symmetry is 1 or 3) x = PiggyFarmTemplates.Width - 1 - x;
                if (symmetry is 2 or 3) y = PiggyFarmTemplates.Height - 1 - y;
                cell["x"] = x;
                cell["y"] = y;
            }
            if (symmetry is 1 or 3)
            {
                var facing = pig["facing"]!.GetValue<string>();
                pig["facing"] = facing switch { "Left" => "Right", "Right" => "Left", _ => facing };
            }
            if (symmetry is 2 or 3)
            {
                var facing = pig["facing"]!.GetValue<string>();
                pig["facing"] = facing switch { "Up" => "Down", "Down" => "Up", _ => facing };
            }
        }
        node["levelId"] = int.TryParse(levelNumber, out var parsed) ? parsed : 900 + band;
        node["difficulty"] = band;
        node["pigCount"] = pigs.Count;
        node["levelName"] = $"v2-template-{band}档-{pigs.Count}猪";
        return node.ToJsonString();
    }

    private static string Serialize(string? levelNumber, int band, JsonArray pigs, string name)
    {
        var node = new JsonObject
        {
            ["levelId"] = int.TryParse(levelNumber, out var parsed) ? parsed : 900 + band,
            ["difficulty"] = band,
            ["levelName"] = name,
            ["mapWidth"] = PiggyFarmTemplates.Width,
            ["mapHeight"] = PiggyFarmTemplates.Height,
            ["pigCount"] = pigs.Count,
            ["pigs"] = pigs,
            ["terrainElements"] = new JsonArray(),
        };
        return node.ToJsonString();
    }

    private static int CountPigs(string levelJson)
    {
        try
        {
            return JsonNode.Parse(levelJson)?["pigCount"]?.GetValue<int>() ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static IReadOnlyDictionary<string, int> CountTypes(string levelJson)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var pigs = JsonNode.Parse(levelJson)?["pigs"]?.AsArray();
            if (pigs is null) return counts;
            foreach (var pig in pigs)
            {
                var type = pig?["pigType"]?.GetValue<string>() ?? "";
                if (type.Length > 0) counts[type] = counts.GetValueOrDefault(type) + 1;
            }
        }
        catch (JsonException)
        {
        }
        return counts;
    }

    private sealed record Requirements(int Total, IReadOnlyDictionary<string, int> Types);

    // 解析"N只猪"与"N只<类型>"——类型词不以"猪"结尾也算（如"30只小黄鸭"）；
    // 类型名走 PiggyFarmPigTypes 别名表，未知/不支持的类型直接给错误，绝不静默丢弃
    private static Requirements ParseRequirements(string? text, List<string> errors)
    {
        var types = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        if (string.IsNullOrWhiteSpace(text)) return new Requirements(0, types);
        foreach (Match match in Regex.Matches(text, @"(\d+)\s*只([\u4e00-\u9fa5A-Za-z]{0,10})"))
        {
            if (!int.TryParse(match.Groups[1].Value, out var count) || count <= 0) continue;
            var typeText = match.Groups[2].Value.Trim();
            if (typeText.EndsWith("猪", StringComparison.Ordinal)) typeText = typeText[..^1];   // "土豪猪"→"土豪"；裸"猪"→""（总只数）
            if (typeText.Length == 0)
            {
                total = Math.Max(total, count);
                continue;
            }
            var type = PiggyFarmPigTypes.Resolve(typeText);
            if (type is null)
            {
                errors.Add($"无法识别的猪类型「{typeText}」；请用：普通猪/大长猪/土豪猪/爆炸猪/小黄鸭/丧尸猪");
                continue;
            }
            if (!PiggyFarmPigTypes.Supported.Contains(type))
            {
                errors.Add($"v1 支持子集之外：{PiggyFarmPigTypes.DisplayName(type)}（{type}）未建模；当前支持普通猪/大长猪/土豪猪/爆炸猪/小黄鸭/丧尸猪");
                continue;
            }
            types[type] = Math.Max(types.GetValueOrDefault(type), count);
        }
        return new Requirements(total, types);
    }

    private static int? TryReadBand(string modelOutput)
    {
        var text = modelOutput?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonNode.Parse(text[start..(end + 1)])?["band"]?.GetValue<int>();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
