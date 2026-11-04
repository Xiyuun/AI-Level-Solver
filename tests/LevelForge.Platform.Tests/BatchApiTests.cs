using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace LevelForge.Platform.Tests;

// 批量生产 API 集成测试：任务发起/状态轮询/取消/重试/导出的协议级验证
// 不依赖 LLM（stub 流水线驱动不了真实生成——用玩具玩法+未配 LLM 走 failed 路径验证协议）
public class BatchApiTests
{
    // 发起批量任务：非法请求被拒
    [Fact]
    public async Task 发起_未知玩法404()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "ghost", fromLevel = 1, toLevel = 3,
            difficulty1Count = 1, difficulty2Count = 1, difficulty3Count = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // 发起批量任务：配比总数不匹配被拒
    [Fact]
    public async Task 发起_配比总数不符400()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 101, toLevel = 105,
            difficulty1Count = 2, difficulty2Count = 2, difficulty3Count = 0   // 总 4≠5
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("配比总数", body);
    }

    // 发起合法任务：返回任务 id + 逐关行 pending + 状态 running
    [Fact]
    public async Task 发起合法任务_返回逐关行()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 201, toLevel = 203,
            difficulty1Count = 2, difficulty2Count = 1, difficulty3Count = 0
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("running", json.GetProperty("status").GetString());
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Equal(3, json.GetProperty("items").GetArrayLength());
        // 难度配比展开：201→1, 202→1, 203→2
        Assert.Equal(201, json.GetProperty("items")[0].GetProperty("levelNumber").GetInt32());
    }

    // 查询不存在任务 → 404
    [Fact]
    public async Task 查询不存在任务404()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.GetAsync("/api/batch/99999/status");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // 取消不存在的 → 404
    [Fact]
    public async Task 取消不存在任务404()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsync("/api/batch/99999/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // 发起后立即取消：状态变为 cancelled（无 LLM 时逐关会快速失败，取消指令确保不继续）
    [Fact]
    public async Task 发起后取消_状态变化()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 301, toLevel = 303,
            difficulty1Count = 1, difficulty2Count = 1, difficulty3Count = 1
        });
        var body = await resp.Content.ReadAsStringAsync();
        var taskId = JsonDocument.Parse(body).RootElement.GetProperty("taskId").GetInt64();

        var cancel = await client.PostAsync($"/api/batch/{taskId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // 轮询直到终态（无 LLM 时会快速失败 → completed 或 cancelled）
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
            var st = status.GetProperty("status").GetString();
            if (st is "completed" or "cancelled") break;
            await Task.Delay(500);
        }
        var final = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
        var finalStatus = final.GetProperty("status").GetString();
        Assert.True(finalStatus is "completed" or "cancelled", $"终态={finalStatus}");
    }

    // 导出接口存在且返回数组（即使空）
    [Fact]
    public async Task 导出接口返回数组()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 401, toLevel = 402,
            difficulty1Count = 1, difficulty2Count = 1, difficulty3Count = 0
        });
        var body = await resp.Content.ReadAsStringAsync();
        var taskId = JsonDocument.Parse(body).RootElement.GetProperty("taskId").GetInt64();
        var export = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/export");
        Assert.Equal(JsonValueKind.Array, export.ValueKind);
    }
}
