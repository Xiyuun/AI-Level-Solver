using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    // ---- 单关详情（预览弹窗数据源，2026-11-16 批量预览）----

    // solved 黄金路径：stub LLM 出可解题 → 任务完成后详情带题面/磁带/解长/难度
    // （解长此前恒为 null——UpdateItem 误收运行 id 且从不赋值，此处一并回归）
    [Fact]
    public async Task 单关详情_solved路径_带题面磁带与解长()
    {
        var llm = new ScriptedLlm((200, EasyLevel), (200, EasyLevel), (200, EasyLevel), (200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var started = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 601, toLevel = 602,
            difficulty1Count = 2, difficulty2Count = 0, difficulty3Count = 0
        });
        started.EnsureSuccessStatusCode();
        var taskId = JsonDocument.Parse(await started.Content.ReadAsStringAsync())
            .RootElement.GetProperty("taskId").GetInt64();

        // 轮询到终态（stub LLM 每关首候选即通过，秒级完成）
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
            if (status.GetProperty("status").GetString() is "completed" or "cancelled") break;
            await Task.Delay(300);
        }
        var final = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
        Assert.Equal("completed", final.GetProperty("status").GetString());
        Assert.Equal(2, final.GetProperty("succeeded").GetInt32());

        // 单关详情：题面可回读、磁带引用非空、解长与难度都有值
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/items/601");
        Assert.Equal("solved", detail.GetProperty("status").GetString());
        Assert.Contains("grid", detail.GetProperty("contentJson").GetString());
        Assert.True(detail.GetProperty("tapeId").GetInt64() > 0, "已解关必须带磁带引用（回放数据源）");
        Assert.True(detail.GetProperty("solutionLength").GetInt32() > 0, "解长必须来自求解运行行");
        Assert.True(detail.GetProperty("difficultyBand").GetInt32() > 0);
    }

    // failed 路径：无 LLM 逐关快速失败，详情如实带失败摘要，不带题面与磁带
    [Fact]
    public async Task 单关详情_failed路径_无题面与磁带()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var started = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 701, toLevel = 701,
            difficulty1Count = 1, difficulty2Count = 0, difficulty3Count = 0
        });
        started.EnsureSuccessStatusCode();
        var taskId = JsonDocument.Parse(await started.Content.ReadAsStringAsync())
            .RootElement.GetProperty("taskId").GetInt64();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
            if (status.GetProperty("status").GetString() is "completed" or "cancelled") break;
            await Task.Delay(300);
        }
        var final = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/status");
        Assert.Equal("completed", final.GetProperty("status").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/batch/{taskId}/items/701");
        Assert.Equal("failed", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("contentJson").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("tapeId").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("failureSummary").GetString()));
    }

    // 404：任务不存在 / 编号不在段内
    [Fact]
    public async Task 单关详情_任务或编号不存在404()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var missingTask = await client.GetAsync("/api/batch/99999/items/1");
        Assert.Equal(HttpStatusCode.NotFound, missingTask.StatusCode);

        var started = await client.PostAsJsonAsync("/api/batch/start", new
        {
            gameId = "sokoban-toy", fromLevel = 801, toLevel = 802,
            difficulty1Count = 1, difficulty2Count = 1, difficulty3Count = 0
        });
        started.EnsureSuccessStatusCode();
        var taskId = JsonDocument.Parse(await started.Content.ReadAsStringAsync())
            .RootElement.GetProperty("taskId").GetInt64();
        var missingLevel = await client.GetAsync($"/api/batch/{taskId}/items/999");
        Assert.Equal(HttpStatusCode.NotFound, missingLevel.StatusCode);
    }

    // ---- stub LLM（与 GenerationTests 同一套脚本驱动法：队列逐条应答）----

    // 可解且难度第 1 档的候选（两步推箱，score < 4）
    private const string EasyLevel = """{"id":"gen-batch","grid":["######","#@$ .#","#    #","######"]}""";

    private static async Task ConfigureLlmAsync(HttpClient client)
    {
        var put = await client.PutAsync("/api/settings", new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["llm.baseUrl"] = "http://llm.test/v1",
                ["llm.apiKey"] = "key-1",
                ["llm.model"] = "test-model"
            }), Encoding.UTF8, "application/json"));
        put.EnsureSuccessStatusCode();
    }

    private static WebApplicationFactory<Program> CreateFactory(ScriptedLlm llm)
    {
        return new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => llm)));
    }

    private sealed class ScriptedLlm(params (int Code, string Content)[] script) : HttpMessageHandler
    {
        private readonly Queue<(int Code, string Content)> _script = new(script);
        private readonly object _gate = new();   // 批量并发请求共用一个队列，出队必须互斥

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int code;
            string content;
            lock (_gate)
            {
                (code, content) = _script.Count > 0 ? _script.Dequeue() : (200, "{}");
            }
            var body = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } }
            });
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
