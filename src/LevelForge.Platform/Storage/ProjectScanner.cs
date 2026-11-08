using System.Text.Json;
using LevelForge.Platform.Database;

namespace LevelForge.Platform.Storage;

// 项目扫描结论：全部是机器可直接验证的事实（诚实语义——观察不冒充判断，四问结论仍由 AI/人给出）
// SchemaSummaries（三轮 N12 完整版）：候选目录的全量 schema 分析（共键/嵌套结构/实体类型/数值范围/异常）
public sealed record ProjectProfile(
    string ScannedAt,
    string ProjectRoot,
    string Engine,
    IReadOnlyList<LevelDirCandidate> LevelDirCandidates,
    IReadOnlyList<SampleFile> SampleFiles,
    IReadOnlyList<string> Observations,
    IReadOnlyList<SchemaSummary>? SchemaSummaries = null,
    IReadOnlyList<CodeDirCandidate>? CodeDirCandidates = null);

// 一个候选目录的全量 schema 事实：对目录内全部（≤100 个）可解析 JSON 做聚合
public sealed record SchemaSummary(
    string Directory,
    int FileCount,
    int ParsedCount,
    string CommonKeys,          // ≥80% 文件都有的顶层键（逗号串）
    string NestedKeys,          // 数组元素的嵌套键（如 chesses[]: id,x,y,type）
    string EntityTypes,         // 带判别字段的取值清单（如 type: normal,boom,lock）
    string NumericRanges,       // 主要数值字段的 min~max（逗号串）
    IReadOnlyList<string> Anomalies);   // 解析失败/缺键等异常事实

// 关卡目录候选：按"同目录下相似命名的 JSON 数量"排序（levels/*.json、level_1.json、1.json 这类聚簇）
public sealed record LevelDirCandidate(
    string Directory,           // 相对项目根目录
    int JsonCount,              // 该目录直接含有的 JSON 文件数
    string NamingHint);         // 命名聚簇提示（numbered=纯数字、levelPrefixed=前缀+编号、mixed）

// 玩法代码目录候选：按"子树内源码文件数量"排序（玩法代码是层级结构，按子树计数而非单层）
// 只提名事实（目录+源码文件数），语义判断（哪里是玩法代码）由用户在向导①确认
public sealed record CodeDirCandidate(
    string Directory,           // 相对项目根目录
    int SourceFileCount);       // 子树内源码文件数（.cs / .ts / .js，已跳过插件目录）

// 抽样关卡文件：只记录机器事实（顶层键/大小/是否可解析），不猜测语义
public sealed record SampleFile(
    string Path,                // 相对项目根目录
    long Bytes,
    bool Parsed,
    string? TopLevelKeys,       // 顶层键逗号串（解析成功才有）
    string? ParseError);

// 项目扫描器（三轮 N12 完整版）：真实读取项目目录，产出结构化画像。
// 用途：①登记预填关卡根目录 ②资格评估的机器事实区 ③需求包内嵌样例原文 ④按项目生成审核清单初稿
public static class ProjectScanner
{
    private const int MaxDepth = 8;            // JSON 扫描相对深度上限（LevelEditor/Luban~/Datas/Levels 这类编辑源在 6 层，5 层会整目录漏掉）
    private const int MaxSourceDepth = 12;     // 源码收集深度上限（玩法代码树嵌套深，如 Assets/Main/Game/Modules/X/Model/…；插件等噪声目录已在 SkipDirs 排除）
    private const int MaxJsonFiles = 2000;     // JSON/源码文件扫描上限（大项目防爆炸）
    private static readonly string[] SkipDirs = ["node_modules", "library", "temp", "packages", "settings", "build", "dist", ".git", ".vscode", ".vs", ".idea", "bin", "obj", "extensions",
        "plugins", "plugin", "thirdparty", "third_party"];   // 插件/三方库/IDE 配置：玩法代码候选与代码抽样一律忽略

    public static ProjectProfile Scan(string projectRoot, string engine)
    {
        var jsons = CollectJsonFiles(projectRoot);
        var candidates = RankLevelDirs(projectRoot, jsons);
        var samples = SampleFiles(projectRoot, candidates, jsons);
        var schemas = SchemaSummaries(projectRoot, candidates);
        var codeCandidates = RankCodeDirs(projectRoot, engine);
        var observations = BuildObservations(engine, candidates, samples, jsons.Count, schemas);
        return new ProjectProfile(
            AppDatabase.UtcNowIso, projectRoot, engine, candidates, samples, observations, schemas, codeCandidates);
    }

