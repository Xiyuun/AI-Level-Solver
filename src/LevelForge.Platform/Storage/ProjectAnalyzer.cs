using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;
using LevelForge.Platform.Llm;

namespace LevelForge.Platform.Storage;

// 深度分析结论：全部字段为"初稿"语义——必须经用户审核/编辑后才算接入结论（诚实语义：AI 产出不冒充已确认事实）
public sealed record ProjectAnalysis(
    string AnalyzedAt,
    string Model,
    IReadOnlyList<QualificationAnswer> FourQuestions,
    IReadOnlyList<string> Mechanics,
    IReadOnlyList<ChecklistDraft> ChecklistDraft,
    IReadOnlyList<string> Risks,
    string Summary);

public sealed record QualificationAnswer(string Question, string Answer, string Evidence);

public sealed record ChecklistDraft(string Group, string Text);

// 深度分析失败（用户可操作）：未配置 LLM / 模型调用失败 / 输出不可解析
public sealed class ProjectAnalysisException(string message) : Exception(message);

// 项目深度分析器（三轮 N12 完整版 B，解冻记录见优化文档 §五 D8）：
// 平台内调用用户配置的模型，基于扫描事实 + 代码抽样 + 关卡抽样，产出
// 资格评估四问报告初稿 / 机制清单 / 审核清单初稿 / 风险点——全部供用户在向导里审核确认。
// 形态铁律遵守：无 agent——单次无状态调用，控制流由本类代码驱动（与生成流水线同形态）
public sealed class ProjectAnalyzer(LlmClient llm, SettingsStore settings, GameStore games)
{
    private const int MaxCodeFiles = 8;         // 代码抽样上限（个）
    private const int MaxCodeFileLines = 120;   // 单文件截断行数（控上下文）
    private const int MaxAnalysisTokens = 4096;

