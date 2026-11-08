using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.AdapterGen;
using LevelForge.Platform.Llm;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 平台内适配层生成（三轮 D9 解冻）：后台任务 + 编译回喂循环 + 自动门1 + adapterReady 翻转
// 替身模型按调用次序返回脚本内容——不验证模型本身，验证平台的轮次控制/编译回喂/装载链路
public sealed class AdapterGenTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string MakeCocosProject(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-agen-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        var levels = Path.Combine(root, "assets", "levels");
        Directory.CreateDirectory(levels);
        for (var i = 1; i <= 3; i++)
        {
            File.WriteAllText(Path.Combine(levels, i + ".json"),
                $$"""{"levelId":{{i}},"moves":20,"board":{"width":8,"height":8},"tiles":[{"type":"red","x":1,"y":1}]}""");
        }
        File.WriteAllText(Path.Combine(root, "package.json"), """{"creator":"3.8"}""");
        return root;
    }

    // 按调用次序返回不同 content 的替身模型
    private sealed class SequencedLlm(string[] contents) : HttpMessageHandler
    {
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = _index < contents.Length ? contents[Math.Min(_index++, contents.Length - 1)] : contents[^1];
            var payload = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content } } },
                usage = new { prompt_tokens = 10, completion_tokens = 10 },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static WebApplicationFactory<Program> FactoryWithLlm(string[] contents) =>
        new PlatformFactory().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => new SequencedLlm(contents))));

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        return await client.PostAsync(url, new StringContent(
            JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"));
    }

    // 产出能通过编译且过门1的最小门面源文件（GameId 按导入目录名 slug 动态绑定）
    // 输出格式与运行器契约一致：FILE 标记切分（c1289e3 起运行器只认标记，不再收 JSON files 数组）
    private static string FacadeFiles(string gameId) => """
        // ==== FILE: __GAMEID__Facade.cs ====
        using LevelForge.Kernel.Analysis;
        using LevelForge.Kernel.Contracts;
        using LevelForge.Kernel.Hosting;
        using LevelForge.Kernel.Replay;
        using LevelForge.Platform.Facades;
        using System.Text.Json.Nodes;

        namespace Gen;

        public sealed class GenFacade : IGameFacade
        {
            public GameRegistration Registration => new(
                "__GAMEID__", "生成的测试玩法", "trial", "1.0.0",
                AdapterFingerprint.Of(typeof(GenFacade).Assembly),
                TapeConstants.KernelVersion, TapeConstants.ContractVersion,
                "levels/gen", false, "{}", "{}", "生成玩法", "{}", "", "{}");

            public IReadOnlyList<string> BuiltinLevels => Array.Empty<string>();

            public string? TryGetLevelId(string levelJson)
            {
                try { return JsonNode.Parse(levelJson)?["levelId"]?.ToString(); }
                catch (System.Text.Json.JsonException) { return null; }
            }

            public IReadOnlyList<string> FormatErrors(string levelJson) => Array.Empty<string>();

            public PlatformSolve Solve(SolveCommand command)
            {
                var result = new SolveResult(SolveStatus.Invalid, Optimality.NotApplicable, false,
                    TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix, [], null, "生成测试");
                var difficulty = new DifficultyAssessment(0, 0, "gen-v1", [], []);
                return new PlatformSolve(result, null, difficulty, null);
            }

            public ComplianceReport RunGate1Checks()
            {
                return new ComplianceReport(
                    ComplianceResult.Pass(ComplianceCheckKind.DependencyScan, ["生成测试"]),
                    ComplianceResult.Pass(ComplianceCheckKind.StateKeyDiscrimination),
                    ComplianceResult.Pass(ComplianceCheckKind.Purity),
                    ComplianceResult.Pass(ComplianceCheckKind.Determinism),
                    ComplianceResult.Pass(ComplianceCheckKind.KernelSelfTest));
            }

            public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
                => throw new PlayException("生成测试不实现试玩");
        }
        // ==== END FILE ====
        """.Replace("__GAMEID__", gameId);

    // 第一轮坏代码（编译错误：缺少 usings 与接口实现）——同样走 FILE 标记格式
    private static string BrokenFiles() => """
        // ==== FILE: Broken.cs ====
        this is not valid c#;
        // ==== END FILE ====
        """;

    [Fact]
    public async Task 平台内生成_未配置模型_给可操作错误()
    {
        var projectDir = MakeCocosProject("Unconfigured");
        using var factory = new PlatformFactory();   // 无 LLM 配置
        var client = factory.CreateClient();
        try
        {
            await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            var start = await PostJsonAsync(client, "/api/games/unconfigured/adapter-gen", new { });
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            var jobId = JsonSerializer.Deserialize<JsonElement>(await start.Content.ReadAsStringAsync()).GetProperty("jobId").GetInt64();

            // 轮询至终态（后台任务发现未配置模型即失败）
            string? error = null;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(300);
                var snap = await client.GetStringAsync($"/api/adapter-gen/{jobId}");
                var doc = JsonSerializer.Deserialize<JsonElement>(snap);
                if (doc.GetProperty("status").GetString() != "running")
                {
                    error = doc.GetProperty("error").GetString();
                    break;
                }
            }
            Assert.Contains("平台内生成需要模型", error ?? "");
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 平台内生成提示词_包含契约与内核装配要求()
    {
        // 路径 A 的提示词必须与路径 B 对齐：契约/制带/门1/五维语义缺一不可
        var prompt = AdapterGenPrompt.BuildSystemPrompt("piggyfarm", @"D:\bin", @"D:\ws", "piggyfarm.csproj");
        Assert.Contains("ReplayEngine.RecordTape", prompt);
        Assert.Contains("AdapterHosting", prompt);
        Assert.Contains("RunGate1Checks", prompt);
        Assert.Contains("ComplianceReport", prompt);
        Assert.Contains("ResourceLimit", prompt);
        Assert.Contains("HostingCheckInput", prompt);
        Assert.Contains("不要尝试读取", prompt);   // 纠正"可读取参考源码"的误导（模型无法读平台目录）
    }

    [Fact]
    public void 契约速查_枚举成员与构造器参数来自真实程序集()
    {
        // 实测路径 A 的编译错误集中在"猜枚举成员/少传构造参数"；契约速查必须列出真实成员与参数
        var reference = AdapterGenContractReference.Build();
        Assert.Contains("UnsolvableProven", reference);          // SolveStatus 真实成员（模型曾猜 Unknown）
        Assert.Contains("NotProven", reference);                 // Optimality 真实成员（模型曾猜 Optimal）
        Assert.Contains("GoalFound", reference);                 // TerminationReason 真实成员（模型曾猜 GoalReached）
        Assert.Contains("ExpandedPrefix", reference);            // MetricsScope 真实成员（模型曾猜 Search）
        Assert.Contains("IExecutionProtocol", reference);        // 适配器必须同时实现执行协议
        Assert.Contains("SceneSnapshot(BoardSize", reference);   // 少传 Cells 的坑
        Assert.Contains("PlayState(SceneSnapshot", reference);   // 少传 MovesExecuted 的坑
    }

    [Fact]
    public async Task 平台内生成_四轮正文为空_最终错误不得为空()
    {
        var projectDir = MakeCocosProject("gen-empty");
        var gameId = "gen-empty";
        using var factory = FactoryWithLlm(["", "", "", ""]);
        var client = factory.CreateClient();
        try
        {
            await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            await client.PutAsync("/api/settings", new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, JsonElement>
                {
                    ["llm.baseUrl"] = JsonSerializer.SerializeToElement("http://llm.test"),
                    ["llm.model"] = JsonSerializer.SerializeToElement("test-model"),
                    ["llm.apiKey"] = JsonSerializer.SerializeToElement("k"),
                }, Json), Encoding.UTF8, "application/json"));

            var start = await PostJsonAsync(client, $"/api/games/{gameId}/adapter-gen", new { });
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            var jobId = JsonSerializer.Deserialize<JsonElement>(await start.Content.ReadAsStringAsync()).GetProperty("jobId").GetInt64();

            JsonElement? final = null;
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(300);
                var snap = JsonSerializer.Deserialize<JsonElement>(await client.GetStringAsync($"/api/adapter-gen/{jobId}"));
                if (snap.GetProperty("status").GetString() != "running") { final = snap; break; }
            }
            Assert.True(final.HasValue, "空正文任务未在 9s 内收口");
            Assert.Equal("failed", final!.Value.GetProperty("status").GetString());
            var error = final.Value.GetProperty("error").GetString() ?? "";
            // 此前四轮空正文时 lastError 为空，界面只剩"最近问题："后面空白；现在必须带具体原因
            Assert.Contains("正文为空", error);
            var after = error[(error.IndexOf("最近问题：", StringComparison.Ordinal) + "最近问题：".Length)..].Trim();
            Assert.False(string.IsNullOrWhiteSpace(after), "最近问题不得为空：" + error);
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 平台内生成_编译错误回喂后第二轮成功_门1绿且adapterReady翻转()
    {
        var projectDir = MakeCocosProject("gen-ok");
        var gameId = "gen-ok";
        using var factory = FactoryWithLlm([BrokenFiles(), FacadeFiles(gameId)]);
        var client = factory.CreateClient();
        try
        {
            await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            // 写入 LLM 连接配置（替身模型需要平台认为"已配置"）
            await client.PutAsync("/api/settings", new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, JsonElement>
                {
                    ["llm.baseUrl"] = JsonSerializer.SerializeToElement("http://llm.test"),
                    ["llm.model"] = JsonSerializer.SerializeToElement("test-model"),
                    ["llm.apiKey"] = JsonSerializer.SerializeToElement("k"),
                }, Json), Encoding.UTF8, "application/json"));

            var start = await PostJsonAsync(client, $"/api/games/{gameId}/adapter-gen", new { });
            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            var jobId = JsonSerializer.Deserialize<JsonElement>(await start.Content.ReadAsStringAsync()).GetProperty("jobId").GetInt64();

            // 轮询至终态（含一次编译失败回喂 + 第二轮编译成功 + 门1）
            JsonElement? final = null;
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                var snap = JsonSerializer.Deserialize<JsonElement>(await client.GetStringAsync($"/api/adapter-gen/{jobId}"));
                if (snap.GetProperty("status").GetString() != "running") { final = snap; break; }
            }
            Assert.True(final.HasValue, "生成任务未在 60s 内收口");
            Assert.True(final!.Value.GetProperty("status").GetString() == "succeeded",
                "生成失败：status=" + final.Value.GetProperty("status").GetString()
                + "，error=" + (final.Value.TryGetProperty("error", out var errEl) ? errEl.GetString() : "")
                + "，rounds=" + (final.Value.TryGetProperty("report", out var repEl) && repEl.ValueKind == JsonValueKind.Object ? repEl.GetProperty("rounds").GetInt32().ToString() : "?"));
            Assert.Equal(2, final.Value.GetProperty("report").GetProperty("rounds").GetInt32());
            Assert.True(final.Value.GetProperty("report").GetProperty("gate1Passed").GetBoolean());

            // adapterReady 翻转
            var games = JsonSerializer.Deserialize<List<Api.GameSummaryDto>>(
                await client.GetStringAsync("/api/games"), Json)!;
            Assert.True(games.Single(g => g.GameId == gameId).AdapterReady);
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 平台内生成_已接入玩法_409拒绝重复生成()
    {
        var projectDir = MakeCocosProject("test-plugin");
        using var factory = FactoryWithLlm(["{}"]);
        var client = factory.CreateClient();
        try
        {
            await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            // 先经上传+检测注册（借用测试插件 DLL）
            using (var form = new MultipartFormDataContent())
            using (var dll = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "LevelForge.TestPlugin.dll")))
            {
                var content = new StreamContent(dll);
                form.Add(content, "file", "LevelForge.TestPlugin.dll");
                await client.PostAsync("/api/games/test-plugin/plugins/upload", form);
            }
            var detect = await PostJsonAsync(client, "/api/games/test-plugin/plugins/detect", new { });
            Assert.Equal(HttpStatusCode.OK, detect.StatusCode);

            // 已接入再生成：409
            var gen = await PostJsonAsync(client, "/api/games/test-plugin/adapter-gen", new { });
            Assert.Equal(HttpStatusCode.Conflict, gen.StatusCode);
            Assert.Contains("已接入", await gen.Content.ReadAsStringAsync());
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }
}
