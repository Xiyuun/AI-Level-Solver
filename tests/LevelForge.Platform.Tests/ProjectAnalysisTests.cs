using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LevelForge.Platform.Tests;

// 深度分析（三轮 N12 完整版 B）：扫描器全量 schema 聚合 + 平台内 LLM 分析端点（替身模型）
public sealed class ProjectAnalysisTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string MakeCocosWithLevels(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "assets", "scripts"));
        var levels = Path.Combine(root, "assets", "resources", "LevelData");
        Directory.CreateDirectory(levels);
        for (var i = 1; i <= 5; i++)
        {
            File.WriteAllText(Path.Combine(levels, i + ".json"),
                $$"""{"levelId":{{i}},"level":{{i}},"difficulty":{{i % 3 + 1}},"chessCount":{{100 + i}},"chesses":[{"type":"normal","x":1,"y":2}]}""");
        }
        File.WriteAllText(Path.Combine(root, "package.json"), """{"name":"demo","creator":{"version":"3.8.1"}}""");
        return root;
    }

    // 替身模型：OpenAI 响应壳内嵌固定分析 JSON（验证平台解析/落库链路，不验证模型本身）
    private sealed class ScriptedLlm(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
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

    private static WebApplicationFactory<Program> FactoryWithLlm(HttpMessageHandler llm) =>
        new PlatformFactory().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddHttpClient<LlmClient>().ConfigurePrimaryHttpMessageHandler(() => llm)));

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        return await client.PostAsync(url, new StringContent(
            JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"));
    }

    [Fact]
    public void 扫描器_全量schema聚合_共键判别值数值范围()
    {
        var root = MakeCocosWithLevels(Path.Combine(Path.GetTempPath(), "lf-schema-" + Guid.NewGuid().ToString("N")));
        try
        {
            var profile = ProjectScanner.Scan(root, "cocos-creator3");
            var schema = profile.SchemaSummaries!.FirstOrDefault(s => s.Directory == "assets/resources/LevelData");
            Assert.NotNull(schema);
            Assert.Equal(5, schema!.FileCount);
            Assert.Equal(5, schema.ParsedCount);
            Assert.Contains("levelId", schema.CommonKeys);
            Assert.Contains("chessCount", schema.CommonKeys);
            Assert.Contains("chesses[]:x", schema.NestedKeys);
            Assert.True(schema.NestedKeys.Contains("chesses[]:y"));
            Assert.Contains("normal", schema.EntityTypes);   // 判别键的取值清单（设计如此：type 不进 NestedKeys）
            Assert.Contains("level:1~5", schema.NumericRanges);
            Assert.Contains("chessCount:101~105", schema.NumericRanges);
            Assert.Empty(schema.Anomalies);
            // 口径：文件总数与抽样解析数分开说，不能把"抽样"说成"全量"
            Assert.Contains(profile.Observations, o => o.Contains("共 5 个 JSON") && o.Contains("抽样 5 个"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 扫描器_深层编辑源关卡目录_压过工具配置JSON()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-unity-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Unity 项目真实形态：编辑源关卡在 LevelEditor/Luban~/Datas/Levels（相对深度 6）
            var levels = Path.Combine(root, "Assets", "Main", "LevelEditor", "Luban~", "Datas", "Levels");
            Directory.CreateDirectory(levels);
            for (var i = 1; i <= 6; i++)
            {
                File.WriteAllText(Path.Combine(levels, $"{i:000000}.json"),
                    $$"""{"levelId":{{i}},"difficulty":1,"levelName":"n","mapWidth":12,"mapHeight":19,"pigCount":1,"pigs":[{"id":0,"facing":"Left","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":1,"y":1},{"x":2,"y":1}]}]}""");
            }

            // 干扰项：工具配置 JSON 数量更多，但字段不是关卡（runtimeOptions/compilerOptions）
            var tools = Path.Combine(root, "Tools", "Luban");
            Directory.CreateDirectory(tools);
            for (var i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(tools, $"tool{i}.json"), """{"runtimeOptions":{"tfm":"net8.0"}}""");
            }
            var vscode = Path.Combine(root, ".vscode");
            Directory.CreateDirectory(vscode);
            File.WriteAllText(Path.Combine(vscode, "a.json"), """{"version":"0.2.0","configurations":[]}""");

            var profile = ProjectScanner.Scan(root, "unity");
            var top = profile.LevelDirCandidates[0];
            Assert.Equal("Assets/Main/LevelEditor/Luban~/Datas/Levels", top.Directory);
            Assert.Equal(6, top.JsonCount);
            Assert.Equal("numbered", top.NamingHint);
            Assert.DoesNotContain(profile.LevelDirCandidates, c => c.Directory == ".vscode");
            // 抽样必须来自真关卡：顶层键含 levelId（而不是 runtimeOptions）
            Assert.Contains(profile.SampleFiles, s => s.Parsed && s.TopLevelKeys!.Contains("levelId"));
            // 第一名候选的小文件带原文预览（需求包直接引用，外部助手少读一次文件）
            Assert.Contains(profile.SampleFiles, s => s.Preview is not null && s.Preview.Contains("levelId"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 扫描器_多套关卡管线_观察里如实提示()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-multi-" + Guid.NewGuid().ToString("N"));
        try
        {
            static void WriteLevels(string dir, Func<int, string> json)
            {
                Directory.CreateDirectory(dir);
                for (var i = 1; i <= 5; i++)
                {
                    File.WriteAllText(Path.Combine(dir, $"{i:000000}.json"), json(i));
                }
            }
            // 两套不同 schema 的关卡管线（模拟 piggyfarm 的主线 499 / 框架 110）
            WriteLevels(Path.Combine(root, "Assets", "Main", "LevelEditor", "Luban~", "Datas", "Levels"),
                i => $$"""{"levelId":{{i}},"mapWidth":12,"mapHeight":19,"pigs":[{"id":0}]}""");
            WriteLevels(Path.Combine(root, "Assets", "Main", "FrameWorkExtras", "LevelData", "Luban~", "Datas", "Levels"),
                i => $$"""{"levelId":{{i}},"chessCount":{{100 + i}},"chesses":[{"type":"normal"}]}""");

            var profile = ProjectScanner.Scan(root, "unity");
            Assert.True(profile.LevelDirCandidates.Count >= 2);
            Assert.Contains(profile.Observations, o =>
                o.Contains("多套关卡数据管线") && o.Contains("LevelEditor") && o.Contains("FrameWorkExtras"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 分析端点_未配置模型_给可操作错误()
    {
        var projectDir = MakeCocosWithLevels(Path.Combine(Path.GetTempPath(), "lf-na-" + Guid.NewGuid().ToString("N")));
        using var factory = new PlatformFactory();   // 默认无 LLM 配置
        var client = factory.CreateClient();
        try
        {
            var import = await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            var imported = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(await import.Content.ReadAsStringAsync(), Json)!;
            var resp = await PostJsonAsync(client, $"/api/games/{imported.GameId}/analyze", new { });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Contains("深度分析需要模型", await resp.Content.ReadAsStringAsync());
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 分析端点_替身模型_产出初稿并落库()
    {
        var projectDir = MakeCocosWithLevels(Path.Combine(Path.GetTempPath(), "lf-an-" + Guid.NewGuid().ToString("N")));
        var analysisJson = JsonSerializer.Serialize(new
        {
            fourQuestions = new[]
            {
                new { question = "回合制", answer = "yes", evidence = "assets/scripts/main.ts:10" },
                new { question = "确定性", answer = "yes", evidence = "无随机源" },
                new { question = "状态可枚举", answer = "yes", evidence = "关卡全 JSON" },
                new { question = "持续产关", answer = "yes", evidence = "LevelData/ 目录" },
            },
            mechanics = new[] { "消除：相邻同色牌配对消除" },
            checklistDraft = new[] { new { group = "A", text = "消除判定与本体一致" } },
            risks = new[] { "countdown 字段语义未明" },
            hygieneGrade = "良好（规则状态集中在 LevelData，与场景弱耦合）",
            workEstimate = "0.5~1 人日：数据外置且状态可序列化",
            summary = "接入难度低",
        }, Json);
        using var factory = FactoryWithLlm(new ScriptedLlm(analysisJson));
        var client = factory.CreateClient();
        try
        {
            var import = await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            var imported = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(await import.Content.ReadAsStringAsync(), Json)!;
            var gameId = imported.GameId;

            // 配置替身模型的连接信息（写入测试库 settings）
            await client.PutAsync("/api/settings", new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, JsonElement>
                {
                    ["llm.baseUrl"] = JsonSerializer.SerializeToElement("http://llm.test"),
                    ["llm.model"] = JsonSerializer.SerializeToElement("test-model"),
                    ["llm.apiKey"] = JsonSerializer.SerializeToElement("k"),
                }, Json), Encoding.UTF8, "application/json"));

            var resp = await PostJsonAsync(client, $"/api/games/{gameId}/analyze", new { });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var dto = JsonSerializer.Deserialize<Api.ProjectAnalysisDto>(await resp.Content.ReadAsStringAsync(), Json)!;
            Assert.Equal(4, dto.FourQuestions.Count);
            Assert.Equal("test-model", dto.Model);
            Assert.Single(dto.ChecklistDraft);
            Assert.Contains("良好", dto.HygieneGrade);
            Assert.Contains("0.5~1 人日", dto.WorkEstimate);

            // 落库：玩法详情带分析结论
            var detail = JsonSerializer.Deserialize<Api.GameDetailDto>(
                await client.GetStringAsync($"/api/games/{gameId}"), Json)!;
            Assert.NotNull(detail.ProjectAnalysis);
            var draft = detail.ProjectAnalysis!["checklistDraft"]!.AsArray()
                .Select(n => n!["text"]!.GetValue<string>()).ToList();
            Assert.Contains("消除判定与本体一致", draft);   // 结构化断言：JsonNode.ToJsonString 会把中文转义成 \uXXXX，不能用字符串 Contains
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }
}
