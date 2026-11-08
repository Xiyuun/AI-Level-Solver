namespace LevelForge.Platform.Gates;

// 门2驱动建议：从项目真实结构推断 Unity batchmode 命令（只探测事实，不启动引擎）
// 目的：把"手填命令+参数"变成"一键预填 + 逐条待办提示"，策划不需要记 Unity 命令行
public sealed record Gate2DriverSuggestion(
    bool Applicable,                 // 找到 harness 才为 true（没有 harness 的对拍是假对拍）
    string? HarnessPath,             // 相对项目根的 harness 路径
    string? UnityVersion,            // ProjectSettings/ProjectVersion.txt 的 m_EditorVersion
    string? UnityPath,               // 自动找到的 Unity.exe（找不到则 null，命令里用占位符）
    string Command,                  // 预填命令（Unity.exe 或 <UnityEditorPath> 占位符）
    IReadOnlyList<string> Args,      // 参数模板（含 {levelFile}/{tapeFile}/{outputFile}）
    int TimeoutSeconds,              // 建议整批超时
    IReadOnlyList<string> Notes);    // 事实与待办（缺 harness/缺 Unity 路径如实说明）

// 建议器：Unity 项目 → 驱动配置草稿
public static class Gate2DriverSuggester
{
    private const string DefaultHarness = "Assets/Editor/LevelForgeHarness/PairingHarness.cs";
    private const string HarnessMethod = "LevelForgeHarness.PairingHarness.Run";
    private const int OverallTimeoutSeconds = 900;   // 单关超时仍由驱动配置的 TimeoutSeconds 控制；这里是整批提示

    public static Gate2DriverSuggestion Suggest(string? projectRoot)
    {
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return new Gate2DriverSuggestion(false, null, null, null, "", [], OverallTimeoutSeconds,
                ["项目根目录不存在：该玩法不是导入项目或目录已移动，无法探测驱动"]);
        }

        // 1) harness：默认位置优先，其次在 Assets/Editor 下浅层搜索（限定深度，避免扫描整个工程）
        var harness = FindHarness(projectRoot);
        if (harness is not null)
        {
            notes.Add($"已找到对拍 harness：{harness}");
        }
        else
        {
            notes.Add($"未找到 {DefaultHarness}：请先按接入套件产出 harness（没有 harness 的对拍不可信）");
        }

        // 2) Unity 版本：ProjectVersion.txt 是机器事实
        var version = ReadUnityVersion(projectRoot);
        if (version is not null)
        {
            notes.Add($"Unity 版本（ProjectSettings/ProjectVersion.txt）：{version}");
        }
        else
        {
            notes.Add("未读到 ProjectSettings/ProjectVersion.txt，无法确定 Unity 版本");
        }

        // 3) Unity 编辑器：环境变量 / Hub 常见安装目录（只探测文件是否存在）
        var unity = FindUnityEditor(version);
        if (unity is not null)
        {
            notes.Add($"已找到 Unity 编辑器：{unity}");
        }
        else
        {
            notes.Add("未自动找到 Unity.exe：请把命令改为本机实际路径（Hub 默认 C:\\Program Files\\Unity\\Hub\\Editor\\<版本>\\Editor\\Unity.exe）");
        }

        var command = unity ?? "<UnityEditorPath>";
        var args = new List<string>
        {
            "-batchmode",
            "-nographics",
            "-projectPath", projectRoot,
            "-executeMethod", HarnessMethod,
            "--level", "{levelFile}",
            "--tape", "{tapeFile}",
            "--out", "{outputFile}",
            "-logFile", "-"
        };
        return new Gate2DriverSuggestion(
            Applicable: harness is not null,
            HarnessPath: harness,
            UnityVersion: version,
            UnityPath: unity,
            Command: command,
            Args: args,
            TimeoutSeconds: OverallTimeoutSeconds,
            Notes: notes);
    }

    #region 探测实现

    // harness 查找：默认位置 → Assets/Editor 下深度 4 的确定性搜索
    private static string? FindHarness(string projectRoot)
    {
        var preferred = Path.Combine(projectRoot, DefaultHarness.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(preferred))
        {
            return DefaultHarness;
        }
        var editorRoot = Path.Combine(projectRoot, "Assets", "Editor");
        if (!Directory.Exists(editorRoot))
        {
            return null;
        }
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((editorRoot, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (depth > 4) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "PairingHarness.cs"))
                {
                    return Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    stack.Push((sub, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 读不了的目录跳过：探测是尽力而为，不阻断向导
            }
        }
        return null;
    }

    // Unity 版本：ProjectVersion.txt 首行形如 "m_EditorVersion: 2022.3.62f1"
    private static string? ReadUnityVersion(string projectRoot)
    {
        var file = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(file)) return null;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("m_EditorVersion:", StringComparison.Ordinal)) continue;
                var value = trimmed["m_EditorVersion:".Length..].Trim();
                return value.Length > 0 ? value : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    // Unity 编辑器探测：显式环境变量优先；其次 Hub 常见安装目录（Windows）
    private static string? FindUnityEditor(string? version)
    {
        foreach (var key in new[] { "UNITY_EDITOR", "UNITY_PATH" })
        {
            var fromEnv = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            {
                return fromEnv;
            }
        }
        if (version is null) return null;
        var candidates = new[]
        {
            Path.Combine("C:\\Program Files\\Unity\\Hub\\Editor", version, "Editor", "Unity.exe"),
            Path.Combine("C:\\Program Files\\Unity\\Editor", "Unity.exe"),
            Path.Combine("D:\\Program Files\\Unity\\Hub\\Editor", version, "Editor", "Unity.exe"),
            Path.Combine("D:\\Unity\\Hub\\Editor", version, "Editor", "Unity.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    #endregion
}
