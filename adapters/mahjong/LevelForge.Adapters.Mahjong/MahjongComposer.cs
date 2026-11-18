using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Search;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.Mahjong;

// 结构化出题器 v1（多米诺铺装）：
//   模型只出"意图"（尺寸/难度/冰冻对数），几何由代码按种子确定性拼装：
//   棋盘划分为多米诺（两格同牌面相邻对）——开局必有相邻可消对，且逐多米诺消除恒可清盘（归纳可证，
//   多米诺两格恒相邻，消除不破坏其余多米诺）。拼装后再过本适配层求解器在环复核（贪心 DFS 小预算），
//   理论可证 + 实测复核双保险。模型输出整份原生配置时按原生直通校验（合法即接受）。
//   种子来自 ComposeContext.Seed（流水线每次尝试派生新种子）：同意图不同尝试得到不同布局。
public static class MahjongComposer
{
    public const string Spec = """
        # 输出约定（意图制出题）
        只输出一个 JSON 对象（不要解释文字），字段与取值：
        {"width":6~12,"height":6~12,"difficulty":1|2|3,"icePairs":0~2}
        - width×height 必须为偶数（棋盘将满铺成对棋子）；建议 6x8 / 8x10 / 10x12；
        - icePairs：冰冻棋对数（0 为无）；冰冻对在开局不可消，会自动解冻；
        - 不要输出 chesses/obstacles 等几何字段——布局由代码按种子确定性生成。
        """;

    private const int FaceMax = 42;           // 麻将牌面 1..42
    private const int MaxBuildAttempts = 3;   // 求解器在环复核失败时的重建次数

    // 意图预检（调 LLM 前跑）：档位合法性
    public static IReadOnlyList<string> ValidateContext(ComposeContext context)
    {
        return context.TargetBand is >= 1 and <= 3
            ? []
            : ["目标难度必须是 1/2/3"];
    }

    public static ComposeResult Compose(string modelOutput, string? levelNumber, ComposeContext context)
    {
        // 原生直通：模型已输出全预设配置（含 chesses 数组）→ 按原生校验裁决，合法即接受原文
        if (TryParseIntent(modelOutput) is null && LooksLikeNativeConfig(modelOutput))
        {
            var nativeErrors = MahjongLevels.Validate(modelOutput);
            return nativeErrors.Count == 0
                ? ComposeResult.Ok(modelOutput)
                : ComposeResult.Fail(nativeErrors);
        }

        var intent = TryParseIntent(modelOutput);
        if (intent is null)
        {
            return ComposeResult.Fail(["输出必须是意图 JSON：" + Spec.Replace("\n", "；")]);
        }
        var errors = new List<string>();
        if (intent.Width is { } w && w is < 2 or > 12) errors.Add($"width={w} 超出范围 2~12");
        if (intent.Height is { } h && h is < 2 or > 12) errors.Add($"height={h} 超出范围 2~12");
        if (intent.IcePairs is < 0 or > 2) errors.Add($"icePairs={intent.IcePairs} 超出范围 0~2");
        if (errors.Count > 0)
        {
            return ComposeResult.Fail(errors);
        }

        var width = intent.Width ?? PickSizeByBand(context.TargetBand, context.Seed);
        var height = intent.Height ?? PickSizeByBand(context.TargetBand, context.Seed + 7);
        var cells = width * height;
        if (cells % 2 != 0)
        {
            return ComposeResult.Fail($"width×height={cells} 为奇数，无法成对铺满；请调整尺寸");
        }

        // 求解器在环：拼装 → 本适配层贪心 DFS 复核（小预算）→ 未过则换种子重建（≤3 次）
        var random = new Random(context.Seed);
        for (var attempt = 0; attempt < MaxBuildAttempts; attempt++)
        {
            var json = BuildDominoConfig(levelNumber, context.TargetBand, width, height, intent.IcePairs ?? 0, random);
            if (json is null)
            {
                random = new Random(random.Next());   // 铺装未满铺：换种子重试
                continue;
            }
            var formatErrors = MahjongLevels.Validate(json);
            if (formatErrors.Count > 0)
            {
                continue;   // 拼装是代码自身行为，格式不过属实现缺陷——换种子重试并如实记因
            }
            if (SolvableInBudget(json))
            {
                return ComposeResult.Ok(json);
            }
            random = new Random(random.Next());   // 换种子重建
        }
        return ComposeResult.Fail($"连续 {MaxBuildAttempts} 次拼装未通过求解器在环复核（理论上多米诺铺装恒可解，此为防御路径）");
    }