    // 序列化/反序列化（games.project_profile_json 存取口径）
    public static string ToJson(ProjectProfile profile) =>
        JsonSerializer.Serialize(profile, Options());

    public static ProjectProfile? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProjectProfile>(json, Options());
        }
        catch (JsonException)
        {
            return null;   // 画像损坏照实返回 null，由界面引导重扫
        }
    }

    private static JsonSerializerOptions Options()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        return o;
    }

    #region 扫描实现

    // 收集项目内全部 JSON（相对路径 + 大小），跳过引擎/构建目录，限深限量
    private static List<(string Rel, long Bytes)> CollectJsonFiles(string root)
    {
        var result = new List<(string, long)>();
        var stack = new Stack<(string Dir, int Depth)>((new[] { (root, 0) }));
        while (stack.Count > 0 && result.Count < MaxJsonFiles)
        {
            var (dir, depth) = stack.Pop();
            if (depth > MaxDepth) continue;
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;   // 读不了的目录照实跳过
            }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (File.GetAttributes(entry).HasFlag(System.IO.FileAttributes.Directory))
                {
                    if (!SkipDirs.Contains(name.ToLowerInvariant()))
                    {
                        stack.Push((entry, depth + 1));
                    }
                    continue;
                }
                if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        result.Add((Path.GetRelativePath(root, entry).Replace('\\', '/'), new FileInfo(entry).Length));
                    }
                    catch (IOException) { }
                    if (result.Count >= MaxJsonFiles) break;
                }
            }
        }
        return result;
    }

    // 关卡目录候选：先按目录聚簇 JSON（数量 ≥3），再按"抽样文件是否含关卡字段"优先排序
    // 动机：Unity 工程里 Tools/Luban 等目录也有一堆 JSON（runtimeconfig/tsconfig），只按数量排序
    // 会把工具配置排在真关卡前面（piggyfarm 实测：25 个工具配置压过 499 个关卡）
    private static List<LevelDirCandidate> RankLevelDirs(string root, List<(string Rel, long Bytes)> jsons)
    {
        var directories = jsons
            .GroupBy(j => Path.GetDirectoryName(j.Rel)?.Replace('\\', '/') ?? "")
            .Where(g => g.Count() >= 3)
            .Select(g => new
            {
                Dir = g.Key,
                Files = g.Select(x => Path.GetFileNameWithoutExtension(x.Rel)).ToList(),
                Samples = g.OrderBy(x => x.Bytes).Take(3).ToList()
            })
            .Select(g => new
            {
                g.Dir,
                Count = g.Files.Count,
                LevelLike = ScoreLevelLike(root, g.Samples),
                Numbered = g.Files.Count(n => long.TryParse(n, out _)) / (double)g.Files.Count,
                Naming = ClassifyNaming(g.Files)
            })
            .OrderByDescending(g => g.LevelLike)
            .ThenByDescending(g => g.Numbered)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Dir, StringComparer.Ordinal)   // 同分按路径字典序（确定性）
            .Take(5)
            .ToList();

        return directories.Select(g => new LevelDirCandidate(g.Dir, g.Count, g.Naming)).ToList();
    }

    // 关卡相似度：抽样最多 3 个小文件，统计"含关卡字段"的比例（0~1）
    // 只用于候选排序，不冒充语义判断——真正选哪个仍由用户在向导①确认
    private static double ScoreLevelLike(string root, List<(string Rel, long Bytes)> samples)
    {
        var checkedCount = 0;
        var levelLike = 0;
        foreach (var sample in samples)
        {
            var full = Path.Combine(root, sample.Rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(full));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                checkedCount++;
                var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (LevelJsonKeys.Any(keys.Contains)) levelLike++;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 读不了/坏 JSON 不计入分母：不让单个坏文件把一个真关卡目录压到 0
            }
        }
        return checkedCount == 0 ? 0 : (double)levelLike / checkedCount;
    }

    // 关卡 JSON 的常见顶层字段（宁严勿宽：通用 id/name/type 不算证据，避免把配置 JSON 误判成关卡）
    private static readonly string[] LevelJsonKeys =
        ["levelId", "level", "levelNumber", "mapWidth", "mapHeight", "pigs", "grid", "tiles", "cells", "chesses", "board"];

    // 命名聚簇：纯数字（1,2,3…）/ 统一前缀+数字（level_1, level_2…）/ 其他
    private static string ClassifyNaming(List<string> names)
    {
        if (names.All(n => long.TryParse(n, out _))) return "numbered";
        var prefixed = names.Count(n => !long.TryParse(n, out _)
            && n.Length > 1
            && long.TryParse(n.TrimStart().Substring(n.IndexOfAny(['-', '_', ' ']) is var idx && idx > 0 ? idx + 1 : n.Length), out _));
        if (prefixed >= names.Count * 0.8) return "levelPrefixed";
        return "mixed";
    }

    // 玩法代码目录候选（确定性，无语义判断）：按引擎扩展名收集源码文件（跳过插件/三方库），
    // 对深度 2~3 的目录统计"子树内源码文件数"，数量 ≥5 的目录按数量排序取前 5。
    // 提名只是事实——哪一个是玩法代码由用户确认（诚实语义：工具不替用户猜规则边界）
    internal static List<CodeDirCandidate> RankCodeDirs(string root, string engine)
    {
        var exts = engine == "unity" ? new[] { ".cs" } : new[] { ".ts", ".js" };
        var sourceRoot = SourceRoot(root, engine);
        var sourceFiles = CollectSourceFiles(sourceRoot, exts);
        if (sourceFiles.Count == 0) return [];

        // 候选目录统一用相对项目根的路径（与关卡目录候选同口径，前端直接展示）
        var sourceRootRel = Path.GetRelativePath(root, sourceRoot).Replace('\\', '/');
        var sourcePrefix = sourceRootRel == "." ? "" : sourceRootRel + "/";

        // 候选 = 源码文件的全部祖先目录（深度窗口内）——纯容器目录（文件全在子目录）也是合法玩法根，
        // 只统计"直接含文件的目录"会把层级结构的玩法根漏掉（piggyfarm 的 Assets/Main/Game 实测隐身）
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in sourceFiles)
        {
            var d = Path.GetDirectoryName(f.Rel)?.Replace('\\', '/') ?? "";
            while (true)
            {
                ancestors.Add(d);
                if (d.Length == 0) break;
                var idx = d.LastIndexOf('/');
                d = idx > 0 ? d[..idx] : "";
            }
        }
        var relDepth = RelativeDepth(sourceRoot, root);
        var result = new List<CodeDirCandidate>();
        foreach (var dir in ancestors)
        {
            var depth = dir.Length == 0 ? 0 : dir.Count(c => c == '/') + 1;
            var absoluteDepth = relDepth + depth;   // 相对项目根的真实层级
            if (absoluteDepth < 2 || absoluteDepth > 4) continue;   // 太浅=引擎根/Assets 本身，太深=碎片目录
            var prefix = dir.Length == 0 ? "" : dir + "/";
            var count = sourceFiles.Count(f =>
            {
                var d = Path.GetDirectoryName(f.Rel)?.Replace('\\', '/') ?? "";
                return d == dir || d.StartsWith(prefix, StringComparison.Ordinal);
            });
            if (count >= 5)
            {
                result.Add(new CodeDirCandidate(sourcePrefix + dir, count));
            }
        }
        return result
            .OrderByDescending(c => c.SourceFileCount)
            .ThenBy(c => c.Directory, StringComparer.Ordinal)   // 同数按路径字典序（确定性）
            .Take(5)
            .ToList();
    }

    // 引擎源码根：unity=Assets，cocos=assets；不存在时退回项目根本身（如实降级）
    internal static string SourceRoot(string root, string engine)
    {
        var name = engine == "unity" ? "Assets" : "assets";
        var dir = Path.Combine(root, name);
        return Directory.Exists(dir) ? dir : root;
    }

    // 源码文件收集（相对 sourceRoot 的路径 + 大小）：跳过 SkipDirs（含 Plugins），限深限量
    internal static List<(string Rel, long Bytes)> CollectSourceFiles(string sourceRoot, string[] exts)
    {
        var result = new List<(string, long)>();
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((sourceRoot, 0));
        while (stack.Count > 0 && result.Count < MaxJsonFiles)
        {
            var (dir, depth) = stack.Pop();
            if (depth > MaxSourceDepth) continue;
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (File.GetAttributes(entry).HasFlag(System.IO.FileAttributes.Directory))
                {
                    if (!SkipDirs.Contains(name.ToLowerInvariant()))
                    {
                        stack.Push((entry, depth + 1));
                    }
                    continue;
                }
                if (exts.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    && !name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        result.Add((Path.GetRelativePath(sourceRoot, entry).Replace('\\', '/'), new FileInfo(entry).Length));
                    }
                    catch (IOException) { }
                    if (result.Count >= MaxJsonFiles) break;
                }
            }
        }
        return result;
    }

    // sourceRoot 相对项目根的层级（Assets=1，项目根本身=0）
    private static int RelativeDepth(string sourceRoot, string projectRoot)
    {
        var rel = Path.GetRelativePath(projectRoot, sourceRoot);
        if (rel is "." or "") return 0;
        return rel.Count(c => c is '/' or '\\') + 1;
    }

    // 抽样：每个候选目录取最小的 2 个 JSON 读出顶层键（小文件更可能是关卡而不是配置）
    private static List<SampleFile> SampleFiles(string root, List<LevelDirCandidate> candidates, List<(string Rel, long Bytes)> jsons)
    {
        var samples = new List<SampleFile>();
        foreach (var candidate in candidates.Take(3))
        {
            var inDir = jsons
                .Where(j => (Path.GetDirectoryName(j.Rel)?.Replace('\\', '/') ?? "") == candidate.Directory)
                .OrderBy(j => j.Bytes)
                .Take(2);
            foreach (var j in inDir)
            {
                var full = Path.Combine(root, j.Rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(full));
                    var keys = string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name).Take(12));
                    samples.Add(new SampleFile(j.Rel, j.Bytes, Parsed: true, keys, null));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    samples.Add(new SampleFile(j.Rel, j.Bytes, Parsed: false, null, ex.Message));
                }
                if (samples.Count >= 6) return samples;
            }
        }
        // 没有任何候选目录时：给全局最小的 2 个 JSON 作为最低限度事实
        if (samples.Count == 0)
        {
            foreach (var j in jsons.OrderBy(j => j.Bytes).Take(2))
            {
                var full = Path.Combine(root, j.Rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(full));
                    samples.Add(new SampleFile(j.Rel, j.Bytes, true,
                        string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name).Take(12)), null));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    samples.Add(new SampleFile(j.Rel, j.Bytes, false, null, ex.Message));
                }
            }
        }
        return samples;
    }

    // 机器观察：只陈述事实，不下语义结论（诚实语义）
    private static List<string> BuildObservations(string engine, List<LevelDirCandidate> candidates,
        List<SampleFile> samples, int totalJson, List<SchemaSummary> schemas)
    {
        var obs = new List<string>
        {
            $"引擎识别：{engine}；项目内共扫描到 {totalJson} 个 JSON 文件",
        };
        if (candidates.Count > 0)
        {
            var top = candidates[0];
            obs.Add($"最像关卡目录的是 {top.Directory}/（{top.JsonCount} 个 JSON，命名聚簇 {top.NamingHint}）");
        }
        else
        {
            obs.Add("未发现 JSON 数量 ≥3 的聚簇目录——关卡可能不是 JSON 存储（表格/自定义格式/硬编码场景），需要人工确认关卡数据形态");
        }
        var parsed = samples.Where(s => s.Parsed).ToList();
        if (parsed.Count > 0)
        {
            var keys = parsed.Select(s => s.TopLevelKeys).Distinct().ToList();
            obs.Add($"抽样 {parsed.Count} 个可解析 JSON，顶层键形态：{string.Join(" / ", keys.Select(k => "{" + k + "}"))}");
        }
        var failed = samples.Where(s => !s.Parsed).ToList();
        if (failed.Count > 0)
        {
            obs.Add($"有 {failed.Count} 个抽样文件解析失败（{string.Join("；", failed.Select(f => f.Path))}）");
        }
        // 全量 schema 摘要（N12 完整版：顶层候选的聚合事实；超过 100 个文件时只抽样解析前 100 个，如实标注）
        var topSchema = schemas.FirstOrDefault(s => s.ParsedCount > 0);
        if (topSchema is not null)
        {
            var sampled = Math.Min(topSchema.FileCount, 100);
            obs.Add($"[{topSchema.Directory}/] 共 {topSchema.FileCount} 个 JSON（抽样 {sampled} 个）中 {topSchema.ParsedCount} 个可解析；"
                + $"共键：{{{topSchema.CommonKeys}}}"
                + (topSchema.NestedKeys.Length > 0 ? $"；嵌套：{topSchema.NestedKeys}" : "")
                + (topSchema.EntityTypes.Length > 0 ? $"；判别值：{topSchema.EntityTypes}" : "")
                + (topSchema.NumericRanges.Length > 0 ? $"；数值范围：{topSchema.NumericRanges}" : "")
                + (topSchema.Anomalies.Count > 0 ? $"；异常 {topSchema.Anomalies.Count} 条：{string.Join("；", topSchema.Anomalies.Take(3))}" : ""));
        }
        return obs;
    }

    #region 全量 schema 聚合（N12 完整版 A：深度确定性扫描）

    // 对前两个候选目录做全量聚合（每目录最多解析 100 个文件，超出部分只计入总数、不解析）
    // 事实口径：FileCount=目录内 JSON 总数，ParsedCount=抽样解析数——不能把"抽样 100"说成"全量 100"
    private static List<SchemaSummary> SchemaSummaries(string root, List<LevelDirCandidate> candidates)
    {
        var result = new List<SchemaSummary>();
        foreach (var candidate in candidates.Take(2))
        {
            var dirFull = Path.Combine(root, candidate.Directory.Replace('/', Path.DirectorySeparatorChar));
            List<string> allFiles;
            try
            {
                allFiles = Directory.EnumerateFiles(dirFull, "*.json").ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            var files = allFiles.Take(100).ToList();
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var nested = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var discriminator = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var numeric = new Dictionary<string, (double Min, double Max, int Seen)>(StringComparer.Ordinal);
            var anomalies = new List<string>();
            var parsedCount = 0;
            foreach (var file in files)
            {
                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    anomalies.Add($"{Path.GetFileName(file)}: 解析失败（{ex.Message}）");
                    continue;
                }
                using (doc)
                {
                    parsedCount++;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        anomalies.Add($"{Path.GetFileName(file)}: 顶层不是对象");
                        continue;
                    }
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        keyCount[prop.Name] = keyCount.GetValueOrDefault(prop.Name) + 1;
                        switch (prop.Value.ValueKind)
                        {
                            case JsonValueKind.Number when prop.Value.TryGetDouble(out var d):
                                (double Min, double Max, int Seen) cur = numeric.GetValueOrDefault(prop.Name, (d, d, 0));
                                numeric[prop.Name] = (Math.Min(cur.Min, d), Math.Max(cur.Max, d), cur.Seen + 1);
                                break;
                            case JsonValueKind.Array when prop.Value.GetArrayLength() > 0:
                                foreach (var el in prop.Value.EnumerateArray())
                                {
                                    if (el.ValueKind != JsonValueKind.Object) continue;
                                    foreach (var inner in el.EnumerateObject())
                                    {
                                        var key = $"{prop.Name}[]:{inner.Name}";
                                        nested.TryAdd(key, new HashSet<string>(StringComparer.Ordinal));
                                        if (inner.Value.ValueKind is JsonValueKind.String)
                                        {
                                            nested[key].Add(inner.Value.GetString() ?? "");
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            // 共键：出现在 ≥80% 可解析文件中
            var threshold = Math.Max(1, (int)Math.Ceiling(parsedCount * 0.8));
            var commonKeys = string.Join(",", keyCount.Where(kv => kv.Value >= threshold)
                .OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
            // 嵌套键：数组元素结构（跳过纯自由文本的值集合）
            var nestedKeys = string.Join(";", nested
                .Where(kv => kv.Key.EndsWith(":type", StringComparison.Ordinal) is false)
                .Select(kv => kv.Key).Take(10));
            // 判别字段取值：xxx[]:type 的全部取值（实体类型清单）
            var entityTypes = string.Join(",", nested
                .Where(kv => kv.Key.EndsWith(":type", StringComparison.Ordinal))
                .SelectMany(kv => kv.Value).Distinct().Take(20));
            // 数值范围：至少出现在 3 个文件中的数值字段
            var numericRanges = string.Join(",", numeric
                .Where(kv => kv.Value.Seen >= 3 && kv.Value.Max > kv.Value.Min)
                .OrderBy(kv => kv.Key).Take(10)
                .Select(kv => $"{kv.Key}:{FormatNum(kv.Value.Min)}~{FormatNum(kv.Value.Max)}"));
            result.Add(new SchemaSummary(candidate.Directory, allFiles.Count, parsedCount,
                commonKeys, nestedKeys, entityTypes, numericRanges, anomalies.Take(10).ToList()));
        }
        return result;
    }

    private static string FormatNum(double d) => d == Math.Truncate(d) ? ((long)d).ToString() : d.ToString("0.##");

    #endregion

    #endregion
}
