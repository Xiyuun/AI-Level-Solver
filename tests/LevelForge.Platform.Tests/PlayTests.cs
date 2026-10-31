using System.Net;
using System.Text;
using System.Text.Json;

namespace LevelForge.Platform.Tests;

// 试玩步进 API：开局/步进/胜利判定/非法动作拒绝（试玩与求解同一份适配层——渲染规范 §6）
public sealed class PlayTests
{
    private const string EasyLevel = """{"id":"play-1","grid":["######","#@$ .#","#    #","######"]}""";

    private static async Task<(HttpStatusCode Status, string Body)> PostAsync(HttpClient client, string url, object body)
    {
        var resp = await client.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Api.PlayStateDto> PostOkAsync(HttpClient client, string url, object body)
    {
        var (status, raw) = await PostAsync(client, url, body);
        Assert.True(status == HttpStatusCode.OK, $"应返回 200，实际 {status}：{raw}");
        return JsonSerializer.Deserialize<Api.PlayStateDto>(raw, TestJson.Options)!;
    }

    [Fact]
    public async Task 开局_返回初始快照与合法动作()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var state = await PostOkAsync(client, "/api/play/start",
            new { gameId = "sokoban-toy", levelJson = EasyLevel });

        // 初始局面：玩家与箱子在场，未通关
        Assert.Equal(0, state.MovesExecuted);
        Assert.False(state.IsGoal);
        Assert.Equal(2, state.Snapshot!["entities"]!.AsArray().Count);
        // 合法动作含"右移（推箱）"与"下移"（走位），动作编码为结构化 JSON
        var pushRight = state.LegalMoves.Single(m => m.Move!.ToJsonString() == """{"dir":"right"}""");
        Assert.Contains("推箱", pushRight.MoveText);
        Assert.Contains(state.LegalMoves, m => m.MoveText == "下移");
    }

    [Fact]
    public async Task 步进_推到目标判胜()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 两次右推：箱到位 → 通关
        var state = await PostOkAsync(client, "/api/play/step", new
        {
            gameId = "sokoban-toy",
            levelJson = EasyLevel,
            moves = new[] { new { dir = "right" }, new { dir = "right" } }
        });
        Assert.Equal(2, state.MovesExecuted);
        Assert.True(state.IsGoal);   // 胜利判定来自适配层（与游戏本体判定条件逐字一致）
    }

    [Fact]
    public async Task 非法动作_400且不推进()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 撞墙方向（上）不合法
        var (status, body) = await PostAsync(client, "/api/play/step", new
        {
            gameId = "sokoban-toy",
            levelJson = EasyLevel,
            moves = new[] { new { dir = "up" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("不合法", body);

        // 未知编码同样拒绝
        var (encStatus, encBody) = await PostAsync(client, "/api/play/step", new
        {
            gameId = "sokoban-toy",
            levelJson = EasyLevel,
            moves = new object[] { new { dir = "teleport" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, encStatus);
        Assert.Contains("非法", encBody);
    }

    [Fact]
    public async Task 非法关卡与未知玩法_被拒()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var bad = await PostAsync(client, "/api/play/start",
            new { gameId = "sokoban-toy", levelJson = "{oops" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);

        var ghost = await PostAsync(client, "/api/play/start",
            new { gameId = "ghost", levelJson = EasyLevel });
        Assert.Equal(HttpStatusCode.NotFound, ghost.Status);
    }

    [Fact]
    public async Task 已知无解关_可走动但永不判胜()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // toy-010（角落箱）：合法走动存在，执行若干步不会误报通关
        var state = await PostOkAsync(client, "/api/play/step", new
        {
            gameId = "sokoban-toy",
            levelJson = """{"id":"toy-010","grid":["#####","#$  #","# . #","# @ #","#####"]}""",
            moves = new[] { new { dir = "right" }, new { dir = "left" }, new { dir = "right" } }
        });
        Assert.Equal(3, state.MovesExecuted);
        Assert.False(state.IsGoal);
    }
}
