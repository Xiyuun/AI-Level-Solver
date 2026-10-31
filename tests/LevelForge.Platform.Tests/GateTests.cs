using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Database;
using LevelForge.Platform.Gates;
using LevelForge.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 三道门：门1运行卡、门2比对器与子进程对拍、门3基准 diff 三色分级、验证记录查询
public sealed class GateTests
{
    private static async Task<string> PostAsync(HttpClient client, string url)
    {
        var resp = await client.PostAsync(url, null);
        return await resp.Content.ReadAsStringAsync();
    }

    // 取一份真实磁带（求解 toy-002）
    private static async Task<SolutionTape> GetTapeAsync(HttpClient client, string levelId)
    {
        var solveResp = await client.PostAsync("/api/solve", new StringContent(
            JsonSerializer.Serialize(new { gameId = "sokoban-toy", levelId }, TestJson.Options),
            Encoding.UTF8, "application/json"));
        solveResp.EnsureSuccessStatusCode();
        var solve = JsonSerializer.Deserialize<Api.SolveResponseDto>(
            await solveResp.Content.ReadAsStringAsync(), TestJson.Options)!;
        return TapeJson.Parse(await client.GetStringAsync($"/api/tapes/{solve.TapeId}"));
    }

    [Fact]
    public async Task 门1_玩具全绿且落验证记录()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var raw = await PostAsync(client, "/api/games/sokoban-toy/gates/1/run");
        var result = JsonSerializer.Deserialize<Api.Gate1RunResponseDto>(raw, TestJson.Options)!;
        Assert.True(result.Passed);
        // 五项检查全部可见且通过（零豁免）
        Assert.True(result.Report!["dependencyScan"]!["passed"]!.GetValue<bool>());
        Assert.True(result.Report["stateKeyDiscrimination"]!["passed"]!.GetValue<bool>());
        Assert.True(result.Report["purity"]!["passed"]!.GetValue<bool>());
        Assert.True(result.Report["determinism"]!["passed"]!.GetValue<bool>());
        Assert.True(result.Report["kernelSelfTest"]!["passed"]!.GetValue<bool>());

