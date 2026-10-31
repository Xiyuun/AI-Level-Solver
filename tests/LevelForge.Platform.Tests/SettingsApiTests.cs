using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 设置 API：默认值、读写回显、重启持久化、LLM 测试连接（stub 网络层，不打真实 API）
public sealed class SettingsApiTests
{
    private static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, TestJson.Options);
        return await client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, TestJson.Options);
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task 默认值与读写回显()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 种子默认值：LLM 三件套为空、生成默认值、默认玩法
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        var root = doc.RootElement;
        Assert.Equal("", root.GetProperty("llm.baseUrl").GetString());
        Assert.Equal("", root.GetProperty("llm.apiKey").GetString());
        Assert.Equal(3, root.GetProperty("gen.candidateLimit").GetInt32());
        Assert.Equal(30000, root.GetProperty("gen.solveTimeoutMs").GetInt32());
        Assert.Equal("sokoban-toy", root.GetProperty("ui.activeGame").GetString());

        // 合并更新：只动传入的键
        var put = await PutJsonAsync(client, "/api/settings",
            new Dictionary<string, object?>
            {
                ["gen.candidateLimit"] = 5,
                ["llm.model"] = "test-model"
            });
        put.EnsureSuccessStatusCode();
        using var updated = JsonDocument.Parse(await put.Content.ReadAsStringAsync());
        Assert.Equal(5, updated.RootElement.GetProperty("gen.candidateLimit").GetInt32());
        Assert.Equal("test-model", updated.RootElement.GetProperty("llm.model").GetString());
        Assert.Equal(30000, updated.RootElement.GetProperty("gen.solveTimeoutMs").GetInt32());   // 未动的键保持
    }

    [Fact]
    public async Task 设置重启持久化()
    {
        var dir = Path.Combine(Path.GetTempPath(), "levelforge-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 第一次进程：写入设置后关闭
            using (var first = new PlatformFactory(dir))
            {
                var client = first.CreateClient();
                var put = await PutJsonAsync(client, "/api/settings",
                    new Dictionary<string, object?> { ["gen.candidateLimit"] = 7 });
                put.EnsureSuccessStatusCode();
            }
            // 第二次进程：同一数据目录，值仍在（SQLite 持久化，非内存态）
            using var second = new PlatformFactory(dir);
            var client2 = second.CreateClient();
            using var doc = JsonDocument.Parse(await client2.GetStringAsync("/api/settings"));
            Assert.Equal(7, doc.RootElement.GetProperty("gen.candidateLimit").GetInt32());
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task LLM测试连接_成功路径()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"choices":[{"message":{"content":"pong"}}]}""");
        using var factory = new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        var client = factory.CreateClient();

        var resp = await PostJsonAsync(client, "/api/settings/llm/test",
            new Api.LlmTestRequestDto("http://llm.test/v1", "key-1", "test-model"));
        resp.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<Api.LlmTestResponseDto>(
            await resp.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal("pong", result.ReplyPreview);
    }

    [Fact]
    public async Task LLM测试连接_失败路径带原因()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, "invalid api key");
        using var factory = new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => handler)));
        var client = factory.CreateClient();

        var resp = await PostJsonAsync(client, "/api/settings/llm/test",
            new Api.LlmTestRequestDto("http://llm.test/v1", "bad-key", "test-model"));
        resp.EnsureSuccessStatusCode();   // 连接测试本身 200：失败结论在 body 里
        var result = JsonSerializer.Deserialize<Api.LlmTestResponseDto>(
            await resp.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(result.Ok);
        Assert.Contains("HTTP 401", result.Error);
    }

    [Fact]
    public async Task LLM测试连接_未配置给出明确提示()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var resp = await PostJsonAsync(client, "/api/settings/llm/test", new { });
        resp.EnsureSuccessStatusCode();
        var result = JsonSerializer.Deserialize<Api.LlmTestResponseDto>(
            await resp.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(result.Ok);
        Assert.Contains("未配置", result.Error);
    }

    // 桩网络层：按预设状态码/响应体应答，不打真实网络
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
