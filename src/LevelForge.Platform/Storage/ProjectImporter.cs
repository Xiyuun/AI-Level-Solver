using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;

namespace LevelForge.Platform.Storage;

// 导入项目结论：gameId 由目录名 slug 化而来；AlreadyImported=true 表示同目录重复导入（幂等返回）
public sealed record ProjectImportOutcome(
    string GameId, string DisplayName, string EngineType, string ProjectRoot, bool AlreadyImported);

// 导入失败（用户可操作的错误）：目录不存在 / 无法识别引擎 / 目录名撞名
public sealed class ProjectImportException(string message) : Exception(message);

// 项目导入（三轮计划 N10，用户期望流程第②步）：
// 选取设备上某一目录 → 识别为 Cocos/Unity 项目 → 目录名即项目名 → 落库为"接入中"的玩法 + 六步向导任务。
// 本服务只做登记，不做适配层——适配层由接入向导指引外部 AI 产出后登记进 GameRegistry（冻结决策：换玩法=改注册表+重新构建）
public sealed class ProjectImporter(GameStore games, AppDatabase db, SettingsStore settings)
{
    private const string RecentKey = "ui.recentProjects";   // 最近导入目录（JSON 字符串数组，供前端快捷回填）
    private const int RecentLimit = 5;

    // 项目根目录 → 玩法。目录不存在/引擎不识别/撞名抛 ProjectImportException
    public ProjectImportOutcome Import(string directory)
    {
        var root = NormalizeRoot(directory);
        if (!Directory.Exists(root))
        {
            throw new ProjectImportException($"目录不存在：{root}");
        }

        var (engine, evidence) = DetectEngine(root);
        if (engine is null)
        {
            throw new ProjectImportException(
                $"无法识别为 Cocos 或 Unity 项目（依据：{evidence}）。" +
                "Unity 项目应包含 Assets/ 与 ProjectSettings/，Cocos Creator 项目应包含 creator 版本声明的 package.json");
        }

        var displayName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var gameId = Slugify(displayName);
        if (games.FindGame(gameId) is { } existing)
        {
            if (existing.ProjectRoot == root)
            {
                // 同目录重复导入：幂等返回（不重复种任务、不覆盖状态）
                TouchRecent(root);
                return new ProjectImportOutcome(gameId, existing.DisplayName, engine, root, AlreadyImported: true);
            }
            throw new ProjectImportException(
                $"目录名「{displayName}」与已导入项目「{existing.Id}」（{existing.DisplayName}）撞名：请重命名项目目录后重新导入");
        }

        // 项目扫描（三轮 N12 完整版）：关卡目录候选 + 抽样解析 + 机器观察——向导全部内容的数据源
        var profile = ProjectScanner.Scan(root, engine);
        if (!games.InsertImportedGame(gameId, displayName, engine, root, ProjectScanner.ToJson(profile)))
        {
            throw new ProjectImportException($"导入落库失败：{gameId}");
        }
        GameSeeder.SeedOnboardingTasks(db, gameId, production: false);
        TouchRecent(root);
        return new ProjectImportOutcome(gameId, displayName, engine, root, AlreadyImported: false);
    }

    // 重新扫描：项目内容可能已变化（关卡目录移动/新增）——用现有 games 行的根目录与引擎重跑扫描并回写画像
    public ProjectProfile Rescan(string gameId)
    {
        var game = games.FindGame(gameId) ?? throw new ProjectImportException($"未知玩法：{gameId}");
        if (string.IsNullOrWhiteSpace(game.ProjectRoot))
        {
            throw new ProjectImportException("该玩法不是导入的项目（无项目根目录），无法扫描");
        }
        var profile = ProjectScanner.Scan(game.ProjectRoot, game.EngineType ?? "unknown");
        games.UpdateProfile(gameId, ProjectScanner.ToJson(profile));
        return profile;
    }

    // 最近导入目录（前端快捷回填）
    public IReadOnlyList<string> RecentDirectories()
    {
        var json = settings.Get<string>(RecentKey, "[]") ?? "[]";
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // 数据目录（删除项目时清理磁带文件用）
    public string TapeDirectory() => db.DataDir;

    #region 引擎识别

    // 识别结果：unity / cocos-creator3 / cocos-creator2；识别不了返回 null，依据里带目录内容帮助用户对照
    public static (string? Engine, string Evidence) DetectEngine(string root)
    {
        var unityManifest = Path.Combine(root, "ProjectSettings", "ProjectVersion.txt");
        if (Directory.Exists(Path.Combine(root, "Assets")) && Directory.Exists(Path.Combine(root, "ProjectSettings")))
        {
            return ("unity", $"存在 Assets/ 与 ProjectSettings/{(File.Exists(unityManifest) ? "ProjectVersion.txt" : string.Empty)}");
        }
        var packageJson = Path.Combine(root, "package.json");
        if (File.Exists(packageJson) && JsonHasCreatorField(packageJson))
        {
            return ("cocos-creator3", "package.json 含 creator 字段（Cocos Creator 3.x）");
        }
        var projectJson = Path.Combine(root, "project.json");
        if (File.Exists(projectJson) && JsonHasKey(projectJson, "engine-version"))
        {
            return ("cocos-creator2", "project.json 含 engine-version 字段（Cocos Creator 2.x）");
        }
        var entries = string.Join("、", TopLevelEntries(root));
        return (null, $"未找到 Unity/Cocos 项目特征文件；目录内容：{(entries.Length == 0 ? "空目录" : entries)}");
    }

    // Cocos Creator 3.x：package.json 顶层 "creator" 字段（字符串版本号或对象）
    private static bool JsonHasCreatorField(string path)
    {
        return JsonHasKey(path, "creator");
    }

    private static bool JsonHasKey(string path, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out _);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // 顶层条目（错误信息用，帮助用户对照特征）
    private static List<string> TopLevelEntries(string root)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(root)
                .Select(Path.GetFileName)
                .Where(n => n is not null)
                .Select(n => n!)
                .OrderBy(n => n)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    #endregion

    #region 目录与命名

    // 规范化：去引号与首尾空白/分隔符（用户从资源管理器复制路径常带引号）
    public static string NormalizeRoot(string directory)
    {
        var trimmed = directory.Trim().Trim('"').Trim();
        return trimmed.Length > 1 ? trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : trimmed;
    }

    // 目录名 → gameId：小写 ASCII 字母数字保留，其余折叠为 '-'（中文目录名折叠为空时回退 project-<时间戳>）
    public static string Slugify(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '-');
        }
        var slug = sb.ToString().Trim('-');
        var collapsed = new StringBuilder();
        foreach (var ch in slug)
        {
            if (ch != '-' || collapsed.Length == 0 || collapsed[^1] != '-')
            {
                collapsed.Append(ch);
            }
        }
        slug = collapsed.ToString();
        return slug.Length == 0 ? $"project-{DateTime.UtcNow:yyyyMMddHHmmss}" : slug;
    }

    #endregion

    // 最近目录记忆：去重后取前 N 个
    private void TouchRecent(string root)
    {
        var list = RecentDirectories().Where(d => d != root).ToList();
        list.Insert(0, root);
        settings.Set(RecentKey, list.Take(RecentLimit).ToList());
    }
}
