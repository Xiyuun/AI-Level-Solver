using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Gates;

// 门运行失败（请求/配置层）：端点转 400
public sealed class GateException(string message) : Exception(message);

// 门对该玩法不适用（如玩具无游戏本体）：端点转 409
public sealed class GateNotApplicableException(string message) : Exception(message);

// 门1运行结论
public sealed record Gate1RunResult(long RecordId, bool Passed, ComplianceReport Report);

// 门2运行结论（Verdict 为 null = 执行层失败：超时/无报告/退出码非零）
public sealed record Gate2RunResult(
    long RecordId, bool Passed, string Message, string LevelId, string LevelHash,
    Gate2Verdict? Verdict, string? GameBuildFingerprint);

// 门3单关比对行（§5.2 diff 语义：match / red 红灯 / yellow 黄灯 / new 待确认）
public sealed record Gate3Row(
    string LevelId, string LevelHash, string Verdict, string Status, string? BaselineStatus,
    int SolutionLength, int? BaselineSolutionLength,
    string TerminationReason, IReadOnlyList<string> Notes);

// 门3运行结论
public sealed record Gate3RunResult(
    long RecordId, int Total, int Matched, int Red, int Yellow, int NewLevels,
    IReadOnlyList<Gate3Row> Rows);

// 门编排服务：三道门的统一入口（验证门 §2/§3/§4/§5）
// 门1=规则测试（内核五项检查）；门2=真机对拍（子进程+文件交换）；门3=全量回归（基准 diff）
public sealed class GateService
{
    private readonly GameRegistry _registry;
    private readonly GameStore _games;
    private readonly VerificationRecordStore _records;
    private readonly BaselineStore _baselines;
    private readonly SolveRunStore _runs;
    private readonly TapeStore _tapes;
    private readonly AppDatabase _db;

    public GateService(GameRegistry registry, GameStore games, VerificationRecordStore records,
        BaselineStore baselines, SolveRunStore runs, TapeStore tapes, AppDatabase db)
    {
        _registry = registry;
        _games = games;
        _records = records;
        _baselines = baselines;
        _runs = runs;
        _tapes = tapes;
        _db = db;
    }

    #region 门1：规则测试集

