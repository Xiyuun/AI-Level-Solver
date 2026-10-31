using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Database;
using LevelForge.Platform.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 求解 API：存量关/质检粘贴、五维结论落库、磁带落盘与获取、诚实语义（无解≠未知）
public sealed class SolveApiTests
{
    private static async Task<(HttpStatusCode Status, string Body)> SolveRawAsync(HttpClient client, object body)
    {
        var json = JsonSerializer.Serialize(body, TestJson.Options);
        var resp = await client.PostAsync("/api/solve",
            new StringContent(json, Encoding.UTF8, "application/json"));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Api.SolveResponseDto> SolveAsync(HttpClient client, object body)
    {
        var (status, raw) = await SolveRawAsync(client, body);
        Assert.True(status == HttpStatusCode.OK, $"求解应返回 200，实际 {status}：{raw}");
        return JsonSerializer.Deserialize<Api.SolveResponseDto>(raw, TestJson.Options)!;
    }

    [Fact]
    public async Task 求解存量关_全链路落库落盘()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var outcome = await SolveAsync(client, new { gameId = "sokoban-toy", levelId = "toy-001" });

        // 五维结论：一步推箱即胜，最优性已证，图未走完（早停）
        Assert.Equal(SolveStatus.Solved, outcome.Status);
        Assert.Equal(Optimality.UnitCostShortest, outcome.Optimality);
        Assert.False(outcome.GraphComplete);
        Assert.Equal(TerminationReason.GoalFound, outcome.TerminationReason);
        Assert.Equal(1, outcome.SolutionLength);
        Assert.True(outcome.RunId > 0);
        Assert.NotNull(outcome.TapeId);
        Assert.StartsWith("sha256:", outcome.LevelHash);

        // 难度评估随结论返回（带模型版本，仅模型内有解时给档）
        Assert.InRange(outcome.Difficulty.Band, 1, 3);
        Assert.Equal("sokoban-fixed-v1", outcome.Difficulty.ModelVersion);

        // 落库：solve_run 行与响应一致，指纹与注册表对齐
        var row = factory.Services.GetRequiredService<SolveRunStore>().Get(outcome.RunId);
        Assert.NotNull(row);
        Assert.Equal("solved", row!.Status);
        Assert.Equal("unitCostShortest", row.Optimality);
        Assert.Equal(1, row.SolutionLength);
        Assert.Equal(outcome.TapeId, row.TapeId);
        Assert.Null(row.RunConfigHash);   // 未声明预算 = 无资源条件
        var toy = factory.Registry.TryGet("sokoban-toy", out var facade) ? facade.Registration : null;
        Assert.NotNull(toy);
        Assert.Equal(toy!.AdapterHash, row.AdapterHash);
        Assert.Equal(toy.KernelVersion, row.KernelVer);

        // 落盘：磁带文件真实存在于数据目录
        var tapeFile = Path.Combine(factory.Database.DataDir, "tapes", $"{outcome.TapeId}.tape.json");
        Assert.True(File.Exists(tapeFile));
    }

    [Fact]
    public async Task 磁带获取_与求解结论往返一致()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var outcome = await SolveAsync(client, new { gameId = "sokoban-toy", levelId = "toy-002" });

        var tapeRaw = await client.GetStringAsync($"/api/tapes/{outcome.TapeId}");
        var tape = TapeJson.Parse(tapeRaw);   // 平台回传的必须是合法磁带格式
        Assert.Equal("sokoban-toy", tape.GameId);
        Assert.Equal("toy-002", tape.LevelRef);
        Assert.Equal(outcome.LevelHash, tape.LevelHash);
        Assert.Equal(outcome.SolutionLength, tape.Steps.Count);
        Assert.Equal(ExecutionOutcome.Win, tape.Outcome);   // 结局由实际执行重判
        Assert.NotEmpty(tape.TrajectoryHash);
        Assert.StartsWith("sha256:", tape.ArtifactHash);