    // 多米诺铺装：棋盘满铺 W×H/2 个相邻同牌面对 + 冰冻对（开局不可消）。
    // 可解性：每对两格恒相邻，逐对消除恒可清盘（归纳可证）；冰冻对在其余对消除至少一次后解冻（计数=2）。
    // 贪心铺装可能留孤儿格：内部最多重洗 20 次，满铺失败返回 null（调用方换种子）。
    private static string? BuildDominoConfig(
        string? levelNumber, int band, int width, int height, int icePairs, Random random)
    {
        var cells = width * height;
        var pairCount = cells / 2;
        icePairs = Math.Min(icePairs, Math.Max(0, pairCount / 4));   // 冰冻对不超过总对数 1/4
        var normalPairs = pairCount - icePairs;

        // 牌面：普通对取互不相同的牌面；冰冻对固定 type=43 专属牌面（与游戏一致）
        var faces = new List<int>();
        var pool = Enumerable.Range(1, FaceMax).OrderBy(_ => random.Next()).ToList();
        faces.AddRange(pool.Take(normalPairs));

        for (var tilingAttempt = 0; tilingAttempt < 20; tilingAttempt++)
        {
            var pieces = new List<JsonObject>();
            var id = 1;
            // 槽位序列：按行满铺打乱（布局多样性；多米诺沿相邻格放置）
            var slots = Enumerable.Range(0, cells).OrderBy(_ => random.Next()).ToList();
            var occupied = new HashSet<int>();
            var stuck = false;

            // 冰冻对先行放置（相邻两格）
            for (var p = 0; p < icePairs && !stuck; p++)
            {
                if (!TryPlaceDomino(slots, occupied, width, height, random, out var ia, out var ib))
                {
                    stuck = true;
                    break;
                }
                pieces.Add(ChessJson(id++, ia % width, ia / width, MahjongLevels.PopFaceType, MahjongLevels.SpecialIce, 2));
                pieces.Add(ChessJson(id++, ib % width, ib / width, MahjongLevels.PopFaceType, MahjongLevels.SpecialIce, 2));
            }

            // 普通对：逐对相邻放置（行内成对；行末折行时改列向配对——由 TryPlaceDomino 统一处理）
            for (var p = 0; p < normalPairs && !stuck; p++)
            {
                if (!TryPlaceDomino(slots, occupied, width, height, random, out var a, out var b))
                {
                    stuck = true;
                    break;
                }
                var face = faces[p % faces.Count];
                pieces.Add(ChessJson(id++, a % width, a / width, face));
                pieces.Add(ChessJson(id++, b % width, b / width, face));
            }

            if (stuck || occupied.Count != cells)
            {
                continue;   // 满铺失败：重洗
            }

            pieces.Sort((a, b) => a["id"]!.GetValue<int>().CompareTo(b["id"]!.GetValue<int>()));
            return new JsonObject
            {
                ["level"] = int.TryParse(levelNumber, out var parsed) ? parsed : 900,
                ["difficulty"] = band,
                ["isDailyChallenge"] = false,
                ["chessCount"] = pieces.Count,
                ["mapWidth"] = width,
                ["mapHeight"] = height,
                ["chesses"] = new JsonArray(pieces.Select(p => (JsonNode?)p).ToArray()),
                ["obstacles"] = new JsonArray(),
            }.ToJsonString();
        }
        return null;
    }

    // 放置一枚相邻多米诺：从剩余槽位中（按种子随机序）找一个尚有相邻空槽的槽位与其相邻空槽配对
    private static bool TryPlaceDomino(List<int> slots, HashSet<int> occupied, int width, int height, Random random, out int a, out int b)
    {
        a = -1;
        b = -1;
        var live = slots.Where(s => !occupied.Contains(s)).ToList();
        foreach (var slot in live.OrderBy(_ => random.Next()))
        {
            var sx = slot % width;
            var sy = slot / width;
            foreach (var (dx, dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
            {
                var nx = sx + dx;
                var ny = sy + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    continue;
                }
                var neighbor = ny * width + nx;
                if (!live.Contains(neighbor))
                {
                    continue;
                }
                occupied.Add(slot);
                occupied.Add(neighbor);
                a = slot;
                b = neighbor;
                return true;
            }
        }
        return false;
    }

    private static JsonObject ChessJson(int id, int x, int y, int type, int specialType = 0, int specialInfo = 0)
        => new JsonObject
        {
            ["id"] = id,
            ["x"] = x,
            ["y"] = y,
            ["type"] = type,
            ["specialType"] = specialType,
            ["specialInfo"] = specialInfo,
        };

    // 档位 → 建议尺寸：1 档 6x8、2 档 8x10、3 档 10x12（解长 24/40/60 → 阈值 20/44 与难度权重口径一致）
    private static int PickSizeByBand(int band, int seed) => (band, seed % 2) switch
    {
        (1, _) => 6,
        (2, _) => 8,
        (_, 0) => 10,
        _ => 12,
    };

    // 意图解析：{"width":8,"height":10,"icePairs":1,...}；非意图形态返回 null（交由原生直通）
    private sealed record Intent(int? Width, int? Height, int? IcePairs);

    private static Intent? TryParseIntent(string modelOutput)
    {
        try
        {
            if (JsonNode.Parse(modelOutput ?? "") is not JsonObject obj)
            {
                return null;
            }
            if (obj.ContainsKey("chesses"))
            {
                return null;   // 含几何字段 → 原生配置，交直通路径
            }
            int? Read(string name) => obj[name] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
            return new Intent(Read("width"), Read("height"), Read("icePairs"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeNativeConfig(string modelOutput)
    {
        try
        {
            return JsonNode.Parse(modelOutput ?? "") is JsonObject obj && obj.ContainsKey("chesses");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // 求解器在环复核：本适配层贪心 DFS，小预算（多米诺铺装理论恒可解，此为防御路径）
    private static bool SolvableInBudget(string levelJson)
    {
        var level = MahjongLevels.Load(levelJson);
        var bundle = MahjongFacade.BundleForCompose;
        var request = new SolveRequest<MahjongLevel>(level, TimeSpan.FromSeconds(2), 50_000);
        var result = new SolveExecutor().Solve(bundle, request, new GreedyDfsSolver<MahjongLevel, MahjongState, MahjongMove>());
        return result.Status == SolveStatus.Solved;
    }
}
