using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;
using LevelForge.Platform.Llm;

namespace LevelForge.Platform.Storage;

// 深度分析结论：全部字段为"初稿"语义——必须经用户审核/编辑后才算接入结论（诚实语义：AI 产出不冒充已确认事实）
// HygieneGrade/WorkEstimate：与 onboarding-skill/qualification.md 报告规格对齐（良好/一般/纠缠 + 人日预估）；旧存档缺省为空
public sealed record ProjectAnalysis(
    string AnalyzedAt,
    string Model,
    IReadOnlyList<QualificationAnswer> FourQuestions,
    IReadOnlyList<string> Mechanics,
    IReadOnlyList<ChecklistDraft> ChecklistDraft,
    IReadOnlyList<string> Risks,
    string Summary,
    string HygieneGrade = "",
    string WorkEstimate = "");

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
    private const int MaxInventoryFiles = 500;      // 索引层文件数上限（防异常大工程撑爆 prompt）
    private const int MaxFulltextFileChars = 300_000;   // 单文件正文字符上限（超过疑似生成物/序列化数据，跳过并标注）
    private const int MaxAnalysisTokens = 16384;    // 分析输出预算：玩法代码全文入 prompt 后推理模型思考大幅变长（D9 实测思考可达 38K 字符），4096 会被思考耗尽导致正文截断
    private const int DefaultAnalysisBudgetChars = 200_000;   // 分析正文预算默认值：1M 窗口模型下可覆盖大部分核心玩法文件（48K 时代只装得下约 10 个文件，模型被迫"未读先猜"）
    private const int DefaultGenBudgetChars = 300_000;        // 生成正文预算默认值：求解器语义来源，完整性要求最高

    // 玩法代码正文预算（可配置）：settings 缺失/非法回退默认值，钳制到合理区间防呆
    private int AnalysisBudgetChars() => ClampBudget(settings.Get("analyze.codeBudgetChars", DefaultAnalysisBudgetChars));
    private int GenBudgetChars() => ClampBudget(settings.Get("gen.codeBudgetChars", DefaultGenBudgetChars));
    private static int ClampBudget(int v) => Math.Clamp(v <= 0 ? 0 : v, 20_000, 2_000_000);

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
        var codeSamples = SampleCode(root, engine, game.CodeRoot, AnalysisBudgetChars());
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
              "checklistDraft": [{"group":"A|B|C|D","text":"可签字的断言：写清『谁与谁一致/什么成立 + 预期 + 依据』；不要写『验证…/检查…』这类任务"}],
              "risks": ["适配层制作的风险点（具体）"],
              "hygieneGrade": "良好|一般|纠缠——判据：规则代码与 MonoBehaviour/场景/引擎服务的耦合程度；纠缠=规则状态分散在引擎对象中难以提取序列化",
              "workEstimate": "接入工作量预估（人日区间 + 一句话依据；纠缠时上浮 1~3 人日并明示原因）",
              "summary": "不超过三句话：接入难度与关键结论"
            }
            要求：evidence 必须具体到文件与观察；answer 是 uncertain 时必须说明缺什么信息；
            mechanics 从代码与关卡数据中归纳，不要臆造；checklistDraft 覆盖该玩法特有语义（至少 3 条），
            每条必须是**可确认的断言**（主语 + 规则 + 预期 + 依据），不要写成“验证/检查”任务；确实无法确认的语义写进 risks；
            hygieneGrade 必须三选一并给出耦合证据，workEstimate 给出区间与依据。
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

    // 代码抽样（四轮重写）：插件/三方库一律忽略——求解器质量取决于玩法规则代码的完整性，8 文件×120 行
    // 的任意抽样对插件重的工程会系统性漏掉玩法代码。两层结构：
    //   索引层——非插件全部源码文件的 路径·行数·类型/公共成员签名（任何规则文件都不可能隐身）
    //   正文层——玩法代码根目录（向导①用户确认的 codeRoot；未确认时自动提名最可能目录并如实标注）
    //           下的文件全文收录（不按行截断），预算封顶、超出如实说明
    public static string SampleCode(string root, string engine) => SampleCode(root, engine, codeRoot: null);

    public static string SampleCode(string root, string engine, string? codeRoot, int maxChars = 200_000)
    {
        var exts = engine == "unity" ? (string[])[".cs"] : [".ts", ".js"];
        var sourceRoot = ProjectScanner.SourceRoot(root, engine);
        var sourceRel = Path.GetRelativePath(root, sourceRoot).Replace('\\', '/');
        var sourcePrefix = sourceRel == "." ? "" : sourceRel + "/";
        var files = ProjectScanner.CollectSourceFiles(sourceRoot, exts);   // 已跳过插件/三方库目录
        if (files.Count == 0)
        {
            return "# 代码抽样\n（未在引擎源码目录找到源码文件——接入评估需以关卡数据与用户确认为准）";
        }

        var sb = new StringBuilder();

        // ---- 索引层：全部源码文件（防爆炸上限 MaxInventoryFiles 个）----
        var inventory = new List<(string Rel, int Lines, string Sigs)>();
        var lineCache = new Dictionary<string, string[]>(StringComparer.Ordinal);   // 供正文层复用，避免二次读盘
        foreach (var (rel, bytes) in files.OrderBy(f => f.Rel, StringComparer.Ordinal).Take(MaxInventoryFiles))
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(Path.Combine(sourceRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                inventory.Add((sourcePrefix + rel, 0, "（读取失败）"));
                continue;
            }
            lineCache[rel] = lines;
            inventory.Add((sourcePrefix + rel, lines.Length, ExtractSignatures(lines)));
        }

        sb.AppendLine($"# 源码全量索引（跳过插件/三方库后的 {inventory.Count} 个源码文件：路径 · 行数 · 类型与公共成员签名）");
        foreach (var (rel, lines, sigs) in inventory)
        {
            sb.AppendLine($"- {rel}（{lines}行）{sigs}");
        }
        if (files.Count > MaxInventoryFiles)
        {
            sb.AppendLine($"（源码文件共 {files.Count} 个，索引只列前 {MaxInventoryFiles} 个——超出部分未进索引，属异常大工程，请人工确认）");
        }

        // ---- 正文层：玩法代码根目录下全文收录 ----
        var (codeDirRel, nominated) = ResolveCodeDir(root, engine, sourceRoot, sourcePrefix, codeRoot, files);
        var dirPrefix = codeDirRel.Length == 0 ? "" : codeDirRel + "/";
        var candidates = files
            .Where(f => f.Rel.StartsWith(dirPrefix, StringComparison.Ordinal))
            .OrderBy(f => f.Rel.Count(c => c == '/'))            // 浅层优先（确定性）
            .ThenBy(f => f.Rel, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine();
        sb.AppendLine(nominated
            ? $"# 玩法代码正文（根：{sourcePrefix}{codeDirRel}——自动提名，未经用户确认；请在向导①确认玩法代码目录后重新分析）"
            : $"# 玩法代码正文（根：{sourcePrefix}{codeDirRel}——用户确认的玩法代码目录）");
        if (candidates.Count == 0)
        {
            sb.AppendLine("（该目录下没有源码文件——请检查玩法代码目录是否填对）");
            return sb.ToString();
        }

        var used = sb.Length;
        var included = 0;
        var skippedOversize = 0;
        foreach (var (rel, bytes) in candidates)
        {
            if (!lineCache.TryGetValue(rel, out var lines))
            {
                try { lines = File.ReadAllLines(Path.Combine(sourceRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            }
            var text = string.Join('\n', lines);
            if (text.Length > MaxFulltextFileChars)   // 单文件过大（疑似生成物/序列化数据）：跳过并如实标注
            {
                skippedOversize++;
                continue;
            }
            if (used + text.Length > maxChars) break;   // 预算封顶：剩余文件以索引为准
            used += text.Length;
            included++;
            sb.AppendLine($"## {sourcePrefix}{rel}（{lines.Length}行，全文）");
            sb.AppendLine(text);
        }
        var budgetNote = included < candidates.Count
            ? $"（正文预算 {maxChars} 字符已用尽：收录 {included}/{candidates.Count} 个文件全文{skippedOversize} 个超大文件未入正文；未收录的以索引为准，需要时扩大预算或细化玩法代码目录）"
            : skippedOversize > 0
                ? $"（已收录全部 {included} 个文件；{skippedOversize} 个超大文件未入正文，见索引）"
                : $"（已收录全部 {included} 个文件全文）";
        sb.AppendLine(budgetNote);
        return sb.ToString();
    }

    // 解析玩法代码根：用户确认的 codeRoot（相对项目根或绝对路径）优先；非法/未填时自动提名最可能的目录并如实标注
    private static (string DirRel, bool Nominated) ResolveCodeDir(
        string root, string engine, string sourceRoot, string sourcePrefix, string? codeRoot, List<(string Rel, long Bytes)> files)
    {
        // 用户确认：规范化 → 校验目录真实存在 → 换算成相对 sourceRoot 的前缀
        if (!string.IsNullOrWhiteSpace(codeRoot))
        {
            var normalized = codeRoot.Trim().Replace('\\', '/').TrimEnd('/');
            var abs = Path.IsPathRooted(normalized) ? normalized : Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(abs))
            {
                var relToSource = Path.GetRelativePath(sourceRoot, abs).Replace('\\', '/');
                if (relToSource is not ("." or ""))
                {
                    // 玩法代码目录必须是源码根的子目录（否则索引/正文口径对不上）
                    if (files.Any(f => f.Rel.StartsWith(relToSource + "/", StringComparison.Ordinal)))
                    {
                        return (relToSource, false);
                    }
                }
                else
                {
                    return ("", false);   // 指向的就是源码根本身
                }
            }
        }
        // 自动提名：与扫描器同口径的子树计数，取最可能的目录（诚实标注"未经用户确认"）
        var nominated = ProjectScanner.RankCodeDirs(root, engine).FirstOrDefault();
        if (nominated is null) return ("", true);
        var rel = nominated.Directory;
        return (rel.StartsWith(sourcePrefix, StringComparison.Ordinal) ? rel[sourcePrefix.Length..] : rel, true);
    }

    // 类型声明 + public/export 成员签名（≤6 条，单条 ≤110 字符）——索引层的"类长什么样"事实
    private static string ExtractSignatures(string[] lines)
    {
        var sigs = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var isType = line.Contains("class ") || line.Contains("interface ") || line.Contains("struct ") || line.Contains("enum ");
            var isMember = (line.StartsWith("public ") || line.StartsWith("export ") || line.StartsWith("protected "))
                && line.Contains('(');
            if (isType || isMember)
            {
                sigs.Add(line.Length > 110 ? line[..110] + "…" : line);
                if (sigs.Count >= 6) break;
            }
        }
        return string.Join(" | ", sigs);
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
