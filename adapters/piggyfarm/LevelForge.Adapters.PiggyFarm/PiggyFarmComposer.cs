using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm;

// 结构化出题器 v3（合成式，无模板）：
//   - 几何由 PiggyFarmSynthesizer 逆向构造：先造一条合法解，再把解反过来当关卡 → 天生可解；
//   - 每次生成按种子取"成型计划"（猪数/通道数/尾巴槽位/长猪配比），不同种子得到不同骨架；
//   - 需求即契约：有类型需求时，未要求的特殊猪从合成结果上剥离（纯度），要求的类型按脚印就地布点；
//   - 每条都必须过原生校验器 + 质量门，并由平台难度模型命中目标档（流水线最终判定）。
public static class PiggyFarmComposer
{
    public const string Spec = """
        # 输出约定（结构化出题）
        只输出一个 JSON 对象：{}（推荐）或 {"band": 1|2|3}（仅作提示）。
        实际档位以本次目标难度为准；不要输出关卡几何、不要输出 pigs/mapWidth 等字段。
        """;

    // 12×19 带多朝向/可解的实际上限（物理裸上限 114 = 228 格 ÷ 2）
    private const int MapCapacityPigs = 112;

    // 合成重试次数：单次合成只要几毫秒，重试预算主要花在"求解 + 判档"上
    private const int MaxAttempts = 10;

    // 各档的最低猪数：解长 ≥ 猪数，猪数直接决定难度下限
    private static int MinPigs(int band) => band switch { 1 => 6, 2 => 60, _ => 100 };

    public static IReadOnlyList<string> ValidateContext(ComposeContext context)
    {
        var band = context.TargetBand;
        if (band is < 1 or > 3) return ["目标难度必须是 1/2/3"];
        var errors = new List<string>();
        var req = ParseRequirements(context.SpecialRequirements, errors);
        if (errors.Count > 0) return errors;

        // 有效总只数 = max(裸"N只猪"，最大单类型需求数)——"500只土豪猪"的 500 计在类型里而非总数里
        var effectiveTotal = Math.Max(req.Total, req.Types.Count > 0 ? req.Types.Values.Max() : 0);
        var cap = BandCapacity(band);
        if (effectiveTotal > cap)
        {
            errors.Add($"目标 {band} 档最多 {cap} 只（12×19 全 1×2 猪的物理上限 {MapCapacityPigs}；再多解长会超出该档）；请换档或降低数量");
        }
        // 小黄鸭自动离场、不产生点击：要凑够 3 档解长，鸭之外还得有 ≥100 只可点猪
        var ducks = req.Types.TryGetValue("Duck", out var duckCount) ? duckCount : 0;
        if (ducks > 0 && band == 3 && 100 + ducks > cap)
        {
            errors.Add($"3 档需要解长 ≥100，而小黄鸭不产生点击：{ducks} 只鸭之外还要 ≥100 只可点猪，" +
                       $"合计超过 {cap} 只上限；请减少鸭数或降档");
        }
        if (band == 1 && req.Types.Count > 0)
        {
            errors.Add("1 档小关（12~16 只）不支持指定猪类型：合成器按普通猪铺排，请换 2/3 档");
        }
        return errors;
    }

    // 各档的猪数上限：解长 ≥ 猪数，超过上限必然判到更高档（1 档 <20、2 档 <100、3 档不限）
    private static int BandCapacity(int band) => band switch { 1 => 18, 2 => 96, _ => MapCapacityPigs };

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
        if (band == 1 && req.Types.Count > 0)
        {
            return ComposeResult.Reject("1 档小关只支持普通猪，无法满足指定猪类型");
        }

        var random = new Random(context.Seed ^ 0x5f3759df);
        var lastObstacle = "未知";
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var plan = PlanFor(band.Value, req, random);
            var synth = PiggyFarmSynthesizer.Synthesize(plan);
            if (synth is null)
            {
                lastObstacle = "合成器未能布点";
                continue;
            }
            if (synth.PigCount < Math.Max(MinPigs(band.Value), req.Total))
            {
                lastObstacle = $"合成 {synth.PigCount} 只，不足 {Math.Max(MinPigs(band.Value), req.Total)}";
                continue;
            }

