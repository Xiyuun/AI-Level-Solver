using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;

namespace LevelForge.Platform.Tests;

// LLM 客户端：思考开关与兼容回退（平台内生成正文为空问题的根因修复）
// 背景：deepseek-flash 等推理模型的 reasoning_content 与正文共用 max_tokens；
// 长代码任务里思考会吃光预算导致 content 为空。生成链路必须能关闭思考。
public sealed class LlmClientTests
{
    private static readonly LlmConfig Config = new("http://llm.test", "k", "test-model");

    // 记录每次请求体；按设定返回响应
    private sealed class CapturingHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return respond(body);
        }
    }

    private static HttpResponseMessage Ok(string content)
    {
        var payload = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 1, completion_tokens = 1 },
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task 关闭思考_请求体带thinking_disabled()
    {
        var handler = new CapturingHandler(_ => Ok("FILE"));
        var llm = new LlmClient(new HttpClient(handler));

        var result = await llm.ChatAsync(Config, [new ChatMessage("user", "hi")],
            maxTokens: 16, disableThinking: true);

        Assert.True(result.Ok);
        Assert.Single(handler.Bodies);
        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.Bodies[0]);
    }

    [Fact]
    public async Task 关闭思考_服务端不认thinking_自动去掉重发()
    {
        var handler = new CapturingHandler(body => body.Contains("\"thinking\"", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("unknown field thinking") }
            : Ok("FILE"));
        var llm = new LlmClient(new HttpClient(handler));

        var result = await llm.ChatAsync(Config, [new ChatMessage("user", "hi")],
            maxTokens: 16, disableThinking: true);

        Assert.True(result.Ok);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("\"thinking\"", handler.Bodies[0]);
        Assert.DoesNotContain("\"thinking\"", handler.Bodies[1]);   // 兼容回退：第二次不带该字段
    }
}
