using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Storage;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LevelForge.Platform.Tests;

// 玩法代码目录（四轮）：扫描器代码目录候选（跳过插件）+ 代码抽样两层结构（全量索引+玩法目录全文）+ code-root 端点
// 背景：8 文件×120 行的任意抽样对插件重的工程会系统性漏掉玩法代码（piggyfarm 实测 8 个名额全被 UniTask 吃掉）
public sealed class GameplayCodeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // 构造 unity 工程：Assets/Main/Game/（玩法代码 5 文件）+ Assets/Plugins/UniTask/（插件 6 文件）+ Assets/Main/FrameWork/（基础设施 2 文件）
    private static string MakeUnityWithCode(string root)
    {
        var game = Path.Combine(root, "Assets", "Main", "Game");
        Directory.CreateDirectory(game);
        for (var i = 1; i <= 5; i++)
        {
            var methods = string.Join("\n", Enumerable.Range(1, 12).Select(n =>
                $"    public void DoWork{n}(int amount) {{ Gold += amount + {n}; }}"));
            File.WriteAllText(Path.Combine(game, $"Player{i}.cs"),
                $"public class Player{i} {{\n    public int Gold;\n    public void AddGold(int amount) {{ Gold += amount; }}\n{methods}\n}}\n");
        }
        var plugins = Path.Combine(root, "Assets", "Plugins", "UniTask", "Runtime");
        Directory.CreateDirectory(plugins);
        for (var i = 1; i <= 6; i++)
        {
            File.WriteAllText(Path.Combine(plugins, $"UniTask{i}.cs"),
                $"namespace UniTaskInternal\n{{\n    public class Secret{i} {{ public void HiddenMethod() {{}} }}\n}}\n");
        }
        var framework = Path.Combine(root, "Assets", "Main", "FrameWork");
        Directory.CreateDirectory(framework);
        for (var i = 1; i <= 2; i++)
        {
            File.WriteAllText(Path.Combine(framework, $"Audio{i}.cs"), $"public class AudioManager{i} {{}}\n");
        }
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
        return root;
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "lf-code-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void 扫描器_玩法代码目录候选_跳过插件且按子树计数排序()
    {
        var root = MakeUnityWithCode(TempDir());
        try
        {
            var profile = ProjectScanner.Scan(root, "unity");
            Assert.NotNull(profile.CodeDirCandidates);
            Assert.NotEmpty(profile.CodeDirCandidates!);
            // 玩法代码目录（子树 5 个文件）在候选里
            var game = Assert.Single(profile.CodeDirCandidates!, c => c.Directory == "Assets/Main/Game");
            Assert.Equal(5, game.SourceFileCount);
            // 插件目录绝不提名
            Assert.DoesNotContain(profile.CodeDirCandidates!, c => c.Directory.Contains("Plugins", StringComparison.OrdinalIgnoreCase));
            // 基础设施 FrameWork 只有 2 个文件（<5）——不够格，但也不被排除出抽样索引（索引层不判断语义）
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 代码抽样_用户确认目录_索引全量加目录全文且插件忽略()
    {
        var root = MakeUnityWithCode(TempDir());
        try
        {
            var text = ProjectAnalyzer.SampleCode(root, "unity", "Assets/Main/Game");

            // 索引层：非插件源码全部在索引里（玩法 5 + 基础设施 2；插件 0）
            Assert.Contains("源码全量索引", text);
            Assert.Contains("Assets/Main/Game/Player1.cs", text);
            Assert.Contains("Assets/Main/FrameWork/Audio1.cs", text);
            Assert.DoesNotContain("UniTask", text);
            // 索引带签名事实（类名 + public 方法）
            Assert.Contains("class Player1", text);
            Assert.Contains("AddGold", text);
            // 正文层：用户确认的目录标注 + 玩法文件全文收录
            Assert.Contains("用户确认的玩法代码目录", text);
            Assert.Contains("public void AddGold(int amount)", text);   // 全文（此前 120 行截断时代的任意抽样做不到）
            // 插件代码绝不进正文
            Assert.DoesNotContain("HiddenMethod", text);
            Assert.Contains("已收录全部 5 个文件全文", text);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 代码抽样_未确认目录_自动提名最可能目录并如实标注()
    {
        var root = MakeUnityWithCode(TempDir());
        try
        {
            var text = ProjectAnalyzer.SampleCode(root, "unity");
            Assert.Contains("自动提名，未经用户确认", text);
            // 提名到了玩法代码目录：全文里出现玩法代码，插件仍然不出现
            Assert.Contains("AddGold", text);
            Assert.DoesNotContain("HiddenMethod", text);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void 代码抽样_预算封顶_如实标注收录进度()
    {
        var root = MakeUnityWithCode(TempDir());
        try
        {
            // 预算压到极小：5 个玩法文件装不全，必须如实说明收录了多少
            var text = ProjectAnalyzer.SampleCode(root, "unity", "Assets/Main/Game", maxChars: 2000);
            Assert.Contains("正文预算 2000 字符已用尽", text);
            Assert.Contains("/5 个文件全文", text);
            Assert.DoesNotContain("已收录全部 5 个文件全文", text);
            // 收录进去的部分仍是全文而非截断片段
            Assert.Contains("class Player1", text);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task 端点_玩法代码目录回填落库且详情带出()
    {
        var projectDir = MakeUnityWithCode(TempDir());
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        try
        {
            var import = await client.PostAsync("/api/projects/import", new StringContent(
                JsonSerializer.Serialize(new { directory = projectDir }, Json), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, import.StatusCode);
            var imported = JsonSerializer.Deserialize<Api.ProjectImportResultDto>(
                await import.Content.ReadAsStringAsync(), Json)!;

            // 未确认时详情 codeRoot 为 null
            var before = JsonSerializer.Deserialize<Api.GameDetailDto>(
                await client.GetStringAsync($"/api/games/{imported.GameId}"), Json)!;
            Assert.Null(before.CodeRoot);

            var put = await client.PutAsync($"/api/games/{imported.GameId}/code-root", new StringContent(
                JsonSerializer.Serialize(new { codeRoot = "Assets/Main/Game" }, Json), Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var after = JsonSerializer.Deserialize<Api.GameDetailDto>(
                await client.GetStringAsync($"/api/games/{imported.GameId}"), Json)!;
            Assert.Equal("Assets/Main/Game", after.CodeRoot);
        }
        finally
        {
            try { Directory.Delete(projectDir, true); } catch (IOException) { }
        }
    }
}
