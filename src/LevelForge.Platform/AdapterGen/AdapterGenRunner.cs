using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Gates;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Plugins;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.AdapterGen;

// 适配层平台内生成执行器（三轮 D9 解冻：解冻记录见优化文档 §五）。
// 形态：代码控制流驱动的"有限轮次受控循环"——生成 → 编译 → 错误回喂（≤MaxRounds 轮）→ 装载 → 门1 → 门1问题回喂。
// 全程事件可见；产出初稿语义——门1 全绿后仍需用户完成④人工审核清单等验收（不冒充"接入完成"）。
public sealed class AdapterGenRunner(
    LlmClient llm, SettingsStore settings, GameStore games, AppDatabase db,
    PluginLoader loader, GateService gates)
{
    public const int MaxRounds = 4;            // 生成-修复总轮次上限（含首轮）
    private const int MaxGenTokens = 16384;    // 推理模型思考+正文共用输出预算（deepseek 实测思考可达 38K 字符——8K 必然截断正文）
    private static readonly int CompileTimeoutMs = 180_000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed record GenFile(string Name, string Content);

    // 单轮执行：job 负责事件记录，runner 只做逻辑与判定
    public async Task<AdapterGenReport> RunAsync(AdapterGenJob job, CancellationToken ct)
    {
        var game = games.FindGame(job.GameId)
            ?? throw new AdapterGenException($"未知玩法：{job.GameId}");
        var root = string.IsNullOrWhiteSpace(game.ProjectRoot)
            ? throw new AdapterGenException("该玩法不是导入的项目（无项目根目录），无法生成适配层")
            : game.ProjectRoot!;
        var engine = game.EngineType ?? "unknown";
        var config = LlmConfig.Resolve(settings);
        if (!config.IsConfigured)
        {
            throw new AdapterGenException("平台内生成需要模型：请先在设置页配置 LLM（地址/模型），或改用需求包+外部编程助手路径");
        }
        // 生成完整适配层（推理模型思考 + 数千行代码）远慢于普通调用：超时下限抬到 180s
        config = config with { TimeoutSeconds = Math.Max(config.TimeoutSeconds, 180) };

        var profile = ProjectScanner.FromJson(game.ProjectProfileJson) ?? ProjectScanner.Scan(root, engine);
        var facts = ProjectAnalyzer.BuildFacts(game.DisplayName, game.Id, root, engine, profile);
        // 玩法代码全文（向导①确认的 codeRoot 优先）：生成预算读设置（默认 300K）——求解器对规则完整性的要求最高
        var genBudget = Math.Clamp(settings.Get("gen.codeBudgetChars", 300_000) <= 0 ? 300_000 : settings.Get("gen.codeBudgetChars", 300_000), 20_000, 2_000_000);
        var codeSamples = ProjectAnalyzer.SampleCode(root, engine, game.CodeRoot, maxChars: genBudget);
        var analysisText = BuildAnalysisText(game.ProjectAnalysisJson);
        var binDir = AppContext.BaseDirectory;   // 契约/内核 DLL 所在（平台运行目录）

        var workspace = Path.Combine(db.DataDir, "adapter-gen", job.GameId);
        Directory.CreateDirectory(workspace);
        var csprojName = $"{job.GameId}.csproj";
        File.WriteAllText(Path.Combine(workspace, csprojName), CsprojTemplate(job.GameId, binDir));

        var messages = new List<ChatMessage>
        {
            new("system", BuildSystemPrompt(job.GameId, binDir, workspace, csprojName)),
            new("user", facts + "\n\n" + codeSamples + "\n\n" + levelSample(root, profile) + "\n\n请产出第一版适配层源文件。"),
        };

        string? feedback = null;
        var compiled = false;
        var gate1Passed = false;
        var gate1Message = "";
        var lastError = "";

        for (var round = 1; round <= MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            if (feedback is not null)
            {
                messages.Add(new ChatMessage("user", "上一版的问题（必须全部修复后重新输出全部源文件 JSON）：\n" + feedback));
            }
            job.Append("生成", $"第 {round}/{MaxRounds} 轮：调用模型产出适配层源码（温度 0.2）", round);

            var chat = await llm.ChatAsync(config, messages, temperature: 0.2, MaxGenTokens, jsonMode: false, ct);
            if (!chat.Ok)
            {
                // 超时/偶发网络错误：计入轮次回喂重试，不一票否决（推理模型单轮可达数分钟）
                if (chat.Error?.Contains("超时") == true && round < MaxRounds)
                {
                    feedback = "上一轮生成超时。请输出更精简的适配层（聚焦可编译的最小实现，其他文件下一轮补充）。";
                    messages.Add(new ChatMessage("user", feedback));
                    job.Append("生成", $"第 {round} 轮调用超时——精简要求后重试", round);
                    continue;
                }
                throw new AdapterGenException($"模型调用失败：{chat.Error}");
            }
            if (string.IsNullOrWhiteSpace(chat.Content))
            {
                // 推理模型的思考会占用输出预算：content 为空说明预算被思考耗尽——加大预算重试
                feedback = $"输出预算被推理思考耗尽（正文为空）。请在完整输出思考之后，必须输出至少一个 FILE 标记块。";
                job.Append("生成", $"第 {round} 轮正文为空（推理耗尽预算）——加大预算回喂", round);
                continue;
            }
            messages.Add(new ChatMessage("assistant", chat.Content));

            var files = ExtractFiles(chat.Content, out var parseError);
            if (files.Count == 0)
            {
                feedback = $"输出不符合结构要求：{parseError ?? "未找到 files 数组"}。请重新输出完整 JSON。";
                job.Append("校验", $"第 {round} 轮输出不可用：{feedback}", round);
                continue;
            }
            WriteFiles(workspace, files);
            job.Append("编译", $"第 {round} 轮：已写入 {files.Count} 个源文件，编译插件项目…", round);

            var compileErrors = Compile(workspace, csprojName, ct);
            if (compileErrors.Count > 0)
            {
                compiled = false;
                lastError = string.Join("\n", compileErrors.Take(6));
                feedback = "编译错误（file(line,col): error 描述），逐条修复：\n" + string.Join("\n", compileErrors.Take(12));
                job.Append("编译", $"第 {round} 轮编译失败：{compileErrors.Count} 条错误——回喂修复", round);
                continue;
            }
            compiled = true;
            job.Append("编译", $"第 {round} 轮编译通过（{files.Count} 个源文件）", round);

            // 装载：编译产物拷贝进插件目录并加载
            CopyToPlugins(workspace, job.GameId);
            var load = loader.LoadForGame(job.GameId, allowOverwrite: true);
            if (load.Loaded != "ok")
            {
                lastError = load.Error ?? $"装载失败（{load.Loaded}）";
                feedback = "装载失败：" + lastError + "。检查 GameId 绑定与门面实现后重新输出全部源文件。";
                job.Append("装载", $"第 {round} 轮装载失败：{lastError}", round);
                continue;
            }
            job.Append("装载", "适配层装载成功——自动运行门1（五项合规检查）", round);

            var gate1 = RunGate1Safe(job.GameId);
            gate1Passed = gate1.Passed;
            gate1Message = gate1.Message;
            if (!gate1Passed)
            {
                feedback = "门1 未通过，逐条修复后重新输出全部源文件：\n" + (gate1.Issues.Count > 0
                    ? string.Join("\n", gate1.Issues.Take(12))
                    : gate1Message);
                job.Append("门1", $"第 {round} 轮门1 未通过——回喂修复", round);
                continue;
            }
            job.Append("门1", $"第 {round} 轮门1 全绿（零豁免）", round);
            return new AdapterGenReport(
                Success: true, Rounds: round, Compiled: true, Gate1Passed: true, Gate1Message: null,
                Error: null, WorkspaceDir: workspace, PluginsDir: Path.Combine(db.DataDir, "plugins", job.GameId),
                Summary: $"第 {round} 轮生成成功并通过门1。适配层为初稿：请完成④人工审核清单等验收步骤后再转正。");
        }

        var finalMessage = $"已达 {MaxRounds} 轮上限仍未通过（{(compiled ? "门1 未绿" : "编译未通过")}）。" +
                           "建议改用「需求包+外部编程助手」路径，或换更强模型后重试。最近问题：\n" + lastError;
        return new AdapterGenReport(
            Success: false, Rounds: MaxRounds, Compiled: compiled, Gate1Passed: gate1Passed,
            Gate1Message: gate1Message, Error: finalMessage, WorkspaceDir: workspace,
            PluginsDir: Path.Combine(db.DataDir, "plugins", job.GameId), Summary: "生成未成功——已给出转外部路径的建议");
    }

    #region 门1 / 提示词 / 编译 / 落盘

    // 门1 包装：GateException（理论不发生——已注册）转失败结论
    private (bool Passed, List<string> Issues, string Message) RunGate1Safe(string gameId)
    {
        try
        {
            var r = gates.RunGate1(gameId);
            var issues = new List<string>();
            try
            {
                foreach (var item in JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(r.Report, TapeJson.Options), JsonOpts).EnumerateObject())
                {
                    if (item.Value.ValueKind != JsonValueKind.Object) continue;
                    foreach (var issue in item.Value.GetProperty("issues").EnumerateArray())
                    {
                        issues.Add($"{item.Name}: {issue.GetString()}");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"门1 报告解析降级（不影响判定）：{ex.Message}");
            }
            return (r.Passed, issues, r.Passed ? "" : "门1 存在未通过项");
        }
        catch (Gates.GateException ex)
        {
            return (false, [], ex.Message);
        }
    }

    private static string BuildSystemPrompt(string gameId, string binDir, string workspace, string csprojName)
    {
        return $$"""
            你是资深适配层工程师，为 LevelForge 平台编写一个新玩法的适配层插件（C#/net10.0）。
            输出格式（严格遵守）：不要 markdown 代码块、不要解释文字。每个源文件用以下标记包裹，标记之外不得有任何内容：
            // ==== FILE: 文件名.cs ====
            （完整 C# 文件原文）
            // ==== END FILE ====
            文件名只能是简单文件名（不含路径）。可以输出多个文件块（如门面 + 解析器 + 搜索器分文件）。

            # 插件项目（平台已生成，你只产 .cs 文件）
            - 项目文件：{{workspace}}\{{csprojName}}（AssemblyName={{gameId}}）
            - 已引用（HintPath 指向平台运行目录 {{binDir}}）：LevelForge.Kernel.dll、LevelForge.FacadeContracts.dll——不要在代码中重新定义这些程序集里的类型
            - 编译命令即 dotnet build 该 csproj

            # 必须实现的门面契约（LevelForge.Platform.Facades 命名空间）
            public interface IGameFacade {
              GameRegistration Registration { get; }
              IReadOnlyList<string> BuiltinLevels { get; }
              string? TryGetLevelId(string levelJson);
              IReadOnlyList<string> FormatErrors(string levelJson);
              PlatformSolve Solve(SolveCommand command);
              ComplianceReport RunGate1Checks();
              PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves);
            }
            - GameRegistration 构造参数（按序）：GameId, DisplayName, Status, AdapterVersion, AdapterHash, KernelVersion, ContractVersion, LevelsRootDir, Gate2Applicable, VisualMappingJson, MechanicsJson, GameBrief, FormatSpec, RuleMatrix, DifficultyWeightsJson
              * GameId 必须一字不差为「{{gameId}}」；Status 用 "trial"；AdapterHash 用 AdapterFingerprint.Of(typeof(你的门面).Assembly)
              * KernelVersion/ContractVersion 用 TapeConstants.KernelVersion / TapeConstants.ContractVersion（LevelForge.Kernel.Replay）
            - SolveCommand 构造参数：(string LevelJson, byte[] LevelFileBytes, string LevelRef, TimeSpan? TimeBudget=null, long? NodeBudget=null, long? MemoryBudgetBytes=null, bool FullEnumeration=false)
            - PlatformSolve 构造参数：(SolveResult Result, SolutionTape? Tape, DifficultyAssessment Difficulty, string? RunConfigHash)
              * 无解/未实现时 Tape=null、Difficulty 用 new DifficultyAssessment(0, 0, "{{gameId}}-v1", [], [])
            - SolveResult 构造参数：(SolveStatus Status, Optimality Optimality, bool GraphComplete, TerminationReason TerminationReason, MetricsScope MetricsScope, IReadOnlyList<MoveRecord> Solution, SearchMetrics? Metrics=null, string? ErrorMessage=null)
            - ComplianceReport 由五个 ComplianceResult 组成（DependencyScan/StateKeyDiscrimination/Purity/Determinism/KernelSelfTest），用 ComplianceResult.Pass(kind, notes) 构造

            # 诚实语义（硬约束）
            - 非法关卡数据必须返回错误/抛异常，禁止静默修复；求解未证明不冒充结论
            - 求解策略：优先实现基于关卡数据的确定性搜索（如广度优先/状态去重）；确定做不到时返回 ResourceLimit 并如实说明，不要伪造 Solved

            # 求解与玩法逻辑来源
            - 玩法规则语义来自用户项目代码抽样与关卡数据（见用户消息）；无法确定的规则用保守默认并在注释中标注 // TODO-CONFIRM（需与游戏本体核对）
            {{binDir}} 目录下有参考实现源码项目：src/Adapters/LevelForge.Adapters.SokobanToy（最小示例）与 src/Adapters/LevelForge.Adapters.SlidePig（实战，含状态/搜索组织）——如需参考结构可读取其源码文件
            """;
    }

    private static string CsprojTemplate(string gameId, string binDir)
    {
        var bin = binDir.Replace("'", "");
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="**/*.cs" />
              </ItemGroup>
              <ItemGroup>
                <Reference Include="LevelForge.Kernel">
                  <HintPath>{{bin}}\LevelForge.Kernel.dll</HintPath>
                  <Private>false</Private>
                </Reference>
                <Reference Include="LevelForge.FacadeContracts">
                  <HintPath>{{bin}}\LevelForge.FacadeContracts.dll</HintPath>
                  <Private>false</Private>
                </Reference>
              </ItemGroup>
            </Project>
            """;
    }

    // 从模型输出抽取源文件（FILE 标记格式，零转义膨胀——JSON 包代码会在 8K 输出上限下截断，已弃用）
    private static List<GenFile> ExtractFiles(string content, out string? parseError)
    {
        parseError = null;
        const string startMark = "// ==== FILE:";
        const string endMark = "// ==== END FILE ====";
        var result = new List<GenFile>();
        var rest = content;
        while (true)
        {
            var startIdx = rest.IndexOf(startMark, StringComparison.Ordinal);
            if (startIdx < 0) break;
            var nameStart = rest.IndexOf(':', startIdx + startMark.Length - 1) + 1;
            var lineEnd = rest.IndexOf('\n', startIdx);
            if (lineEnd < 0) { parseError = "FILE 标记未闭合"; break; }
            // 文件名 = FILE: 与行尾之间的内容；剥掉提示词格式里的尾部 "===="（如 "// ==== FILE: Foo.cs ===="）
            var name = rest[nameStart..lineEnd].Trim().TrimEnd('=').Trim();
            var bodyStart = lineEnd + 1;
            var endIdx = rest.IndexOf(endMark, bodyStart, StringComparison.Ordinal);
            if (endIdx < 0) { parseError = $"文件 {name} 缺少 END FILE 标记"; break; }
            var body = rest[bodyStart..endIdx].Trim('\r', '\n');
            if (name.Contains('/') || name.Contains('\\') || !name.EndsWith(".cs", StringComparison.Ordinal))
            {
                parseError = $"文件名非法：{name}（只允许 .cs 简单文件名）";
                break;
            }
            result.Add(new GenFile(name, body));
            rest = rest[(endIdx + endMark.Length)..];
        }
        if (result.Count == 0 && parseError is null)
        {
            parseError = "输出中没有 FILE 标记块（模型未按格式输出）";
        }
        return result;
    }

    private static void WriteFiles(string workspace, List<GenFile> files)
    {
        // 先清旧 .cs（保留 csproj），避免上一轮残留文件混入编译
        foreach (var old in Directory.EnumerateFiles(workspace, "*.cs")) File.Delete(old);
        foreach (var f in files)
        {
            File.WriteAllText(Path.Combine(workspace, f.Name), f.Content, Encoding.UTF8);
        }
    }

    private static List<string> Compile(string workspace, string csprojName, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{Path.Combine(workspace, csprojName)}\" -v q --nologo",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        if (!proc.WaitForExitAsync(ct).Wait(CompileTimeoutMs, ct))
        {
            try { proc.Kill(true); } catch { }
            return ["dotnet build 超时（180s）"];
        }
        var text = stdout.Result + "\n" + stderr.Result;
        var errors = text.Split('\n')
            .Where(l => l.Contains("error ", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .Take(20)
            .ToList();
        return errors;
    }

    // 编译产物 → 插件目录：拷贝输出目录全部 DLL，排除契约程序集（平台共享，绝不复制——防类型分裂）
    private void CopyToPlugins(string workspace, string gameId)
    {
        var output = Path.Combine(workspace, "bin", "Debug", "net10.0");
        var target = Path.Combine(loader.PluginsRoot, gameId);
        Directory.CreateDirectory(target);
        foreach (var dll in Directory.EnumerateFiles(output, "*.dll"))
        {
            var name = Path.GetFileName(dll);
            if (name.Equals("LevelForge.Kernel.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("LevelForge.FacadeContracts.dll", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(dll, Path.Combine(target, name), overwrite: true);
        }
    }

    private static string levelSample(string projectRoot, ProjectProfile profile)
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
        try
        {
            var text = File.ReadAllText(Path.Combine(projectRoot, rel.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (text.Length > 8000) text = text[..8000] + "\n…（截断）";
            return $"# 关卡抽样（{rel.Path}，{rel.Bytes}B，原文）\n{text}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"# 关卡抽样\n{rel.Path}：读取失败";
        }
    }

    private string BuildAnalysisText(string? projectAnalysisJson)
    {
        if (string.IsNullOrWhiteSpace(projectAnalysisJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(projectAnalysisJson);
            var lines = new List<string> { "# 深度分析结论（平台内模型初稿，已过用户审核环节；仍请核验）" };
            if (doc.RootElement.TryGetProperty("fourQuestions", out var fq) && fq.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in fq.EnumerateArray())
                {
                    lines.Add($"- 四问｜{q.GetProperty("question").GetString()}：{q.GetProperty("answer").GetString()}" +
                              $"（证据：{q.GetProperty("evidence").GetString()}）");
                }
            }
            if (doc.RootElement.TryGetProperty("mechanics", out var m) && m.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in m.EnumerateArray()) lines.Add($"- 机制｜{item.GetString()}");
            }
            if (doc.RootElement.TryGetProperty("risks", out var r) && r.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in r.EnumerateArray()) lines.Add($"- 风险｜{item.GetString()}");
            }
            return string.Join("\n", lines);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    #endregion
}
