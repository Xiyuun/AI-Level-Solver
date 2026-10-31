using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 生成流水线：stub LLM 脚本驱动整条"出题→校验→判卷→重生"循环
// 覆盖：成功命中/格式修复/无解回填/难度不达标/候选耗尽/API失败/预算诚实/设置默认/历史查询/JSON提取
public sealed class GenerationTests
{
    // 可解且难度第 1 档的候选（两步推箱，score < 4）
    private const string EasyLevel = """{"id":"gen-101","grid":["######","#@$ .#","#    #","######"]}""";
    // 已证无解候选（角落箱，照 toy-010 换编号）
    private const string CornerLevel = """{"id":"gen-102","grid":["#####","#$  #","# . #","# @ #","#####"]}""";
    // 带 markdown 围栏的坏输出（提取不出 JSON）
    private const string BadFenced = "好的，这是关卡：```json\n{\"id\":\n```\n以上请查收";

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

    private static async Task<(HttpStatusCode Status, string Body)> GenerateAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsync("/api/generate",
            new StringContent(JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Api.GenerationRunDto> GenerateOkAsync(HttpClient client, object body)
    {
        var (status, raw) = await GenerateAsync(client, body);
        Assert.True(status == HttpStatusCode.OK, $"生成应返回 200，实际 {status}：{raw}");
        return JsonSerializer.Deserialize<Api.GenerationRunDto>(raw, TestJson.Options)!;
    }

    // stub LLM：脚本队列逐条应答，并记录每次请求体（断言提示词组成与回填）
    private sealed class ScriptedLlm(params (int Code, string Content)[] script) : HttpMessageHandler
    {
        private readonly Queue<(int Code, string Content)> _script = new(script);
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
            var (code, content) = _script.Count > 0 ? _script.Dequeue() : (200, "{}");
            var body = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } }
            });
            return new HttpResponseMessage((HttpStatusCode)code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(ScriptedLlm llm)
    {
        return new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => llm)));
    }

    [Fact]
    public async Task 成功路径_单候选命中并全链路落库()
    {
        var llm = new ScriptedLlm((200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client, new
        {
            gameId = "sokoban-toy",
            targetDifficulty = 1,
            levelNumber = "3",
            specialRequirements = "必须包含一个箱子"
        });

        // 运行结论：单候选通过
        Assert.Equal("succeeded", run.Status);
        Assert.Equal(1, run.AdoptedAttempt);
        Assert.Equal("v1", run.PromptVersion);
        var candidate = Assert.Single(run.Candidates);
        Assert.Equal("none", candidate.FailureClass);
        Assert.Equal(1, candidate.DifficultyBand);
        Assert.NotNull(candidate.SolveRunId);       // 判卷求解落库
        Assert.NotNull(candidate.TapeId);           // 有解候选有磁带（预览用）
        Assert.Equal(1, candidate.LlmCalls);
        Assert.Contains("命中目标", candidate.FailureDetail);

        // 提示词组成（§3.1）：规则书 + 格式说明书 + few-shot + 参数 + 特殊要求
        Assert.Single(llm.RequestBodies);
        var prompt = llm.RequestBodies[0];
        Assert.Contains("推箱子", prompt);                       // 玩法规则书（GameBrief）
        Assert.Contains("grid", prompt);                          // 格式说明书（FormatSpec）
        Assert.Contains("toy-0", prompt);                         // few-shot 历史关示例
        Assert.Contains("目标难度", prompt);
        Assert.Contains("关卡编号：3", prompt);
        Assert.Contains("可用机制", prompt);                      // 机制范围来自 MechanicsTable
        Assert.Contains("必须包含一个箱子", prompt);              // 特殊要求
    }

    [Fact]
    public async Task 坏JSON_修复重试后成功()
    {
        var llm = new ScriptedLlm((200, BadFenced), (200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1 });

        Assert.Equal("succeeded", run.Status);
        var candidate = Assert.Single(run.Candidates);
        Assert.Equal("none", candidate.FailureClass);
        Assert.Equal(2, candidate.LlmCalls);        // 1 次出题 + 1 次修复
        Assert.Equal(2, llm.RequestBodies.Count);
        // 修复请求回喂了校验错误（§3.2）
        Assert.Contains("校验错误", llm.RequestBodies[1]);
        Assert.Contains("未找到 JSON 对象", llm.RequestBodies[1]);
    }

    [Fact]
    public async Task 无解候选_失败反馈回填后重生成功()
    {
        var llm = new ScriptedLlm((200, CornerLevel), (200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1 });

        Assert.Equal("succeeded", run.Status);
        Assert.Equal(2, run.AdoptedAttempt);
        Assert.Equal(2, run.Candidates.Count);
        // 第一个候选：已证无解，反馈只给已验证事实（穷尽证明 + 状态数，不编修复因果）
        Assert.Equal("unsolvable", run.Candidates[0].FailureClass);
        Assert.Contains("未发现目标", run.Candidates[0].FailureDetail);
        Assert.Contains("已访问状态数", run.Candidates[0].FailureDetail);
        // 第二次调用携带失败反馈（§3.3 分类回填）
        Assert.Equal(2, llm.RequestBodies.Count);
        Assert.Contains("未发现目标", llm.RequestBodies[1]);
        Assert.Contains("unsolvable", llm.RequestBodies[1]);
    }

    [Fact]
    public async Task 难度不达标_候选耗尽_如实失败()
    {
        var llm = new ScriptedLlm((200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        // 目标第 3 档，候选只会出第 1 档：单候选耗尽
        var run = await GenerateOkAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 3, candidateLimit = 1 });

        Assert.Equal("failed", run.Status);
        Assert.Null(run.AdoptedAttempt);
        var candidate = Assert.Single(run.Candidates);
        Assert.Equal("difficulty_miss", candidate.FailureClass);
        Assert.Equal(1, candidate.DifficultyBand);        // 评到的档如实记录
        Assert.Contains("未命中目标第 3 档", candidate.FailureDetail);
        // 结论措辞：候选耗尽 ≠ 需求不可满足（§3.3）
        Assert.Contains("候选已耗尽", run.FailureSummary);
        Assert.Contains("未生成符合当前要求", run.FailureSummary);
    }

    [Fact]
    public async Task 格式反复失败_修复重试上限_候选耗尽()
    {
        var llm = new ScriptedLlm(
            (200, BadFenced), (200, BadFenced), (200, BadFenced),
            (200, BadFenced), (200, BadFenced), (200, BadFenced));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 1, candidateLimit = 2 });

        Assert.Equal("failed", run.Status);
        Assert.Equal(2, run.Candidates.Count);
        Assert.All(run.Candidates, c => Assert.Equal("format_invalid", c.FailureClass));
        // 每次尝试 1 次出题 + 2 次修复 = 3 次调用；2 次尝试共 6 次
        Assert.Equal(6, llm.RequestBodies.Count);
        Assert.Contains("候选已耗尽（2/2）", run.FailureSummary);
    }

    [Fact]
    public async Task API失败_立即终止_与内容失败分开()
    {
        var llm = new ScriptedLlm((500, "server exploded"));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 1, candidateLimit = 3 });

        Assert.Equal("failed", run.Status);
        var candidate = Assert.Single(run.Candidates);
        Assert.Equal("api_error", candidate.FailureClass);
        Assert.Contains("HTTP 500", candidate.FailureDetail);
        Assert.Single(llm.RequestBodies);   // 基础设施问题不烧候选预算：只调 1 次
        Assert.Contains("LLM API 调用失败", run.FailureSummary);
    }

    [Fact]
    public async Task 求解预算耗尽_未知不冒充无解()
    {
        var llm = new ScriptedLlm((200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        // 0ms 预算：非初始即胜的关卡必然立即预算耗尽（单候选即收口）
        var run = await GenerateOkAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 1, solveTimeoutMs = 0, candidateLimit = 1 });

        Assert.Equal("failed", run.Status);
        var candidate = Assert.Single(run.Candidates);
        Assert.Equal("resource_limit", candidate.FailureClass);
        Assert.Contains("未得出结论", candidate.FailureDetail);          // 未知，不计作无解
        Assert.Null(candidate.DifficultyBand);                            // 未知不评分
    }

    [Fact]
    public async Task 未配置LLM_明确400()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var (status, body) = await GenerateAsync(client,
            new { gameId = "sokoban-toy", targetDifficulty = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("未配置 LLM", body);
    }

    [Fact]
    public async Task 请求校验_未知玩法与难度越界()
    {
        var llm = new ScriptedLlm((200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var ghost = await GenerateAsync(client, new { gameId = "ghost", targetDifficulty = 1 });
        Assert.Equal(HttpStatusCode.NotFound, ghost.Status);

        var band = await GenerateAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, band.Status);
    }

    [Fact]
    public async Task 候选上限默认来自设置()
    {
        var llm = new ScriptedLlm(
            (200, BadFenced), (200, BadFenced), (200, BadFenced),
            (200, BadFenced), (200, BadFenced), (200, BadFenced));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);
        // 设置页把候选上限调成 2；请求不带 candidateLimit → 用设置值
        var put = await client.PutAsync("/api/settings", new StringContent(
            "{\"gen.candidateLimit\":2}", Encoding.UTF8, "application/json"));
        put.EnsureSuccessStatusCode();

        var run = await GenerateOkAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1 });

        Assert.Equal("failed", run.Status);
        Assert.Equal(2, run.CandidateLimit);
        Assert.Equal(2, run.Candidates.Count);
        Assert.Equal(6, llm.RequestBodies.Count);
    }

    [Fact]
    public async Task 运行历史可回查_候选与采纳信息完整()
    {
        var llm = new ScriptedLlm((200, EasyLevel));
        using var factory = CreateFactory(llm);
        var client = factory.CreateClient();
        await ConfigureLlmAsync(client);

        var run = await GenerateOkAsync(client, new { gameId = "sokoban-toy", targetDifficulty = 1 });

        var raw = await client.GetStringAsync($"/api/generation/runs/{run.RunId}");
        var history = JsonSerializer.Deserialize<Api.GenerationRunDto>(raw, TestJson.Options)!;
        Assert.Equal("succeeded", history.Status);
        Assert.Equal(1, history.AdoptedAttempt);
        Assert.Equal("v1", history.PromptVersion);
        var candidate = Assert.Single(history.Candidates);
        Assert.Equal("none", candidate.FailureClass);
        Assert.NotNull(candidate.TapeId);          // 磁带索引经 solve_run 关联带回
        Assert.Contains("gen-101", candidate.ContentJson);

        // 不存在的运行 404
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/generation/runs/999999")).StatusCode);
    }

    [Fact]
    public void LLM输出JSON提取_围栏与前后缀()
    {
        // markdown 围栏剥离
        Assert.Equal("""{"id":"x"}""", LlmJson.ExtractJson("```json\n{\"id\":\"x\"}\n```"));
        // 前后解释文字宽容截取
        Assert.Equal("""{"a":1}""", LlmJson.ExtractJson("好的，这是：{\"a\":1} 请查收"));
        // 没有 JSON 返回 null（交由校验器判卷，不静默修复）
        Assert.Null(LlmJson.ExtractJson("抱歉我不会出题"));
        Assert.Null(LlmJson.ExtractJson(""));
    }
}