        // 记录落库：指纹字段齐备（适配指纹/内核版本/规则矩阵哈希）
        var records = JsonSerializer.Deserialize<List<Api.VerificationRecordDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/verification-records"), TestJson.Options)!;
        var record = Assert.Single(records);
        Assert.Equal("gate1", record.Gate);
        Assert.Equal("pass", record.Verdict);
        Assert.StartsWith("sha256:", record.AdapterHash);
        Assert.StartsWith("sha256:", record.RuleMatrixHash);
        Assert.Equal(TapeConstants.KernelVersion, record.KernelVer);
    }

    [Fact]
    public async Task 门2_玩具不适用_如实返回409()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var resp = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-001"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("不适用", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 门2比对器_一致则通过()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var tape = await GetTapeAsync(client, "toy-002");

        // 完美 harness：逐字段对齐磁带的摘要与结局
        var report = new HarnessReport(
            tape.LevelHash, tape.Initial.SemanticStateHash,
            tape.Steps.Select(s => s.SemanticStateHash).ToList(),
            "win", "toy-build-1", "sha256:harness-1");
        var verdict = Gate2Comparator.Compare(tape, report);
        Assert.True(verdict.Passed);
        Assert.Null(verdict.FirstDivergenceStep);
        Assert.True(verdict.OutcomeMatch);
    }

    [Fact]
    public async Task 门2比对器_首分歧与初始错位定位()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var tape = await GetTapeAsync(client, "toy-002");
        var steps = tape.Steps.Select(s => s.SemanticStateHash).ToList();

        // 第 2 步语义不一致 → 首分歧 = 2
        var diverged = steps.ToArray();
        diverged[1] = "sha256:wrong-step";
        var step2 = Gate2Comparator.Compare(tape, new HarnessReport(
            tape.LevelHash, tape.Initial.SemanticStateHash, diverged, "win", null, null));
        Assert.False(step2.Passed);
        Assert.Equal(2, step2.FirstDivergenceStep);
        Assert.Contains("第 2 步", step2.DivergenceDetail);

        // 初始即不一致 → 第 0 步
        var step0 = Gate2Comparator.Compare(tape, new HarnessReport(
            tape.LevelHash, "sha256:wrong-initial", steps, "win", null, null));
        Assert.False(step0.Passed);
        Assert.Equal(0, step0.FirstDivergenceStep);

        // 加载错位（关卡指纹不符）→ 判关卡数据错位，不是规则分歧
        var mismatch = Gate2Comparator.Compare(tape, new HarnessReport(
            "sha256:other-level", tape.Initial.SemanticStateHash, steps, "win", null, null));
        Assert.False(mismatch.Passed);
        Assert.Contains("错位", mismatch.DivergenceDetail);

        // 步数不一致 → 分歧在较短一方的下一步
        var shorted = Gate2Comparator.Compare(tape, new HarnessReport(
            tape.LevelHash, tape.Initial.SemanticStateHash, steps.Take(1).ToList(), "win", null, null));
        Assert.False(shorted.Passed);
        Assert.Equal(2, shorted.FirstDivergenceStep);
        Assert.Contains("步数不一致", shorted.DivergenceDetail);
    }

    [Fact]
    public async Task 门2_子进程对拍全链路_完美harness通过()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var tape = await GetTapeAsync(client, "toy-002");

        // 预制"完美 harness 报告"，用 cmd copy 冒充驱动：把它拷到 {outputFile}
        var prepared = Path.Combine(Path.GetTempPath(), $"perfect-report-{Guid.NewGuid():N}.json");
        File.WriteAllText(prepared, JsonSerializer.Serialize(new
        {
            levelHash = tape.LevelHash,
            initialSemanticHash = tape.Initial.SemanticStateHash,
            stepSemanticHashes = tape.Steps.Select(s => s.SemanticStateHash).ToList(),
            outcome = "win",
            gameBuildFingerprint = "toy-build-1",
            harnessHash = "sha256:harness-1"
        }, TestJson.Options));
        EnableToyGate2(factory, "cmd", ["/c", "copy", "/y", prepared, "{outputFile}"], 30);

        var resp = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-002"}""", Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<Api.Gate2RunResponseDto>(
            await resp.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.True(result.Passed);
        Assert.Equal("toy-build-1", result.GameBuildFingerprint);
        Assert.Null(result.FirstDivergenceStep);

        // 记录：门2结论 + 游戏/harness 指纹 + 语义 schema 版本（失效矩阵字段）
        var records = JsonSerializer.Deserialize<List<Api.VerificationRecordDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/verification-records?gate=gate2"), TestJson.Options)!;
        var record = Assert.Single(records);
        Assert.Equal("pass", record.Verdict);
        Assert.Equal("toy-build-1", record.GameBuildFingerprint);
        Assert.Equal("sha256:harness-1", record.HarnessHash);
        Assert.Equal(1, record.SemanticSchemaVer);
    }

    [Fact]
    public async Task 门2_驱动未产出报告与超时_如实失败()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 驱动直接退出（无报告）
        EnableToyGate2(factory, "cmd", ["/c", "exit", "1"], 30);
        var noReport = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-002"}""", Encoding.UTF8, "application/json"));
        noReport.EnsureSuccessStatusCode();
        var failed = JsonSerializer.Deserialize<Api.Gate2RunResponseDto>(
            await noReport.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(failed.Passed);
        Assert.Contains("未产出报告", failed.Message);

        // 驱动挂起 30 秒但超时设 1 秒 → kill 标 timeout（不等它跑完）
        EnableToyGate2(factory, "cmd", ["/c", "ping", "-n", "30", "127.0.0.1"], 1);
        var timeout = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-002"}""", Encoding.UTF8, "application/json"));
        timeout.EnsureSuccessStatusCode();
        var timed = JsonSerializer.Deserialize<Api.Gate2RunResponseDto>(
            await timeout.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(timed.Passed);
        Assert.Contains("超时", timed.Message);
    }

    [Fact]
    public async Task 门3_无基准时全部进待确认集合()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var raw = await PostAsync(client, "/api/games/sokoban-toy/gates/3/run");
        var result = JsonSerializer.Deserialize<Api.Gate3RunResponseDto>(raw, TestJson.Options)!;
        Assert.Equal(10, result.Total);
        Assert.Equal(10, result.NewLevels);
        Assert.Equal(0, result.Matched);
        Assert.Equal(0, result.Red);
        // 门3新跑不自动成为自己的预期：记录 verdict 为 pass（无红灯），不是自动入基准
        var records = JsonSerializer.Deserialize<List<Api.VerificationRecordDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/verification-records?gate=gate3"), TestJson.Options)!;
        Assert.Equal("pass", Assert.Single(records).Verdict);
    }

    [Fact]
    public async Task 门3_基准对齐后全match()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        SeedBaselinesFromCurrentRuns(factory, client, only: null);   // 全部 10 关按当前结果建基准

        var raw = await PostAsync(client, "/api/games/sokoban-toy/gates/3/run");
        var result = JsonSerializer.Deserialize<Api.Gate3RunResponseDto>(raw, TestJson.Options)!;
        Assert.Equal(10, result.Matched);
        Assert.Equal(0, result.Red);
        Assert.Equal(0, result.Yellow);
        Assert.Equal(0, result.NewLevels);
        Assert.All(result.Rows, r => Assert.Contains("语义结果与基准一致", r.Notes));
    }

    [Fact]
    public async Task 门3_结论变化红灯_轨迹差异黄灯()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var reg = factory.Registry.TryGet("sokoban-toy", out var facade) ? facade.Registration : null!;
        Assert.NotNull(reg);

        // toy-001 基准被伪造成"无解"（模拟旧版本错误预期）→ 本次 solved：红灯
        factory.Services.GetRequiredService<BaselineStore>().Insert(
            "sokoban-toy", "toy-001", "sha256:fake-old-file",
            reg.AdapterVersion, reg.AdapterHash, reg.KernelVersion, reg.ContractVersion,
            null, null,
            "unsolvableProven", "notApplicable", true, "frontierEmpty",
            0, 5, "", null, null, "tester");
        // toy-002 基准：结论与解长一致但轨迹摘要不同 → 黄灯（路径差异待核对，不判回退）
        var solve2 = await SolveAsync(client, "toy-002");
        factory.Services.GetRequiredService<BaselineStore>().Insert(
            "sokoban-toy", "toy-002", solve2.LevelHash,
            reg.AdapterVersion, reg.AdapterHash, reg.KernelVersion, reg.ContractVersion,
            null, null,
            "solved", "unitCostShortest", false, "goalFound",
            solve2.SolutionLength, 10, "sha256:different-trajectory", null, null, "tester");

        var raw = await PostAsync(client, "/api/games/sokoban-toy/gates/3/run");
        var result = JsonSerializer.Deserialize<Api.Gate3RunResponseDto>(raw, TestJson.Options)!;
        Assert.Equal(1, result.Red);
        Assert.Equal(1, result.Yellow);
        Assert.Equal(8, result.NewLevels);
        var red = result.Rows.Single(r => r.Verdict == "red");
        Assert.Equal("toy-001", red.LevelId);
        Assert.Equal("solved", red.Status);
        Assert.Equal("unsolvableProven", red.BaselineStatus);
        Assert.Contains(red.Notes, n => n.Contains("结论变化"));
        var yellow = result.Rows.Single(r => r.Verdict == "yellow");
        Assert.Equal("toy-002", yellow.LevelId);
        Assert.Contains(yellow.Notes, n => n.Contains("路径差异待核对"));

        // 有红灯 → 记录 verdict=fail
        var records = JsonSerializer.Deserialize<List<Api.VerificationRecordDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/verification-records?gate=gate3"), TestJson.Options)!;
        Assert.Equal("fail", Assert.Single(records).Verdict);
    }

    #region 内部工具

    // 直接求解一关（建基准时取当前结论字段）
    private static async Task<Api.SolveResponseDto> SolveAsync(HttpClient client, string levelId)
    {
        var resp = await client.PostAsync("/api/solve", new StringContent(
            JsonSerializer.Serialize(new { gameId = "sokoban-toy", levelId }, TestJson.Options),
            Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<Api.SolveResponseDto>(
            await resp.Content.ReadAsStringAsync(), TestJson.Options)!;
    }

    // 按当前求解结果为关卡建基准（模拟"人工已确认"的基准状态）
    private static void SeedBaselinesFromCurrentRuns(PlatformFactory factory, HttpClient client, string? only)
    {
        var reg = factory.Registry.TryGet("sokoban-toy", out var facade) ? facade.Registration : null!;
        var baselines = factory.Services.GetRequiredService<BaselineStore>();
        var runs = factory.Services.GetRequiredService<SolveRunStore>();
        var tapes = factory.Services.GetRequiredService<TapeStore>();
        var games = factory.Services.GetRequiredService<GameStore>();
        foreach (var level in games.ListLevels("sokoban-toy"))
        {
            if (only is not null && level.LevelId != only) continue;
            var solve = SolveAsync(client, level.LevelId).GetAwaiter().GetResult();
            var run = runs.Get(solve.RunId)!;
            baselines.Insert(
                "sokoban-toy", level.LevelId, level.LevelHash,
                reg.AdapterVersion, reg.AdapterHash, reg.KernelVersion, reg.ContractVersion,
                null, run.RunConfigHash,
                run.Status, run.Optimality, run.GraphComplete, run.TerminationReason,
                run.SolutionLength, run.StatesExplored,
                tapes.FindByRun(run.Id)?.TrajectoryHash ?? "", tapes.FindByRun(run.Id)?.ArtifactHash,
                null, "tester");
        }
    }

    // 给玩具玩法临时启用门2（直改 games 表；真实玩法的驱动配置来自接入登记）
    private static void EnableToyGate2(PlatformFactory factory, string command, string[] args, int timeoutSeconds)
    {
        var driver = JsonSerializer.Serialize(new { command, args, timeoutSeconds }, TestJson.Options);
        using var conn = factory.Database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE games SET gate2_applicable = 1, gate2_driver_json = $d WHERE id = 'sokoban-toy'";
        cmd.Parameters.AddWithValue("$d", driver);
        cmd.ExecuteNonQuery();
    }

    #endregion
}
