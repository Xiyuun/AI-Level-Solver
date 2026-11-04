using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Tests;

// 项目导入（三轮 N10/N11）：引擎识别、slug 命名、导入落库、facadeless 护栏、重复导入幂等
public sealed class ProjectImportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 建一个假的 Unity 项目目录（Assets/ + ProjectSettings/ProjectVersion.txt）
    private static string MakeUnityProject(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 2022.3.10f1");
        return root;
    }

    // 建一个假的 Cocos Creator 3.x 项目目录（package.json 带 creator 字段）
    private static string MakeCocosProject(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "package.json"), """{"name":"demo","creator":{"version":"3.8.0"}}""");
        return root;
    }

    #region 引擎识别（纯逻辑）

    [Theory]
    [InlineData("unity")]
    [InlineData("cocos")]
    public void 引擎识别_两种项目都能认出(string kind)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lf-imp-" + kind + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (kind == "unity") MakeUnityProject(dir); else MakeCocosProject(dir);
            var (engine, evidence) = ProjectImporter.DetectEngine(dir);
            Assert.NotNull(engine);
            if (kind == "unity")
            {
                Assert.Equal("unity", engine);
            }
            else
            {
                Assert.StartsWith("cocos", engine);
            }
            Assert.False(string.IsNullOrWhiteSpace(evidence));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 引擎识别_普通目录给依据并返回空()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lf-imp-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a game project");
        try
        {
            var (engine, evidence) = ProjectImporter.DetectEngine(dir);
            Assert.Null(engine);
            Assert.Contains("readme.txt", evidence);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }



    [Fact]
    public void Slug_目录名转合法标识()
    {
        Assert.Equal("piggyfarm", ProjectImporter.Slugify("PiggyFarm"));
        Assert.Equal("my-game-v1-2", ProjectImporter.Slugify("My Game v1.2"));
        Assert.StartsWith("project-", ProjectImporter.Slugify("我的农场"));   // 纯中文折叠为空 → 时间戳回退
    }

    #endregion

    #region 导入 API（HTTP 全链路）

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, Json);
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task 导入_Unity项目_落库为接入中玩法并种六步任务()
    {
        var projectDir = MakeUnityProject(Path.Combine(Path.GetTempPath(), "lf-proj-" + Guid.NewGuid().ToString("N")));
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        try
        {
            var resp = await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var raw = await resp.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(raw, Json)!;
            Assert.Equal(ProjectImporter.Slugify(Path.GetFileName(projectDir)), result.GameId);   // slug 化自目录名
            Assert.False(result.AlreadyImported);
            Assert.Equal("unity", result.EngineType);
            Assert.Equal(projectDir, result.ProjectRoot);

            // games 列表：AdapterReady=false（未接入）、状态 onboarding
            var games = await client.GetStringAsync("/api/games");
            var list = JsonSerializer.Deserialize<List<Api.GameSummaryDto>>(games, Json)!;
            var imported = list.Single(g => g.GameId == result.GameId);
            Assert.False(imported.AdapterReady);
            Assert.Equal("onboarding", imported.Status);
            Assert.Equal("unity", imported.EngineType);

            // 六步任务已种子（全 todo）
            var tasks = JsonSerializer.Deserialize<List<Api.OnboardingTaskDto>>(
                await client.GetStringAsync($"/api/games/{result.GameId}/onboarding"), Json)!;
            Assert.Equal(6, tasks.Count);
            Assert.All(tasks, t => Assert.Equal("todo", t.Status));

            // 同目录重复导入：幂等返回
            var again = await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir + "/" });
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Contains("\"alreadyImported\":true", await again.Content.ReadAsStringAsync());
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 导入_目录名撞名_返回冲突()
    {
        var parent1 = Path.Combine(Path.GetTempPath(), "lf-dup-a-" + Guid.NewGuid().ToString("N"));
        var parent2 = Path.Combine(Path.GetTempPath(), "lf-dup-b-" + Guid.NewGuid().ToString("N"));
        var dir1 = MakeUnityProject(Path.Combine(parent1, "SameName"));
        var dir2 = MakeCocosProject(Path.Combine(parent2, "SameName"));   // 同名目录（不同父路径）→ slug 相同
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(client, "/api/projects/import", new { directory = dir1 })).StatusCode);
            var second = await PostJsonAsync(client, "/api/projects/import", new { directory = dir2 });
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Contains("撞名", await second.Content.ReadAsStringAsync());
        }
        finally
        {
            try { Directory.Delete(parent1, true); } catch (IOException) { }
            try { Directory.Delete(parent2, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 导入_无法识别的目录_给可操作的错误()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lf-noengine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "todo.txt"), "x");
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        try
        {
            var resp = await PostJsonAsync(client, "/api/projects/import", new { directory = dir });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("无法识别", body);
            Assert.Contains("todo.txt", body);   // 列出目录内容帮助对照
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 未接入护栏_生成求解采纳批量都给可操作提示()
    {
        var projectDir = MakeUnityProject(Path.Combine(Path.GetTempPath(), "lf-guard-" + Guid.NewGuid().ToString("N")));
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        try
        {
            var import = await PostJsonAsync(client, "/api/projects/import", new { directory = projectDir });
            var result = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(
                await import.Content.ReadAsStringAsync(), Json)!;
            var gameId = result.GameId;

            // 生成（异步入口）：409 + 适配层提示
            var gen = await PostJsonAsync(client, "/api/generate/jobs", new { gameId, targetDifficulty = 2 });
            Assert.Equal(HttpStatusCode.Conflict, gen.StatusCode);
            Assert.Contains("适配层尚未接入", await gen.Content.ReadAsStringAsync());

            // 求解（按编号）：409
            var solve = await PostJsonAsync(client, "/api/solve", new { gameId, levelId = "1" });
            Assert.Equal(HttpStatusCode.Conflict, solve.StatusCode);

            // 采纳：409
            var adopt = await PostJsonAsync(client, "/api/adopt", new { gameId, levelJson = "{}", source = "generated" });
            Assert.Equal(HttpStatusCode.Conflict, adopt.StatusCode);

            // 批量：409
            var batch = await PostJsonAsync(client, "/api/batch/start", new { gameId, fromLevel = 1, toLevel = 2, difficulty1Count = 2 });
            Assert.Equal(HttpStatusCode.Conflict, batch.StatusCode);

            // 关卡导入：409
            var levels = await PostJsonAsync(client, $"/api/games/{gameId}/import-levels", new { directory = projectDir });
            Assert.Equal(HttpStatusCode.Conflict, levels.StatusCode);

            // 门1：400 + 明确提示（门服务以 GateException 口径返回）
            var gate1 = await PostJsonAsync(client, $"/api/games/{gameId}/gates/1/run", new { });
            var gate1Body = await gate1.Content.ReadAsStringAsync();
            Assert.Contains("适配层尚未接入", gate1Body);

            // 未知玩法仍是 404（护栏不吞掉真正的未知）
            var unknown = await PostJsonAsync(client, "/api/solve", new { gameId = "no-such-game", levelId = "1" });
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }

    #endregion
}
