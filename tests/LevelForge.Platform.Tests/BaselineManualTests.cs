using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LevelForge.Platform.Tests;

// ⑥基准管理：状态查询 + 人工签认建立基准（门2不适用时的降级路径）
public sealed class BaselineManualTests
{
    [Fact]
    public async Task 基准状态_初始为0_人工签认后全部建立_重复签认幂等()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var before = await GetStatus(client);
        Assert.Equal(10, before.GetProperty("total").GetInt32());
        Assert.Equal(0, before.GetProperty("confirmed").GetInt32());
        Assert.Equal(10, before.GetProperty("missing").GetInt32());

        var body = JsonSerializer.Serialize(new
        {
            confirmedBy = "测试确认人",
            note = "逐关在游戏本体执行通过（测试数据）",
            levelIds = Array.Empty<string>(),
        });
        var resp = await client.PostAsync("/api/games/sokoban-toy/baselines/manual",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(10, result.GetProperty("created").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());

        var after = await GetStatus(client);
        Assert.Equal(10, after.GetProperty("confirmed").GetInt32());
        Assert.Equal(0, after.GetProperty("missing").GetInt32());

        // 重复签认：已有基准全部跳过，不重复追加
        var again = await client.PostAsync("/api/games/sokoban-toy/baselines/manual",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var againResult = JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, againResult.GetProperty("created").GetInt32());
        Assert.Equal(10, againResult.GetProperty("skipped").GetInt32());

        // 基准带执行确认引用，可追溯
        var baselines = factory.Services.GetRequiredService<BaselineStore>().ListByGame("sokoban-toy");
        Assert.Equal(10, baselines.Count);
        Assert.All(baselines, b => Assert.StartsWith("confirmation:", b.ExecutionConfirmationRef));
        Assert.All(baselines, b => Assert.Equal("测试确认人", b.ApprovedBy));
    }

    [Fact]
    public async Task 人工签认_确认人与说明必填()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var noName = await client.PostAsync("/api/games/sokoban-toy/baselines/manual",
            new StringContent("""{"confirmedBy":"","note":"逐关在游戏本体执行通过"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);

        var shortNote = await client.PostAsync("/api/games/sokoban-toy/baselines/manual",
            new StringContent("""{"confirmedBy":"张三","note":"跑过"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, shortNote.StatusCode);
    }

    private static async Task<JsonElement> GetStatus(HttpClient client)
    {
        var raw = await client.GetStringAsync("/api/games/sokoban-toy/baselines/status");
        return JsonDocument.Parse(raw).RootElement;
    }
}