        // 不存在的磁带 404
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/tapes/999999")).StatusCode);
    }

    [Fact]
    public async Task 质检粘贴_原生直通_不写入关卡表()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var pasted = """{"id":"qc-900","grid":["######","#    #","#@$ .#","#    #","######"]}""";
        var outcome = await SolveAsync(client, new { gameId = "sokoban-toy", levelJson = pasted });

        Assert.Equal(SolveStatus.Solved, outcome.Status);
        Assert.Equal("质检:qc-900", outcome.LevelRef);

        // 质检是临时验证：不产生 levels 新行（内置仍是 10 关）
        var levelsRaw = await client.GetStringAsync("/api/games/sokoban-toy/levels");
        var levels = JsonSerializer.Deserialize<List<Api.LevelDto>>(levelsRaw, TestJson.Options)!;
        Assert.Equal(10, levels.Count);
        Assert.DoesNotContain(levels, l => l.LevelId == "qc-900");
    }

    [Fact]
    public async Task 已知无解关_结论诚实()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var outcome = await SolveAsync(client, new { gameId = "sokoban-toy", levelId = "toy-010" });

        // 唯一合法的"无解"：完整遍历且前沿耗尽；未知绝不冒充无解（内核铁律的平台侧呈现）
        Assert.Equal(SolveStatus.UnsolvableProven, outcome.Status);
        Assert.True(outcome.GraphComplete);
        Assert.Equal(TerminationReason.FrontierEmpty, outcome.TerminationReason);
        Assert.Null(outcome.TapeId);                        // 无解没有可回放内容
        Assert.Equal(0, outcome.Difficulty.Band);           // 无解不评分
        Assert.Equal("不适用", outcome.Difficulty.BandName);
    }

    [Fact]
    public async Task 非法关卡_Invalid且落库()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var outcome = await SolveAsync(client, new { gameId = "sokoban-toy", levelJson = "{oops" });

        Assert.Equal(SolveStatus.Invalid, outcome.Status);
        Assert.Equal(TerminationReason.InvalidInput, outcome.TerminationReason);
        Assert.Contains("解析失败", outcome.ErrorMessage);

        // Invalid 也是一次真实运行：落库可查（供历史记录与失败分析）
        var row = factory.Services.GetRequiredService<SolveRunStore>().Get(outcome.RunId);
        Assert.NotNull(row);
        Assert.Equal("invalid", row!.Status);
    }

    [Fact]
    public async Task 求解目标约束与未知引用()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var both = await SolveRawAsync(client,
            new { gameId = "sokoban-toy", levelId = "toy-001", levelJson = "{}" });
        Assert.Equal(HttpStatusCode.BadRequest, both.Status);   // 二选一：都给=歧义

        var neither = await SolveRawAsync(client, new { gameId = "sokoban-toy" });
        Assert.Equal(HttpStatusCode.BadRequest, neither.Status); // 二选一：都不给=空请求

        var unknownGame = await SolveRawAsync(client, new { gameId = "ghost", levelId = "toy-001" });
        Assert.Equal(HttpStatusCode.NotFound, unknownGame.Status);

        var unknownLevel = await SolveRawAsync(client, new { gameId = "sokoban-toy", levelId = "toy-999" });
        Assert.Equal(HttpStatusCode.NotFound, unknownLevel.Status);
    }

    [Fact]
    public async Task 节点预算耗尽_未知不冒充无解()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        // toy-007 两箱关：3 个节点的预算必然耗尽且尚未出结论
        var outcome = await SolveAsync(client,
            new { gameId = "sokoban-toy", levelId = "toy-007", nodeBudget = 3 });

        Assert.Equal(SolveStatus.ResourceLimit, outcome.Status);
        Assert.Equal(TerminationReason.NodeLimit, outcome.TerminationReason);
        Assert.Contains("节点预算", outcome.ErrorMessage);
        Assert.Equal(0, outcome.Difficulty.Band);   // 未知不评分

        // 声明了预算：资源条件指纹落库非空（该结论只在同等条件下可复用）
        var row = factory.Services.GetRequiredService<SolveRunStore>().Get(outcome.RunId);
        Assert.NotNull(row);
        Assert.StartsWith("sha256:", row!.RunConfigHash);
    }

    [Fact]
    public async Task JSON口径_camelCase与小写枚举()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var (_, raw) = await SolveRawAsync(client, new { gameId = "sokoban-toy", levelId = "toy-001" });

        // 前端契约：字段 camelCase、枚举小写驼峰（TapeJson.Options 同款口径）
        Assert.Contains("\"status\":\"solved\"", raw);
        Assert.Contains("\"terminationReason\":\"goalFound\"", raw);
        Assert.Contains("\"optimality\":\"unitCostShortest\"", raw);
        Assert.Contains("\"difficulty\":", raw);
        Assert.Contains("\"runId\":", raw);
    }
}