            var levelId = int.TryParse(levelNumber, out var parsed) ? parsed : 900 + band.Value;
            var json = PiggyFarmSynthesizer.Serialize(
                synth.Pigs, levelId, band.Value, $"v3-synth-{band.Value}档-{synth.PigCount}猪");

            // 需求布点：剥离未要求类型 + 把要求的特殊猪放置到合成结果上（换型/收缩/延伸/落位）
            var node = JsonNode.Parse(json)!;
            if (req.Types.Count > 0)
            {
                var placeError = PlaceRequirements(node, req, random);
                if (placeError is not null)
                {
                    lastObstacle = placeError;
                    continue;
                }
            }

            // 导出规范化：id=索引 / pigCount=最终长度 / 补缺省结构 / 名称用最终数量
            FinalizeExport(node, $"v3-synth-{band.Value}档-{node["pigs"]!.AsArray().Count}猪");
            var finalJson = node.ToJsonString();
            var formatErrors = PiggyFarmLevels.Validate(finalJson);
            if (formatErrors.Count > 0)
            {
                lastObstacle = string.Join("；", formatErrors.Take(2));
                continue;
            }
            var pigCount = PiggyFarmLevels.Load(finalJson).Pigs.Count;
            if (pigCount < Math.Max(MinPigs(band.Value), req.Total))
            {
                lastObstacle = $"布点后只剩 {pigCount} 只，不足 {Math.Max(MinPigs(band.Value), req.Total)}";
                continue;
            }

            // 质量门自查（与流水线 QualityErrors 同一份标准，环内提前拦截，不合格不烧候选预算）
            var qualityErrors = PiggyFarmQuality.Errors(finalJson);
            if (qualityErrors.Count > 0)
            {
                lastObstacle = string.Join("；", qualityErrors.Take(2));
                continue;
            }

