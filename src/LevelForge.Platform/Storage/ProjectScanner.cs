using System.Text.Json;
using LevelForge.Platform.Database;

namespace LevelForge.Platform.Storage;

// 项目扫描结论：全部是机器可直接验证的事实（诚实语义——观察不冒充判断，四问结论仍由 AI/人给出）
public sealed record ProjectProfile(
    string ScannedAt,
    string ProjectRoot,
    string Engine,
    IReadOnlyList<LevelDirCandidate> LevelDirCandidates,
    IReadOnlyList<SampleFile> SampleFiles,
    IReadOnlyList<string> Observations);

// 关卡目录候选：按"同目录下相似命名的 JSON 数量"排序（levels/*.json、level_1.json、1.json 这类聚簇）
public sealed record LevelDirCandidate(
    string Directory,           // 相对项目根目录
    int JsonCount,              // 该目录直接含有的 JSON 文件数
    string NamingHint);         // 命名聚簇提示（numbered=纯数字、levelPrefixed=前缀+编号、mixed）

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
    private const int MaxDepth = 5;            // 相对深度上限（防止扫到引擎 SDK/库目录深处）
    private const int MaxJsonFiles = 2000;     // JSON 文件扫描上限（大项目防爆炸）
    private static readonly string[] SkipDirs = ["node_modules", "library", "temp", "packages", "settings", "build", "dist", ".git", "bin", "obj", "extensions"];

    public static ProjectProfile Scan(string projectRoot, string engine)
    {
        var jsons = CollectJsonFiles(projectRoot);
        var candidates = RankLevelDirs(jsons);
        var samples = SampleFiles(projectRoot, candidates, jsons);
        var observations = BuildObservations(engine, candidates, samples, jsons.Count);
        return new ProjectProfile(
            AppDatabase.UtcNowIso, projectRoot, engine, candidates, samples, observations);
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

    // 关卡目录候选：按目录聚簇 JSON，数量 ≥3 的目录按"编号命名相似度"打分排序
    private static List<LevelDirCandidate> RankLevelDirs(List<(string Rel, long Bytes)> jsons)
    {
        var byDir = jsons
            .GroupBy(j => Path.GetDirectoryName(j.Rel)?.Replace('\\', '/') ?? "")
            .Select(g => new { Dir = g.Key, Files = g.Select(x => Path.GetFileNameWithoutExtension(x.Rel)).ToList() })
            .Where(g => g.Files.Count >= 3)
            .OrderByDescending(g => g.Files.Count)
            .Take(5)
            .ToList();

        return byDir.Select(g => new LevelDirCandidate(
            g.Dir,
            g.Files.Count,
            ClassifyNaming(g.Files))).ToList();
    }

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
    private static List<string> BuildObservations(string engine, List<LevelDirCandidate> candidates, List<SampleFile> samples, int totalJson)
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
        return obs;
    }

    #endregion
}