    // 门1：五项合规检查 + 规则矩阵指纹登记；全绿零豁免才 pass
    public Gate1RunResult RunGate1(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !_registry.TryGet(gameId, out var facade))
        {
            throw new GateException($"未知玩法：{gameId}");
        }
        var report = facade.RunGate1Checks();
        var reg = facade.Registration;
        var recordId = _records.Insert(
            gameId, "gate1", report.AllPassed ? "pass" : "fail",
            adapterHash: reg.AdapterHash, kernelVer: reg.KernelVersion, contractVer: reg.ContractVersion,
            ruleMatrixHash: HashText(reg.RuleMatrix),
            samplesJson: """["键判别维度对：规则矩阵声明的全部状态维度", "确定性检查关：首个内置关"]""",
            summaryJson: JsonSerializer.Serialize(report, TapeJson.Options));
        return new Gate1RunResult(recordId, report.AllPassed, report);
    }

    #endregion

    #region 门2：真机对拍

    // 门2：对拍单关——生成磁带 → 拉起 harness 子进程 → 逐步语义差分 → 结论登记
    public Gate2RunResult RunGate2(string gameId, string levelId)
    {
        var game = _games.FindGame(gameId)
            ?? throw new GateException($"未知玩法：{gameId}");
        if (!game.Gate2Applicable || game.Gate2DriverJson is null)
        {
            // 玩具无独立游戏本体：如实返回"不适用"，不显示"未通过"（规则矩阵备注）
            throw new GateNotApplicableException("门2（真机对拍）对该玩法不适用：无独立游戏本体或未配置对拍驱动");
        }
        var facade = _registry.TryGet(gameId, out var f) ? f : throw new GateException($"玩法未注册：{gameId}");
        var level = _games.FindLevel(gameId, levelId)
            ?? throw new GateException($"关卡不存在：{levelId}");

        // 现场磁带：对拍验证的是"当前这份内容 + 完整执行路径"（§4.1：初始及每步实际语义状态）
        var bytes = Encoding.UTF8.GetBytes(level.ContentJson);
        var solve = facade.Solve(new SolveCommand(level.ContentJson, bytes, level.LevelId));
        if (solve.Tape is null)
        {
            var message = $"当前关无解法磁带（求解结论 {solve.Result.Status}），对拍需要可执行序列";
            var noTapeId = _records.Insert(gameId, "gate2", "fail", levelHash: solve.Result.Status == SolveStatus.Solved ? null : LevelHashOf(bytes),
                adapterHash: facade.Registration.AdapterHash, kernelVer: facade.Registration.KernelVersion,
                samplesJson: level.LevelId, summaryJson: message);
            return new Gate2RunResult(noTapeId, false, message, level.LevelId, LevelHashOf(bytes), null, null);
        }
        var tape = solve.Tape;

        // 输入包：data/gate2/{guid}/ 下关卡与磁带，harness 只经文件交换（§4.5 不用 MCP）
        var workDir = Path.Combine(_db.DataDir, "gate2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var levelFile = Path.Combine(workDir, "level.json");
        var tapeFile = Path.Combine(workDir, "tape.json");
        var outputFile = Path.Combine(workDir, "result.json");
        File.WriteAllBytes(levelFile, bytes);
        File.WriteAllText(tapeFile, TapeJson.Serialize(tape));

        // 拉起子进程：命令与参数来自对拍驱动配置（占位符替换）
        var config = Gate2DriverConfig.Parse(game.Gate2DriverJson);
        var psi = new ProcessStartInfo
        {
            FileName = config.Command,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };
        foreach (var arg in config.Args)
        {
            psi.ArgumentList.Add(arg
                .Replace("{levelFile}", levelFile)
                .Replace("{tapeFile}", tapeFile)
                .Replace("{outputFile}", outputFile));
        }
        using var process = Process.Start(psi) ?? throw new GateException($"无法启动对拍驱动：{config.Command}");
        if (!process.WaitForExit(config.TimeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);   // 超时 kill 标 timeout（§4.5）
            var message = $"harness 执行超时（{config.TimeoutSeconds}s），已终止";
            return RecordGate2(gameId, facade, tape, level.LevelId, false, message, null, null);
        }
        if (!File.Exists(outputFile))
        {
            var message = $"harness 退出码 {process.ExitCode} 且未产出报告文件";
            return RecordGate2(gameId, facade, tape, level.LevelId, false, message, null, null);
        }

        // 比对：关卡指纹 → 初始 → 逐步 → 结局（§4.2 平台侧职责）
        HarnessReport harness;
        try
        {
            harness = HarnessReport.Parse(File.ReadAllText(outputFile));
        }
        catch (JsonException ex)
        {
            var message = $"harness 报告无法解析：{ex.Message}";
            return RecordGate2(gameId, facade, tape, level.LevelId, false, message, null, null);
        }
        var verdict = Gate2Comparator.Compare(tape, harness);
        var passed = verdict.Passed && verdict.OutcomeMatch;
        var finalMessage = passed
            ? "对拍通过：初始与逐步语义一致，结局一致"
            : $"对拍未通过：{verdict.DivergenceDetail ?? (verdict.OutcomeMatch ? "" : $"结局不一致（磁带 {verdict.TapeOutcome}，游戏 {verdict.HarnessOutcome}）")}";
        return RecordGate2(gameId, facade, tape, level.LevelId, passed, finalMessage, verdict, harness);
    }

    #endregion

    #region 门3：全量回归

    // 门3：全量求解 + 基准 diff（§5.2 语义分级）；新跑结果不自动入基准
    public Gate3RunResult RunGate3(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !_registry.TryGet(gameId, out var facade))
        {
            throw new GateException($"未知玩法：{gameId}");
        }
        var reg = facade.Registration;
        var rows = new List<Gate3Row>();
        foreach (var level in _games.ListLevels(gameId))
        {
            // 每关都是一次真实求解：落运行记录（审计 + 校准数据底座）
            var bytes = Encoding.UTF8.GetBytes(level.ContentJson);
            var solve = facade.Solve(new SolveCommand(level.ContentJson, bytes, level.LevelId));
            var runId = _runs.Insert(reg, level.LevelId, LevelHashOf(bytes), solve);
            if (solve.Tape is not null)
            {
                _runs.AttachTape(runId, _tapes.Save(runId, solve.Tape).Id);
            }
            var status = ToCamel(solve.Result.Status.ToString());
            var trajectory = solve.Tape?.TrajectoryHash ?? "";
            var baseline = _baselines.FindExisting(gameId, level.LevelId, reg.AdapterHash, reg.KernelVersion);
            rows.Add(Classify(level.LevelId, LevelHashOf(bytes), status, solve, trajectory, baseline));
        }
        var matched = rows.Count(r => r.Verdict == "match");
        var red = rows.Count(r => r.Verdict == "red");
        var yellow = rows.Count(r => r.Verdict == "yellow");
        var fresh = rows.Count(r => r.Verdict == "new");
        var verdict = red == 0 ? "pass" : "fail";   // 黄灯=待人工核对；新关=待确认集合；都不算失败
        var summary = JsonSerializer.Serialize(new { total = rows.Count, matched, red, yellow, fresh }, TapeJson.Options);
        var recordId = _records.Insert(
            gameId, "gate3", verdict,
            adapterHash: reg.AdapterHash, kernelVer: reg.KernelVersion, contractVer: reg.ContractVersion,

            samplesJson: JsonSerializer.Serialize(rows.Select(r => new { r.LevelId, r.LevelHash }).ToList(), TapeJson.Options),
            summaryJson: JsonSerializer.Serialize(rows.Select(r => new
            {
                r.LevelId, r.Verdict, r.Status, baseline = r.BaselineStatus, r.Notes
            }).ToList(), TapeJson.Options));
        return new Gate3RunResult(recordId, rows.Count, matched, red, yellow, fresh, rows);
    }

    #endregion

    #region 内部工具

    // 单关分级（§5.2）：结论变化=红灯；同结论解长变=红灯；同结论同解长轨迹变=黄灯；其余=match
    private static Gate3Row Classify(
        string levelId, string levelHash, string status,
        Facades.PlatformSolve solve, string trajectory, BaselineRow? baseline)
    {
        var notes = new List<string>();
        if (baseline is null)
        {
            return new Gate3Row(levelId, levelHash, "new", status, null,
                solve.Result.Solution.Count, null, ToCamel(solve.Result.TerminationReason.ToString()),
                ["无同版本基准：进入待确认集合（门3新跑不自动成为自己的预期）"]);
        }
        // 性能项单列（§5.2）：探索量倍级变化不算语义回退
        var explored = solve.Result.Metrics?.StatesExplored ?? 0;
        if (baseline.StatesExplored > 0 && (explored > baseline.StatesExplored * 2 || explored * 2 < baseline.StatesExplored))
        {
            notes.Add($"性能项单列：探索状态数 基准 {baseline.StatesExplored} → 本次 {explored}");
        }
        if (status != baseline.Status)
        {
            notes.Add($"结论变化：基准 {baseline.Status}，本次 {status}（版本对齐：同适配指纹+内核版本）");
            return new Gate3Row(levelId, levelHash, "red", status, baseline.Status,
                solve.Result.Solution.Count, baseline.SolutionLength,
                ToCamel(solve.Result.TerminationReason.ToString()), notes);
        }
        if (status == "solved" && solve.Result.Solution.Count != baseline.SolutionLength)
        {
            notes.Add($"解长变化：基准 {baseline.SolutionLength}，本次 {solve.Result.Solution.Count}");
            return new Gate3Row(levelId, levelHash, "red", status, baseline.Status,
                solve.Result.Solution.Count, baseline.SolutionLength,
                ToCamel(solve.Result.TerminationReason.ToString()), notes);
        }
        if (status == "solved" && trajectory != baseline.TrajectoryHash)
        {
            notes.Add("最短步数与结论相同但等长路径不同（轨迹摘要变化）：路径差异待核对，不仅凭轨迹摘要判定规则回退");
            return new Gate3Row(levelId, levelHash, "yellow", status, baseline.Status,
                solve.Result.Solution.Count, baseline.SolutionLength,
                ToCamel(solve.Result.TerminationReason.ToString()), notes);
        }
        notes.Add("语义结果与基准一致");
        return new Gate3Row(levelId, levelHash, "match", status, baseline.Status,
            solve.Result.Solution.Count, baseline.SolutionLength,
            ToCamel(solve.Result.TerminationReason.ToString()), notes);
    }

    // 门2结论登记（指纹含游戏版本/harness/语义 schema 版本——失效矩阵 §2.1）
    private Gate2RunResult RecordGate2(
        string gameId, IGameFacade facade, SolutionTape tape, string levelId,
        bool passed, string message, Gate2Verdict? verdict, HarnessReport? harness)
    {
        var recordId = _records.Insert(
            gameId, "gate2", passed ? "pass" : "fail",
            levelHash: tape.LevelHash,
            adapterHash: facade.Registration.AdapterHash,
            kernelVer: facade.Registration.KernelVersion,
            contractVer: facade.Registration.ContractVersion,
            gameBuildFingerprint: harness?.GameBuildFingerprint,
            harnessHash: harness?.HarnessHash,
            semanticSchemaVer: tape.SemanticSchemaVer,
            samplesJson: JsonSerializer.Serialize(new { levelId, steps = tape.Steps.Count }, TapeJson.Options),
            summaryJson: JsonSerializer.Serialize(new { passed, message, verdict }, TapeJson.Options));
        return new Gate2RunResult(recordId, passed, message, levelId, tape.LevelHash, verdict, harness?.GameBuildFingerprint);
    }

    // 文本哈希（规则矩阵等产物指纹）
    private static string HashText(string text)
    {
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    // 字节关卡哈希
    private static string LevelHashOf(byte[] bytes)
    {
        return LevelForge.Kernel.Hashing.TapeHashes.LevelHash(bytes);
    }

    // PascalCase → camelCase（落库/比较口径统一）
    private static string ToCamel(string name)
    {
        return System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name);
    }

    #endregion
}
