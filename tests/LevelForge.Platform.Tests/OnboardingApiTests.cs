using System.Net;
using System.Text;
using System.Text.Json;

namespace LevelForge.Platform.Tests;

// 接入向导 API：六步任务种子与步进、玩法状态变更（分级接入）、门2驱动配置登记
public sealed class OnboardingApiTests
{
    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(HttpClient client, string url, object body)
    {
        var resp = await client.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<List<Api.OnboardingTaskDto>> GetTasksAsync(HttpClient client)
    {
        return JsonSerializer.Deserialize<List<Api.OnboardingTaskDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/onboarding"), TestJson.Options)!;
    }

    [Fact]
    public async Task 六步任务_内置玩法种子为已完成()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var tasks = await GetTasksAsync(client);
        Assert.Equal(6, tasks.Count);
        Assert.Equal(1, tasks[0].Step);
        Assert.Equal(6, tasks[^1].Step);
        Assert.All(tasks, t => Assert.Equal("done", t.Status));   // 内置玩法随平台发布
        Assert.Contains(tasks, t => t.Title.Contains("登记"));
    }

    [Fact]
    public async Task 步进更新_状态与存档()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 第二步：资格评估报告存档 + 标记完成
        var report = "四问结论：回合制✓ 确定性✓ 状态可枚举（上界约1e6）✓ 持续产关✓；证据见 qualification-report.md";
        var (status, _) = await PostAsync(client, "/api/games/sokoban-toy/onboarding/2",
            new { status = "done", detail = report });
        Assert.Equal(HttpStatusCode.OK, status);

        var tasks = await GetTasksAsync(client);
        Assert.Contains(report, tasks[1].Detail);   // 报告原文存档可回看

        // 非法状态与步号被拒
        var badStatus = await PostAsync(client, "/api/games/sokoban-toy/onboarding/2", new { status = "oops" });
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.Status);
        var badStep = await PostAsync(client, "/api/games/sokoban-toy/onboarding/9", new { status = "done" });
        Assert.Equal(HttpStatusCode.BadRequest, badStep.Status);

        // 未知玩法 404
        var ghost = await PostAsync(client, "/api/games/ghost/onboarding/2", new { status = "done" });
        Assert.Equal(HttpStatusCode.NotFound, ghost.Status);
    }

    [Fact]
    public async Task 玩法状态变更_试用档与转正()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 切试用档（分级接入 §5：门1通过即可试用，结论带"未经对拍"水印）
        var trial = await PostAsync(client, "/api/games/sokoban-toy/status", new { status = "trial" });
        Assert.Equal(HttpStatusCode.OK, trial.Status);
        var games = JsonSerializer.Deserialize<List<Api.GameSummaryDto>>(
            await client.GetStringAsync("/api/games"), TestJson.Options)!;
        Assert.Equal("trial", Assert.Single(games).Status);

        // 转正（门2/门3 补验后）
        var production = await PostAsync(client, "/api/games/sokoban-toy/status", new { status = "production" });
        Assert.Equal(HttpStatusCode.OK, production.Status);

        // 非法状态被拒
        var bad = await PostAsync(client, "/api/games/sokoban-toy/status", new { status = "golden" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
    }

    [Fact]
    public async Task 门2驱动登记_写入后对拍可运行()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 默认玩具：门2不适用
        var before = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-002"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, before.StatusCode);

        // 登记驱动（cmd 直接退出——足够证明配置写入并被执行器消费）
        var register = await PostAsync(client, "/api/games/sokoban-toy/gate2-driver", new
        {
            command = "cmd",
            args = new[] { "/c", "exit", "1" },
            timeoutSeconds = 10,
        });
        Assert.Equal(HttpStatusCode.OK, register.Status);

        // 门2从"不适用"变为"可运行"：驱动真实执行并如实失败
        var run = await client.PostAsync("/api/games/sokoban-toy/gates/2/run", new StringContent(
            """{"levelId":"toy-002"}""", Encoding.UTF8, "application/json"));
        run.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<Api.Gate2RunResponseDto>(
            await run.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(result.Passed);
        Assert.Contains("未产出报告", result.Message);

        // 缺 command/args 被拒
        var bad = await PostAsync(client, "/api/games/sokoban-toy/gate2-driver", new { command = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
    }
}
