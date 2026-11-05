using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Plugins;

namespace LevelForge.Platform.Tests;

// 插件装载（三轮 D7 解冻）：装载器单测 + 检测/上传端点全链路
// 测试插件 LevelForge.TestPlugin.dll 由测试工程引用自动产出到测试输出目录（模拟外部 AI 产出的插件）
public sealed class PluginLoadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lf-plugin-" + Guid.NewGuid().ToString("N"));
    private readonly GameRegistry _registry = new();   // 每测试独立注册表（不污染全局单例）
    private string _pluginDll = Path.Combine(AppContext.BaseDirectory, "LevelForge.TestPlugin.dll");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* 插件 DLL 被 ALC 锁定：清理尽力而为 */ }
    }

    private PluginLoader NewLoader()
    {
        var db = new LevelForge.Platform.Database.AppDatabase(_dir);
        db.Initialize();   // 建表 + 迁移（真实场景由 Program 启动完成）
        return new PluginLoader(db, _registry);
    }

    // 把测试输出目录里的插件 DLL 复制为本测试专属源（避免跨测试的文件锁竞争）
    private void OwnPluginDll()
    {
        var local = Path.Combine(_dir, "source", "LevelForge.TestPlugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.Copy(_pluginDll, local, overwrite: true);
        _pluginDll = local;
    }

    private string PlacePlugin(string gameId)
    {
        var dir = Path.Combine(_dir, "plugins", gameId);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "LevelForge.TestPlugin.dll");
        File.Copy(_pluginDll, target, overwrite: true);
        return target;
    }

    [Fact]
    public void 装载器_目标目录缺失_给出可操作提示()
    {
        OwnPluginDll();
        var loader = NewLoader();
        var outcome = loader.LoadForGame("no-such-game");
        Assert.Equal("missing", outcome.Loaded);
        Assert.Contains("plugins", outcome.Error);
    }

    [Fact]
    public void 装载器_加载测试插件_绑定校验并注册进注册表()
    {
        OwnPluginDll();
        PlacePlugin("test-plugin");
        var loader = NewLoader();
        var outcome = loader.LoadForGame("test-plugin");
        Assert.True(outcome.Loaded == "ok", $"装载结果 {outcome.Loaded}：{outcome.Error}");
        Assert.Equal("测试插件玩法", outcome.DisplayName);
        Assert.StartsWith("sha256:", outcome.AdapterHash);
        // 动态注册生效：注册表立即可用
        Assert.True(_registry.TryGet("test-plugin", out var facade));
        Assert.Equal("test-plugin", facade.Registration.GameId);
        // 权威元数据落库：games 行自动 upsert（行原本不存在——非导入项目也能装载，行 status 取 Registration 声明）
        var row = new GameStore(new AppDatabase(_dir)).FindGame("test-plugin");
        Assert.NotNull(row);
        Assert.StartsWith("sha256:", row!.AdapterHash);
    }

    [Fact]
    public void 装载器_gameId不匹配_拒绝并给出双方向信息()
    {
        OwnPluginDll();
        PlacePlugin("wrong-id");   // 插件声明 test-plugin，目录/请求是 wrong-id
        var loader = NewLoader();
        var outcome = loader.LoadForGame("wrong-id");
        Assert.Equal("bindingError", outcome.Loaded);
        Assert.Contains("test-plugin", outcome.Error);
        Assert.Contains("wrong-id", outcome.Error);
        // 误装护栏：绑定失败绝不进入注册表
        Assert.False(_registry.TryGet("test-plugin", out _));
    }

    [Fact]
    public async Task 检测端点_上传后检测_门1自动运行()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        // 负向：导入一个目录名与插件声明不一致的项目 → 上传 → 检测 → 绑定错误
        var import = await PostJsonAsync(client, "/api/projects/import", new { directory = MakeCocosProject("DemoMahjong") });
        var imported = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(await import.Content.ReadAsStringAsync(), TestJson.Options)!;
        using (var form = new MultipartFormDataContent())
        using (var fs = File.OpenRead(_pluginDll))
        {
            var content = new StreamContent(fs);
            content.Headers.ContentType = new("application/octet-stream");
            form.Add(content, "file", "LevelForge.TestPlugin.dll");
            var upload = await client.PostAsync($"/api/games/{imported.GameId}/plugins/upload", form);
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }
        var detect = await PostJsonAsync(client, $"/api/games/{imported.GameId}/plugins/detect", new { });
        Assert.Equal(HttpStatusCode.BadRequest, detect.StatusCode);
        Assert.Contains("gameId", await detect.Content.ReadAsStringAsync());

        // 正向：导入目录名恰为 test-plugin 的项目 → 上传 → 检测 → 门1全绿 + adapterReady
        var dir2 = MakeCocosProject("test-plugin");
        try
        {
            var import2 = await PostJsonAsync(client, "/api/projects/import", new { directory = dir2 });
            var imported2 = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(await import2.Content.ReadAsStringAsync(), TestJson.Options)!;
            Assert.Equal("test-plugin", imported2.GameId);

            using (var form = new MultipartFormDataContent())
            using (var fs = File.OpenRead(_pluginDll))
            {
                var content = new StreamContent(fs);
                form.Add(content, "file", "LevelForge.TestPlugin.dll");
                await client.PostAsync("/api/games/test-plugin/plugins/upload", form);
            }
            var detect2 = await PostJsonAsync(client, "/api/games/test-plugin/plugins/detect", new { });
            Assert.Equal(HttpStatusCode.OK, detect2.StatusCode);
            var dto = JsonSerializer.Deserialize<Api.AdapterDetectResultDto>(await detect2.Content.ReadAsStringAsync(), TestJson.Options)!;
            Assert.True(dto.Gate1Passed);

            // adapterReady 翻转：玩法列表里 test-plugin 已可生成
            var games = JsonSerializer.Deserialize<List<Api.GameSummaryDto>>(
                await client.GetStringAsync("/api/games"), TestJson.Options)!;
            Assert.True(games.Single(g => g.GameId == "test-plugin").AdapterReady);
        }
        finally
        {
            try { Directory.Delete(dir2, true); } catch (IOException) { }
        }
    }

    private static string MakeCocosProject(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-plug-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "package.json"), """{"creator":"3.8"}""");
        return root;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        return await client.PostAsync(url, new StringContent(
            JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
    }
}
