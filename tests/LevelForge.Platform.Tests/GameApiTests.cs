using System.Net;
using System.Text;
using System.Text.Json;

namespace LevelForge.Platform.Tests;

// 玩法 API：列表/详情/切换/关卡列表/短语 CRUD（HTTP 全链路，含种子生效验证）
public sealed class GameApiTests
{
    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, TestJson.Options);
        return await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task 玩法列表_含内置玩具且元数据正确()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = await client.GetStringAsync("/api/games");

        // JSON 口径：camelCase 字段（前端契约）
        Assert.Contains("\"gameId\":\"sokoban-toy\"", raw);
        var list = JsonSerializer.Deserialize<List<Api.GameSummaryDto>>(raw, TestJson.Options);
        var toy = Assert.Single(list!);
        Assert.Equal("玩具推箱子（内置参考）", toy.DisplayName);
        Assert.Equal("production", toy.Status);
        Assert.Equal("生产中", toy.StatusName);
        Assert.Equal(10, toy.LevelCount);
        Assert.False(toy.Gate2Applicable);

        // 数据目录隔离生效：db 文件确实落在测试临时目录（UseSetting 注入成功的证据）
        Assert.True(File.Exists(factory.Database.DbPath));
    }

    [Fact]
    public async Task 玩法详情_含全部接入产物()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = await client.GetStringAsync("/api/games/sokoban-toy");
        var detail = JsonSerializer.Deserialize<Api.GameDetailDto>(raw, TestJson.Options)!;

        Assert.Equal("1.0.0", detail.AdapterVer);
        Assert.StartsWith("sha256:", detail.AdapterHash);
        Assert.NotNull(detail.VisualMapping?["entityTypes"]?["box"]);   // 视觉映射是解析后的对象
        Assert.NotNull(detail.Mechanics?["ranges"]);
        Assert.NotNull(detail.DifficultyWeights?["thresholds"]);
        Assert.Contains("推", detail.GameBrief);
        Assert.Contains("grid", detail.FormatSpec);
        Assert.Contains("规则矩阵", detail.RuleMatrix);
        Assert.Equal("levels/sokoban-toy", detail.LevelsRoot);
    }

    [Fact]
    public async Task 未知玩法_404()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/games/ghost")).StatusCode);
    }

    [Fact]
    public async Task 切换玩法_持久化生效()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var put = await client.PutAsync("/api/games/sokoban-toy/activate", null);
        put.EnsureSuccessStatusCode();
        var active = JsonSerializer.Deserialize<Api.ActiveGameDto>(
            await client.GetStringAsync("/api/games/active"), TestJson.Options)!;
        Assert.Equal("sokoban-toy", active.GameId);

        // 未知玩法切换被拒
        var ghost = await client.PutAsync("/api/games/ghost/activate", null);
        Assert.Equal(HttpStatusCode.NotFound, ghost.StatusCode);
    }

    [Fact]
    public async Task 关卡列表_内置十关()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var raw = await client.GetStringAsync("/api/games/sokoban-toy/levels");
        var levels = JsonSerializer.Deserialize<List<Api.LevelDto>>(raw, TestJson.Options)!;
        Assert.Equal(10, levels.Count);
        Assert.Equal("toy-001", levels[0].LevelId);
        Assert.All(levels, l =>
        {
            Assert.Equal("builtin", l.Source);
            Assert.StartsWith("sha256:", l.LevelHash);
        });
    }

    [Fact]
    public async Task 短语增删查()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 初始为空
        var listRaw = await client.GetStringAsync("/api/games/sokoban-toy/phrases");
        Assert.Equal("[]", listRaw.Trim());

        // 新增
        var post = await PostJsonAsync(client, "/api/games/sokoban-toy/phrases",
            new Api.PhraseCreateDto("必须包含 3 只朝下的猪"));
        post.EnsureSuccessStatusCode();
        var created = JsonSerializer.Deserialize<Api.PhraseDto>(
            await post.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.True(created.Id > 0);
        Assert.Equal(0, created.SortOrder);

        // 空内容拒绝
        var empty = await PostJsonAsync(client, "/api/games/sokoban-toy/phrases", new Api.PhraseCreateDto("  "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // 列表可见 → 删除 → 列表空 → 再删 404
        listRaw = await client.GetStringAsync("/api/games/sokoban-toy/phrases");
        Assert.Contains("朝下的猪", listRaw);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/phrases/{created.Id}")).StatusCode);
        Assert.Equal("[]", (await client.GetStringAsync("/api/games/sokoban-toy/phrases")).Trim());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.DeleteAsync($"/api/phrases/{created.Id}")).StatusCode);
    }
}
