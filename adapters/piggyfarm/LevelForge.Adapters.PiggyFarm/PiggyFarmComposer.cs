using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm;

// 结构化出题器（真实关卡模板 + 对称变体 + 小关模块）：
//   - 2/3 档：从项目原生关卡模板库取真实关卡（2 档=原生 difficulty 2 且可解的 374；
//     3 档=原生 difficulty 3 的 19/29/59 + 112 猪密集模板），按种子做 4 种对称变换并重编号；
//   - 1 档：000001 的 6 猪互锁模块（1~3 个），保持轻量；
//   - 每条都必须过原生校验器，并由平台难度模型命中目标档（流水线最终判定）。
// 这样每次生成都是"有真实设计挑战"的关卡，而不是靠解长堆出来的规则网格。
public static class PiggyFarmComposer
{
    public const string Spec = """
        # 输出约定（结构化出题）
        只输出一个 JSON 对象：{}（推荐）或 {"band": 1|2|3}（仅作提示）。
        实际档位以本次目标难度为准；不要输出关卡几何、不要输出 pigs/mapWidth 等字段。
        """;

    private const int MapCapacityPigs = 112;   // 12×19 带多朝向/可解的实际上限（物理裸上限 114）

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

        var capacity = band switch { 1 => 18, 2 => 77, 3 => MapCapacityPigs, _ => 0 };
        if (req.Total > capacity)
        {
            errors.Add($"目标 {band} 档可用模板最多容纳 {capacity} 只（3 档最大 {MapCapacityPigs}）；请换档或降低数量");
        }

        var templates = band == 2 ? PiggyFarmTemplates.Band2 : PiggyFarmTemplates.Band3;
        foreach (var (type, count) in req.Types)
        {
            var max = type == "Boom"
                ? templates.Max(t => CountTypes(t).GetValueOrDefault("Normal"))
                : templates.Max(t => CountTypes(t).GetValueOrDefault(type));
            if (count > max)
            {
                errors.Add($"目标 {band} 档模板最多支持 {max} 只{PiggyFarmPigTypes.DisplayName(type)}；要求 {count} 只，请降低或换档");
            }
        }
        return errors;
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
        var candidates = templates
            .Select(t => (Json: t, Pigs: CountPigs(t), Types: CountTypes(t)))
            .Where(t => required <= t.Pigs)
            .Where(t => req.Types.All(kv => kv.Key == "Boom"
                ? t.Types.GetValueOrDefault("Normal") >= kv.Value
                : t.Types.GetValueOrDefault(kv.Key) >= kv.Value))
            .ToList();
        // 3 档优先真实原生 3 档模板（19/29/59）；112 猪密集模板只在需求 >96 时兜底
        if (band == 3 && required <= 96)
        {
            candidates = candidates.Where(t => t.Pigs <= 96).ToList();
        }
        if (candidates.Count == 0)
        {
            return ComposeResult.Reject($"目标 {band} 档没有满足 {required} 只/类型要求的模板");
        }
        // 模板 + 对称 + 随机删猪：不是只换镜像。删 1~8 只后重新求解/判档，
        // 同一个提示词/难度重新生成会得到不同的关卡（仍保持目标档）。
        var random = new Random(context.Seed ^ 0x5f3759df);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var chosen = candidates[random.Next(candidates.Count)];
            var symmetry = random.Next(4);
            var maxRemove = Math.Max(0, Math.Min(8, chosen.Pigs - required));
            // 有猪类型要求时不删猪，避免把要求的类型删掉；变化交给模板/对称/Boom 转换
            var remove = context.Seed == 0 && context.SpecialRequirements is null
                ? 0
                : req.Types.Count == 0 && maxRemove > 0 ? random.Next(0, maxRemove + 1) : 0;
            var json = TransformTemplate(chosen.Json, levelNumber, band.Value, symmetry, remove, random);
            if (req.Types.TryGetValue("Boom", out var boomCount) && boomCount > 0)
            {
                json = ConvertNormalsToBoom(json, boomCount, random);
            }
            if (PiggyFarmLevels.Validate(json).Count > 0) continue;