    public async Task<ProjectAnalysis> Analyze(string gameId, CancellationToken ct = default)
    {
        var config = LlmConfig.Resolve(settings);
        if (!config.IsConfigured)
        {
            throw new ProjectAnalysisException("深度分析需要模型：请先在设置页配置 LLM（地址/模型）");
        }
        var game = games.FindGame(gameId) ?? throw new ProjectAnalysisException($"未知玩法：{gameId}");
        var root = string.IsNullOrWhiteSpace(game.ProjectRoot)
            ? throw new ProjectAnalysisException("该玩法不是导入的项目（无项目根目录），无法分析")
            : game.ProjectRoot!;
        var engine = game.EngineType ?? "unknown";
        var profile = ProjectScanner.FromJson(game.ProjectProfileJson) ?? ProjectScanner.Scan(root, engine);

        var facts = BuildFacts(game.DisplayName, game.Id, root, engine, profile);
        var codeSamples = SampleCode(root, engine);
        var levelSample = SampleLevel(projectRoot: root, profile);

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt()),
            new("user", facts + "\n\n" + codeSamples + "\n\n" + levelSample),
        };

        var chat = await llm.ChatAsync(config, messages, temperature: 0.2, MaxAnalysisTokens, jsonMode: true, ct);
        if (!chat.Ok)
        {
            throw new ProjectAnalysisException($"模型调用失败：{chat.Error}");
        }
        var json = LlmJson.ExtractJson(chat.Content);
        if (json is null)
        {
            throw new ProjectAnalysisException("模型输出中未找到 JSON——深度分析结果不可用，请重试或换模型");
        }
        ProjectAnalysis? report;
        try
        {
            report = JsonSerializer.Deserialize<ProjectAnalysis>(json, JsonOpts());
        }
        catch (JsonException)
        {
            report = null;
        }
        if (report is null || report.FourQuestions.Count == 0)
        {
            throw new ProjectAnalysisException("模型输出不符合分析报告结构（缺 fourQuestions）——请重试或换模型");
        }
        report = report with { AnalyzedAt = AppDatabase.UtcNowIso, Model = config.Model };
        games.UpdateAnalysis(gameId, JsonSerializer.Serialize(report, JsonOpts()));
        return report;
    }

    #region 提示词与抽样

    private static string BuildSystemPrompt()
    {
        return """
            你是游戏接入评估员。基于给出的项目事实、代码抽样与关卡抽样，产出严格的 JSON 分析。
            只输出一个 JSON 对象，不要 markdown 代码块，不要解释文字。结构：
            {
              "fourQuestions": [
                {"question":"回合制（规则推进由离散操作驱动，无实时物理决定胜负）","answer":"yes|no|uncertain","evidence":"文件:行号 或 具体观察"},
                {"question":"确定性（同输入同结果，表现层随机不影响规则结论）","answer":"yes|no|uncertain","evidence":"…"},
                {"question":"状态可枚举（规则层状态可完整序列化与比对）","answer":"yes|no|uncertain","evidence":"…"},
                {"question":"持续产关（关卡数据外置、可持续新增关卡不改代码）","answer":"yes|no|uncertain","evidence":"…"}
              ],
              "mechanics": ["机制名：一句话规则描述（附证据来源）"],
              "checklistDraft": [{"group":"A|B|C|D","text":"该玩法特有的审核核对项（具体、可执行）"}],
              "risks": ["适配层制作的风险点（具体）"],
              "summary": "不超过三句话：接入难度与关键结论"
            }
            要求：evidence 必须具体到文件与观察；answer 是 uncertain 时必须说明缺什么信息；
            mechanics 从代码与关卡数据中归纳，不要臆造；checklistDraft 覆盖该玩法特有语义（至少 3 条）。
            """;
    }

    // 机器事实段：来自扫描画像（观察 + 全量 schema 摘要）；供深度分析与适配层生成共用（公开静态）
    public static string BuildFacts(string displayName, string gameId, string root, string engine, ProjectProfile profile)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 项目事实（平台扫描，机器可验证）");
        sb.AppendLine($"- 玩法：{displayName}（gameId={gameId}）");
        sb.AppendLine($"- 引擎：{engine}；根目录：{root}");
        sb.AppendLine($"- 扫描时间：{profile.ScannedAt}");
        foreach (var o in profile.Observations) sb.AppendLine($"- {o}");
        foreach (var s in profile.SchemaSummaries ?? [])
        {
            sb.AppendLine($"- [{s.Directory}/] 文件 {s.FileCount}、可解析 {s.ParsedCount}、共键 {{{s.CommonKeys}}}");
            if (s.NestedKeys.Length > 0) sb.AppendLine($"  - 数组元素结构：{s.NestedKeys}");
            if (s.EntityTypes.Length > 0) sb.AppendLine($"  - 判别字段取值：{s.EntityTypes}");
            if (s.NumericRanges.Length > 0) sb.AppendLine($"  - 数值范围：{s.NumericRanges}");
            foreach (var a in s.Anomalies) sb.AppendLine($"  - 异常：{a}");
        }
        return sb.ToString();
    }

    // 代码抽样：按引擎扩展名取前 N 个源码文件，各截断前 120 行（确定性采样，无 agent）；供深度分析与适配层生成共用
    public static string SampleCode(string root, string engine)
    {
        string[] exts = engine == "unity" ? ["*.cs"] : ["*.ts", "*.js"];
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules", "library", "temp", "build", "dist", ".git", "extensions", "settings" };
        var picked = new List<string>();
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((Path.Combine(root, engine == "unity" ? "Assets" : "assets"), 0));
        while (stack.Count > 0 && picked.Count < MaxCodeFiles)
        {
            var (dir, depth) = stack.Pop();
            if (depth > 4) continue;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (File.GetAttributes(entry).HasFlag(System.IO.FileAttributes.Directory))
                {
                    if (!skip.Contains(name)) stack.Push((entry, depth + 1));
                    continue;
                }
                if (exts.Any(pat => Fits(name, pat)) && !name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    picked.Add(entry);
                    if (picked.Count >= MaxCodeFiles) break;
                }
            }
        }
        if (picked.Count == 0)
        {
            return "# 代码抽样\n（未在引擎源码目录找到源码文件——接入评估需以关卡数据与用户确认为准）";
        }
        var sb = new StringBuilder("# 代码抽样（每文件截断前 120 行，确定性采样）\n");
        foreach (var file in picked)
        {
            sb.AppendLine($"## {Path.GetRelativePath(root, file).Replace('\\', '/')}");
            try
            {
                var lines = File.ReadAllLines(file).Take(MaxCodeFileLines);
                foreach (var line in lines) sb.AppendLine(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sb.AppendLine("（读取失败）");
            }
        }
        return sb.ToString();

        static bool Fits(string name, string pattern) =>
            pattern.EndsWith("*.*") ? true : name.EndsWith(pattern[1..], StringComparison.Ordinal);
    }

    // 关卡抽样：取画像里最小的一个可解析关卡文件原文（≤8KB），给模型真实数据形态
    private static string SampleLevel(string projectRoot, ProjectProfile profile)
    {
        var rel = profile.SampleFiles
            .Where(s => s.Parsed && s.Bytes <= 8192)
            .OrderBy(s => s.Bytes)
            .FirstOrDefault();
        if (rel is null)
        {
            var any = profile.SampleFiles.OrderBy(s => s.Bytes).FirstOrDefault();
            return any is null
                ? "# 关卡抽样\n（无抽样文件）"
                : $"# 关卡抽样\n{any.Path}：解析失败（{any.ParseError ?? "未知"}）";
        }
        var full = Path.Combine(projectRoot, rel.Path.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var text = File.ReadAllText(full);
            if (text.Length > 8000) text = text[..8000] + "\n…（截断）";
            return $"# 关卡抽样（{rel.Path}，{rel.Bytes}B，原文）\n{text}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"# 关卡抽样\n{rel.Path}：读取失败";
        }
    }

    private static JsonSerializerOptions JsonOpts()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        return o;
    }

    #endregion
}
