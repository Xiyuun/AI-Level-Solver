using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LevelForge.Platform.Tests;

// M3 两个后端防线：
// ① 导入结果逐关可见（过去 110 关里 35 关被跳过却说不清是哪 35 关）
// ② 转正门禁（门3 有红灯时禁止上线——红灯=结论发生了未解释的变化）
public sealed class OnboardingGateTests
{
    private const string ValidLevel = """{"id":"toy-901","grid":["#####","#@$.#","#####"]}""";
    private const string RaggedLevel = """{"id":"toy-902","grid":["######","#@$.#","######"]}""";

    [Fact]
    public async Task 关卡导入_逐关给出结论与跳过原因()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lf-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "a.json"), ValidLevel);
            await File.WriteAllTextAsync(Path.Combine(dir, "b.json"), RaggedLevel);

            using var factory = new PlatformFactory();
            var client = factory.CreateClient();
            var resp = await client.PostAsync("/api/games/sokoban-toy/import-levels",
                new StringContent(JsonSerializer.Serialize(new { directory = dir }), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("imported").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("failed").GetInt32());

            // 逐关明细必须存在，且失败项带原因——这是"说不清哪 35 关"的解法
            var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(2, entries.Count);
            var failed = entries.Single(e => e.GetProperty("outcome").GetString() == "failed");
            Assert.Equal("b.json", failed.GetProperty("file").GetString());
            Assert.False(string.IsNullOrWhiteSpace(failed.GetProperty("reason").GetString()));
            var imported = entries.Single(e => e.GetProperty("outcome").GetString() == "imported");
            Assert.Equal("toy-901", imported.GetProperty("levelId").GetString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task 转正门禁_门3有红灯时拒绝上线()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await CompleteAllStepsAsync(client);
        var store = factory.Services.GetRequiredService<VerificationRecordStore>();
        var hash = CurrentAdapterHash(factory);
        store.Insert("sokoban-toy", "gate1", "pass", adapterHash: hash);
        store.Insert("sokoban-toy", "gate3", "fail", adapterHash: hash, summaryJson: """{"total":3,"red":1}""");

        var promote = await client.PostAsync("/api/games/sokoban-toy/status",
            new StringContent("""{"status":"production"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, promote.StatusCode);
        var body = await promote.Content.ReadAsStringAsync();
        Assert.Contains("红灯", body);
    }

    [Fact]
    public async Task 转正门禁_缺门3记录时拒绝()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await CompleteAllStepsAsync(client);
        var store = factory.Services.GetRequiredService<VerificationRecordStore>();
        store.Insert("sokoban-toy", "gate1", "pass", adapterHash: CurrentAdapterHash(factory));

        var promote = await client.PostAsync("/api/games/sokoban-toy/status",
            new StringContent("""{"status":"production"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, promote.StatusCode);
        Assert.Contains("门3", await promote.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 转正门禁_适配层换过后旧证据不算数()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await CompleteAllStepsAsync(client);
        var store = factory.Services.GetRequiredService<VerificationRecordStore>();
        store.Insert("sokoban-toy", "gate1", "pass", adapterHash: "sha256:old-adapter");
        store.Insert("sokoban-toy", "gate3", "pass", adapterHash: CurrentAdapterHash(factory));

        var promote = await client.PostAsync("/api/games/sokoban-toy/status",
            new StringContent("""{"status":"production"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, promote.StatusCode);
        Assert.Contains("指纹", await promote.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 转正门禁_六步完成且门1门3证据对应当前版本时放行()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await CompleteAllStepsAsync(client);
        var store = factory.Services.GetRequiredService<VerificationRecordStore>();
        var hash = CurrentAdapterHash(factory);
        store.Insert("sokoban-toy", "gate1", "pass", adapterHash: hash);
        store.Insert("sokoban-toy", "gate3", "pass", adapterHash: hash, summaryJson: """{"total":3,"red":0}""");

        var promote = await client.PostAsync("/api/games/sokoban-toy/status",
            new StringContent("""{"status":"production"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
    }

    private static async Task CompleteAllStepsAsync(HttpClient client)
    {
        for (var step = 1; step <= 6; step++)
        {
            var body = JsonSerializer.Serialize(new { status = "done", detail = $"转正测试：完成第{step}步" });
            var resp = await client.PostAsync($"/api/games/sokoban-toy/onboarding/{step}",
                new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
    }

    private static string CurrentAdapterHash(PlatformFactory factory) =>
        factory.Services.GetRequiredService<GameStore>().FindGame("sokoban-toy")!.AdapterHash;
}
