using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LevelForge.Platform.Tests;

// 异步生成任务 + SSE（前后端全流程优化 §8.4）
// 验收要点：发起即返回任务号、进度事件可实时读到、终态带完整运行记录、取消能生效
public sealed class GenerationJobTests
{
    private const string Coord = """{"width":6,"height":4,"player":{"x":1,"y":1},"boxes":[{"x":2,"y":1}],"goals":[{"x":4,"y":1}]}""";

    // 可解且难度第 1 档的坐标制候选（两步推箱）
    private sealed class StubLlm(params (int Code, string Content)[] script) : HttpMessageHandler
    {
        private readonly Queue<(int Code, string Content)> _script = new(script);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            // 延迟 80ms 模拟真实 LLM：让事件流有时间被读到
            await Task.Delay(80, ct);
            var (code, content) = _script.Count > 0 ? _script.Dequeue() : (200, "{}");
            return new HttpResponseMessage((HttpStatusCode)code)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content } } }
                }), Encoding.UTF8, "application/json")
            };
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(HttpMessageHandler llm)
    {
        // 生成 token 解析依赖真实 json 字段，因此这里用宽松的 JSON 序列化（与生产一致：HttpClient 序列化）
        return new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => llm)));
    }

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

    private static async Task<long> StartJobAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsync("/api/generate/jobs",
            new StringContent(JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var raw = await resp.Content.ReadAsStringAsync();
        var started = JsonSerializer.Deserialize<Api.GenerationJobStartedDto>(raw, TestJson.Options)!;
        Assert.True(started.JobId > 0);
        return started.JobId;
    }

    private static async Task<Api.GenerationJobDto> WaitTerminalAsync(HttpClient client, long jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var raw = await client.GetStringAsync($"/api/generate/jobs/{jobId}");
            var dto = JsonSerializer.Deserialize<Api.GenerationJobDto>(raw, TestJson.Options)!;
            if (dto.Status != "running")
            {
                return dto;
            }
            await Task.Delay(60);
        }
        throw new TimeoutException($"任务 {jobId} 15 秒内未收口");
    }

    [Fact]
    public async Task 异步生成_发起即返回任务号_终态带运行记录与事件()
    {
        var llm = new StubLlm((200, Coord));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var jobId = await StartJobAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1, levelNumber = "3" });
        var done = await WaitTerminalAsync(client, jobId);

        Assert.Equal("succeeded", done.Status);
        Assert.NotNull(done.Run);
        Assert.Equal("succeeded", done.Run!.Status);
        // 事件流必须包含"开始/出题/求解/难度/成功"这些阶段，前端进度层才有内容可显示
        Assert.NotEmpty(done.Events);
        Assert.Contains(done.Events, e => e.Stage == "llm");
        Assert.Contains(done.Events, e => e.Stage == "solver");
        Assert.Contains(done.Events, e => e.Stage == "integration");
        Assert.All(done.Events, e => Assert.True(e.Seq > 0));
        // 事件序号单调递增（前端据此去重与排序）
        Assert.Equal(done.Events.OrderBy(e => e.Seq).Select(e => e.Seq), done.Events.Select(e => e.Seq));
    }

    [Fact]
    public async Task 异步生成_SSE实时推送进度并以done收尾()
    {
        var llm = new StubLlm((200, Coord));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);
        var jobId = await StartJobAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1, levelNumber = "3" });

        using var resp = await client.GetAsync($"/api/generate/jobs/{jobId}/events",
            HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("event: progress", body);
        Assert.Contains("event: done", body);
        // done 载荷带终态与运行记录（前端不必再拉一次）
        var donePart = body[body.IndexOf("event: done", StringComparison.Ordinal)..];
        Assert.Contains("\"status\":\"succeeded\"", donePart.Replace(" ", ""));
    }

    [Fact]
    public async Task 异步生成_取消后任务落cancelled()
    {
        // 脚本给足够多的坏输出：保证任务在取消时仍在跑
        var llm = new StubLlm(Enumerable.Range(0, 20).Select(_ => (200, "not json")).ToArray());
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);
        var jobId = await StartJobAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 1, candidateLimit = 10 });

        await Task.Delay(120);
        var cancel = await client.PostAsync($"/api/generate/jobs/{jobId}/cancel", null);
        cancel.EnsureSuccessStatusCode();

        var done = await WaitTerminalAsync(client, jobId);
        Assert.Equal("cancelled", done.Status);
    }

    [Fact]
    public async Task 未配置LLM_任务落failed并给出原因()
    {
        var llm = new StubLlm();
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        // 不配置 LLM 直接发起
        var jobId = await StartJobAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1 });

        var done = await WaitTerminalAsync(client, jobId);

        Assert.Equal("failed", done.Status);
        Assert.Null(done.Run);
        Assert.Contains("LLM", done.Error);
    }

    [Fact]
    public async Task 未知玩法_发起即404()
    {
        var llm = new StubLlm();
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/api/generate/jobs",
            new StringContent("""{"gameId":"no-such-game","targetDifficulty":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