            var outcome = new PiggyFarmSolver().Solve(
                PiggyFarmLevels.Load(json), TimeSpan.FromSeconds(5), 200_000, CancellationToken.None);
            if (outcome.Status != SolveStatus.Solved) continue;
            var weights = PiggyFarmContent.Weights;
            var score = weights.SolutionLengthWeight * outcome.Solution.Count
                      + weights.BranchingWeight * (outcome.Metrics?.AvgBranchingFactor ?? 0);
            var outBand = score < weights.Thresholds[0] ? 1 : score < weights.Thresholds[1] ? 2 : 3;
            if (outBand == band.Value)
            {
                return ComposeResult.Ok(json);
            }
        }

        // 有类型要求时不允许"回退到不带该类型的模板"：宁可明确失败，也不静默少猪
        if (req.Types.Count > 0)
        {
            return ComposeResult.Reject(
                $"模板在 {band} 档无法同时满足类型要求（{string.Join("、", req.Types.Select(kv => $"{PiggyFarmPigTypes.DisplayName(kv.Key)}×{kv.Value}"))}）且可解；请换档或降低要求");
        }

        // 兜底：原始模板（不做删猪）至少保证可展示
        var fallback = TransformTemplate(candidates[0].Json, levelNumber, band.Value, 0, 0, random);
        return PiggyFarmLevels.Validate(fallback).Count > 0
            ? ComposeResult.Fail(PiggyFarmLevels.Validate(fallback))
            : ComposeResult.Ok(fallback);
    }

    public static string? DescribeAsCoordinates(string levelJson) => null;

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
        string templateJson, string? levelNumber, int band, int symmetry, int removeCount, Random random)
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
        if (removeCount > 0)
        {
            var indices = Enumerable.Range(0, pigs.Count)
                .OrderBy(_ => random.Next())
                .Take(removeCount)
                .OrderByDescending(i => i)
                .ToList();
            foreach (var index in indices)
            {
                pigs.RemoveAt(index);
            }
            for (var i = 0; i < pigs.Count; i++)
            {
                pigs[i]!["id"] = i;
            }
        }
        node["levelId"] = int.TryParse(levelNumber, out var parsed) ? parsed : 900 + band;
        node["difficulty"] = band;
        node["pigCount"] = pigs.Count;
        node["levelName"] = $"v1-template-{band}档-{pigs.Count}猪";
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

    // 把模板里随机的 Normal 猪改成 Boom（脚印不变，specialInfo 写剩余爆炸次数），其余类型要求靠模板选择
    private static string ConvertNormalsToBoom(string json, int count, Random random)
    {
        var node = JsonNode.Parse(json)!;
        var pigs = node["pigs"]!.AsArray();
        var normals = Enumerable.Range(0, pigs.Count)
            .Where(i => pigs[i]?["pigType"]?.GetValue<string>() == "Normal")
            .OrderBy(_ => random.Next())
            .Take(count)
            .ToList();
        foreach (var index in normals)
        {
            pigs[index]!["pigType"] = "Boom";
            pigs[index]!["specialInfo"] = "3";
        }
        return node.ToJsonString();
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

    // 解析"N只猪"与"N只<类型>猪"；类型名走 PiggyFarmPigTypes 别名表，未知/不支持的类型直接给错误
    private static Requirements ParseRequirements(string? text, List<string> errors)
    {
        var types = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        if (string.IsNullOrWhiteSpace(text)) return new Requirements(0, types);
        foreach (Match match in Regex.Matches(text, @"(\d+)\s*只([\u4e00-\u9fa5A-Za-z]*?)猪"))
        {
            if (!int.TryParse(match.Groups[1].Value, out var count) || count <= 0) continue;
            var typeText = match.Groups[2].Value.Trim();
            if (typeText.Length == 0)
            {
                total = Math.Max(total, count);
                continue;
            }
            var type = PiggyFarmPigTypes.Resolve(typeText);
            if (type is null)
            {
                errors.Add($"无法识别的猪类型「{typeText}」；请用：普通猪/大长猪/土豪猪/爆炸猪");
                continue;
            }
            if (!PiggyFarmPigTypes.Supported.Contains(type))
            {
                errors.Add($"v1 支持子集之外：{PiggyFarmPigTypes.DisplayName(type)}（{type}）未建模；当前支持普通猪/大长猪/土豪猪/爆炸猪");
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

    private static int ParseRequiredPigs(string? requirements)
    {
        if (string.IsNullOrWhiteSpace(requirements)) return 0;
        var match = Regex.Match(requirements, @"(\d+)\s*只猪");
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
    }
}