            var outcome = new PiggyFarmSolver().Solve(
                PiggyFarmLevels.Load(finalJson), TimeSpan.FromSeconds(5), 200_000, CancellationToken.None);
            if (outcome.Status != SolveStatus.Solved)
            {
                lastObstacle = "合成后不可解(" + outcome.Status + ", len=" + outcome.Solution.Count + ")";
                continue;
            }
            var weights = PiggyFarmContent.Weights;
            var score = weights.SolutionLengthWeight * outcome.Solution.Count
                      + weights.BranchingWeight * (outcome.Metrics?.AvgBranchingFactor ?? 0);
            var outBand = score < weights.Thresholds[0] ? 1 : score < weights.Thresholds[1] ? 2 : 3;
            if (outBand == band.Value)
            {
                return ComposeResult.Ok(finalJson);
            }
            lastObstacle = $"合成后判档 {outBand} ≠ 目标 {band}（解长 {outcome.Solution.Count}，分 {score:F1}）";
        }

        // 需求即契约：宁可明确失败（说明几何/判档障碍），也不静默少猪或混入未要求类型
        var demand = req.Types.Count > 0
            ? string.Join("、", req.Types.Select(kv => $"{PiggyFarmPigTypes.DisplayName(kv.Key)}×{kv.Value}"))
            : $"≥{Math.Max(MinPigs(band.Value), req.Total)} 只";
        return ComposeResult.Reject(
            $"{MaxAttempts} 次合成尝试未能生成满足要求的 {band} 档关卡（{demand}；障碍：{lastObstacle}）；请换档或调整数量");
    }

    // 成型计划：档位决定猪数区间（解长≥猪数），种子决定通道/封口脊细节，同档不同种子得到不同骨架
    private static PiggyFarmSynthesizer.Plan PlanFor(int band, Requirements req, Random random)
    {
        var (minPigs, maxPigs) = band switch
        {
            1 => (12, 16),
            2 => (90, 96),
            _ => (110, 112),
        };
        var target = Math.Max(req.Total, random.Next(minPigs, maxPigs + 1));
        target = Math.Min(target, MapCapacityPigs);
        // 小黄鸭自动离场、不产生点击：要多铺等量的"可点猪"才能把解长顶到目标档
        var ducks = req.Types.TryGetValue("Duck", out var duckCount) ? duckCount : 0;
        target = Math.Min(MapCapacityPigs, target + ducks);
        var slides = band switch { 1 => random.Next(0, 2), 2 => random.Next(2, 8), _ => random.Next(6, 14) };
        // 1 档不需要"贴边尾巴"（棋盘很空）：尾巴猪本身全是可走猪，会拉高可走比例、撞质量门；
        // 2/3 档改走"封口脊"（两侧边列铺竖链，把横链出口挡住），尾巴槽位随之取消
        var useSpines = band >= 2;
        var lanes = band == 1 ? 0 : 1;
        var tail = useSpines ? 0 : (band == 1 ? 1 : random.Next(4, 7));
        return new PiggyFarmSynthesizer.Plan(
            PigCount: target,
            Slides: slides,
            TailCount: tail,
            LaneCount: lanes,
            // 需求即契约：没要求的特殊猪一律不出现（合成器只铺普通猪）。
            // 旧实现按"真实关卡原貌"保留模板自带的 2 只土豪猪，导致每次生成的中间位置都蹲着同样的两只金猪。
            LongRatio: 0.0,
            RichRatio: 0.0,
            Seed: random.Next(),
            ReserveRich: req.Types.GetValueOrDefault("RichPig"),
            ReserveLong: req.Types.GetValueOrDefault("Long"),
            UseSpines: useSpines);
    }

    public static string? DescribeAsCoordinates(string levelJson) => null;

    #region 需求布点

    // 需求布点：剥离未要求类型 → 按"大脚印优先"依次放置要求的特殊猪 → 更新 pigCount。
    // 返回错误文本（该轮不可用，换一次合成重试）或 null（成功，level 已就地更新）。
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
            return $"剥离未要求类型后只剩 {pigs.Count} 只，不足 {req.Total}";
        }

        // ①b 大批量气球（≥10）：区域气球墙布点——顶部整宽区域横排气球（朝右，解绳后整排依次向右
        //    离场），同色剪刀 3 只放区域右缘（出口畅通，先于解绳离场）。随机散布 30 只系留气球会
        //    把剪刀困死在墙里导致不可解——真实关卡设计都是气球成排靠边 + 剪刀留走廊。
        var balloonDemand = req.Types.FirstOrDefault(kv => kv.Key is "Balloon" or "BalloonLong" && kv.Value >= 10);
        if (balloonDemand.Key is not null)
        {
            var wallError = PlaceBalloonWall(level, pigs, balloonDemand.Key, balloonDemand.Value, width, height);
            if (wallError is not null)
            {
                return wallError;
            }
        }

        // ② 布点：大脚印优先（土豪猪自由落位 → 大长猪延伸 → 1x2 族换型 → 小黄鸭收缩）
        foreach (var (type, count) in req.Types.OrderBy(kv => PlacementOrder(kv.Key)))
        {
            var remaining = count - CountType(pigs, type);
            for (var placed = 0; placed < remaining; placed++)
            {
                if (!TryPlaceOne(pigs, type, width, height, random))
                {
                    return $"合成几何无法放置 {PiggyFarmPigTypes.DisplayName(type)}（已放 {placed + CountType(pigs, type)}，差 {remaining - placed}）";
                }
            }
        }

        // ②b 气球配套剪刀：绳只能被同色剪刀"离场"剪断（Balloon 3 绳 / BalloonLong 5 绳，每根一次同色
        //    剪刀离场）——按色补足剪刀猪（改写普通猪），保证布点后的关卡可解
        var ropeNeed = new Dictionary<int, int>();   // color -> 该色气球需要的同色剪刀离场次数
        foreach (var pig in pigs)
        {
            var ropes = TypeOf(pig) == "Balloon" ? 3 : TypeOf(pig) == "BalloonLong" ? 5 : 0;
            if (ropes > 0)
            {
                var color = ColorOf(pig);
                if (color < 0) return "气球猪颜色未就绪（specialInfo 非法）——无法保证可解";
                ropeNeed[color] = Math.Max(ropeNeed.GetValueOrDefault(color), ropes);
            }
        }
        foreach (var (color, need) in ropeNeed)
        {
            var have = pigs.Count(p => TypeOf(p) == "Scissors" && ColorOf(p) == color);
            for (var i = have; i < need; i++)
            {
                if (!TryPlaceOne(pigs, "Scissors", width, height, random, color))
                {
                    return $"普通猪不足以为 {need} 根绳补足 {color} 色剪刀猪";
                }
            }
        }

        // ③ 总只数核对 + pigCount 同步（换型不变、鸭收缩不变、土豪猪 +1；剥离已在上限内核对）
        if (pigs.Count < req.Total)
        {
            return $"布点后只剩 {pigs.Count} 只，不足 {req.Total}";
        }
        level["pigCount"] = pigs.Count;
        return null;
    }

    // 大批量气球区域墙：顶部 rows 行清空 → 右缘放 3 只同色剪刀（贴墙，离场路径天生畅通）→
    // 其余格按行两两一组横排填充气球（朝右）。解绳次序：3 只剪刀先依次离场（每次该色全部气球
    // 各减 1 绳）→ 绳尽解锁 → 气球整排向右依次离场 → 区域外普通猪按原互锁解走位。
    private static string? PlaceBalloonWall(
        JsonNode level, JsonArray pigs, string balloonType, int n, int width, int height)
    {
        var ropePer = balloonType == "BalloonLong" ? 5 : 3;
        var rows = (int)Math.Ceiling((n * 2 + 6) / (double)width);   // 气球格 + 3 只剪刀位
        if (rows * width < n * 2 + 6 || rows >= height)
        {
            return $"地图高度不足以容纳 {n} 只{PiggyFarmPigTypes.DisplayName(balloonType)}气球墙";
        }

        // 清空区域：与顶部 rows 行相交的猪全部移除
        for (var i = pigs.Count - 1; i >= 0; i--)
        {
            if (CellsOf(pigs[i]).Any(c => c.Y < rows)) pigs.RemoveAt(i);
        }

        // 3 只同色剪刀放区域右缘（最后 3 行的右端 1x2），朝右贴墙 → 离场路径畅通
        for (var s = 0; s < 3; s++)
        {
            var y = rows - 1 - s;
            AppendPig(pigs, 1000 + s, "Right", "Scissors", "0", [(width - 2, y), (width - 1, y)]);
        }
        var scissorCells = new HashSet<(int X, int Y)>();
        for (var s = 0; s < 3; s++)
        {
            scissorCells.Add((width - 2, rows - 1 - s));
            scissorCells.Add((width - 1, rows - 1 - s));
        }

        // 气球按行两两一组填充剩余区域格（朝右），跳过剪刀占格
        var placed = 0;
        for (var y = 0; y < rows && placed < n; y++)
        {
            for (var x = 0; x + 1 < width && placed < n; x += 2)
            {
                if (scissorCells.Contains((x, y)) || scissorCells.Contains((x + 1, y))) continue;
                AppendPig(pigs, 1010 + placed, "Right", balloonType, "0", [(x, y), (x + 1, y)]);
                placed++;
            }
        }
        if (placed < n)
        {
            return $"区域容量不足：只能放置 {placed}/{n} 只{PiggyFarmPigTypes.DisplayName(balloonType)}";
        }

        level["pigCount"] = pigs.Count;
        return null;
    }

    // 布点顺序：大脚印优先（2x3 自由落位需要整块空地；1x3 需延伸格；1x2/1x1 随处可放）
    private static int PlacementOrder(string type) => type switch
    {
        "RichPig" => 0,
        "Long" or "BalloonLong" => 1,
        "Duck" => 3,
        _ => 2,   // Normal / Boom / Zombie / Spring / Scissors / Balloon 等 1x2 族
    };

    // 放置一只 type 猪成功返回 true；失败 false（调用方按重试/报错处理）
    // color：剪刀/气球/气球长猪的颜色索引（0黄/1绿/2蓝），写进 specialInfo 与游戏口径一致
    private static bool TryPlaceOne(JsonArray pigs, string type, int width, int height, Random random, int color = 0)
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
                // 兜底：合成时预留的 1×3 空段里直接放一只大长猪（猪数 +1）
                foreach (var (facing, w, h) in Shuffled([("Up", 1, 3), ("Right", 3, 1)], random))
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
                    AppendPig(pigs, maxId + 1, facing, "Long", "", spots[random.Next(spots.Count)]);
                    return true;
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
                    pig["specialInfo"] = "";
                    return true;
                }
                return false;
            }
            case "BalloonLong":
            {
                // 气球长猪 = 1x3 三格猪原地换型（5 绳系留；脚印保持）
                var threeCell = pigs.Where(p => CellCount(p) == 3 && TypeOf(p) != "BalloonLong").Shuffled(random);
                foreach (var pig in threeCell)
                {
                    pig["pigType"] = "BalloonLong";
                    pig["specialInfo"] = color.ToString();   // 绳色；绳数 5 为类型常量非关卡字段
                    return true;
                }
                return false;
            }
            default:
            {
                // 1x2 族（Normal/Boom/Zombie/Spring/Scissors/Balloon）：原地换型（脚印不变）
                // 气球/剪刀写入绳色/剪刀色；Boom 写计数 3
                var twoCell = pigs.Where(p => CellCount(p) == 2 && TypeOf(p) != type).Shuffled(random);
                foreach (var pig in twoCell)
                {
                    pig["pigType"] = type;
                    pig["specialInfo"] = type switch
                    {
                        "Boom" => "3",
                        "Scissors" or "Balloon" => color.ToString(),
                        _ => "",
                    };
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

    private static int ColorOf(JsonNode? pig)
    {
        var v = pig?["specialInfo"]?.GetValue<string>();
        return int.TryParse(v?.Trim(), out var n) && n is >= 0 and <= 2 ? n : -1;
    }

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

    // 最终导出规范化（对齐项目主线契约，2026-11-16 外部分析指南）：
    // ①pigs[i].id = 数组索引——generate.ps1 硬性要求；纯度剥离只更新 pigCount 会留 ID 缺口
    // ②pigCount = 最终地面数组长度
    // ③显式补齐缺省结构 terrainElements=[] 与 specialData（rabbit 全 0 + pigSkin None/0）
    // ④levelName 非空时按最终数量重写（剥离/删猪后曾与实际不符）；null = 保留现名
    private static void FinalizeExport(JsonNode node, string? levelName)
    {
        var pigs = node["pigs"]!.AsArray();
        for (var i = 0; i < pigs.Count; i++)
        {
            pigs[i]!["id"] = i;
        }
        node["pigCount"] = pigs.Count;
        if (node["terrainElements"] is null)
        {
            node["terrainElements"] = new JsonArray();
        }
        if (node["specialData"] is null)
        {
            node["specialData"] = new JsonObject
            {
                ["rabbit"] = new JsonObject
                {
                    ["firstAppearStep"] = 0,
                    ["subsequentAppearStep"] = 0,
                    ["rabbitCountPerWave"] = 0,
                },
                ["pigSkin"] = new JsonObject
                {
                    ["skinType"] = "None",
                    ["skinRatio"] = 0.0,
                },
            };
        }
        if (levelName is not null)
        {
            node["levelName"] = levelName;
        }
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
