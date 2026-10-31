using System.Net;
using System.Text;
using System.Text.Json;
using LevelForge.Platform.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LevelForge.Platform.Tests;

// 采纳落盘 + 执行确认 + 入基准：安全落盘六条流程与生命周期状态机的全链路验证
// 含故障注入反向验证（约定3）：半写事故、路径穿越、指纹不符——防线必须红
public sealed class AdoptionTests
{
    // 两个内容不同但都合法的可解关（同编号 gen-200，用于冲突/替换场景）
    private const string LevelV1 = """{"id":"gen-200","grid":["######","#@$ .#","#    #","######"]}""";
    private const string LevelV2 = """{"id":"gen-200","grid":["#######","#@$  .#","#     #","#######"]}""";

    private static async Task<(HttpStatusCode Status, string Body)> AdoptAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsync("/api/adopt",
            new StringContent(JsonSerializer.Serialize(body, TestJson.Options), Encoding.UTF8, "application/json"));
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static async Task<Api.AdoptResponseDto> AdoptOkAsync(HttpClient client, object body)
    {
        var (status, raw) = await AdoptAsync(client, body);
        Assert.True(status == HttpStatusCode.OK, $"采纳应返回 200，实际 {status}：{raw}");
        return JsonSerializer.Deserialize<Api.AdoptResponseDto>(raw, TestJson.Options)!;
    }

    private static string LevelFile(WebApplicationFactory<Program> factory, string levelId)
    {
        var dataDir = factory.Services.GetRequiredService<LevelForge.Platform.Database.AppDatabase>().DataDir;
        return Path.Combine(dataDir, "levels", "sokoban-toy", $"{levelId}.json");
    }

    [Fact]
    public async Task 采纳_落盘登记与待确认一次完成()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        var adopt = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        // 文件真实存在且内容为冻结候选原文
        Assert.True(File.Exists(LevelFile(factory, "gen-200")));
        Assert.Equal(LevelV1, File.ReadAllText(LevelFile(factory, "gen-200")));
        // 登记链完整：levels 行（source=generated）+ 待执行确认 + 模型验证运行
        Assert.Equal("pending", adopt.ConfirmationState);
        Assert.NotNull(adopt.SolveRunId);
        var levels = JsonSerializer.Deserialize<List<Api.LevelDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/levels"), TestJson.Options)!;
        var row = levels.First(l => l.LevelId == "gen-200");
        Assert.Equal("generated", row.Source);
        Assert.Equal(adopt.LevelHash, row.LevelHash);
    }

    [Fact]
    public async Task 重复采纳同一文件_幂等不重写不重复登记()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var first = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        var second = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        Assert.True(second.SameContent);                       // 识别为同一文件
        Assert.Equal(first.ConfirmationId, second.ConfirmationId);   // 复用确认，不重复登记
        var list = JsonSerializer.Deserialize<List<Api.ConfirmationDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/confirmations"), TestJson.Options)!;
        Assert.Single(list);                                   // 只有一条确认
    }

    [Fact]
    public async Task 编号冲突_默认不覆盖_带旧指纹()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        var (status, body) = await AdoptAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV2 });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("默认不覆盖", body);
        Assert.Contains("conflictHash", body);                 // 旧指纹带给前端做替换确认
        // 原文件未被碰
        Assert.Equal(LevelV1, File.ReadAllText(LevelFile(factory, "gen-200")));
    }

    [Fact]
    public async Task 替换_需旧指纹匹配_旧确认失效()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var first = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        // 旧指纹不对 → 拒绝替换（防并发覆盖）
        var wrong = await AdoptAsync(client, new
        {
            gameId = "sokoban-toy", levelJson = LevelV2, overwrite = true, expectedOldHash = "sha256:deadbeef"
        });
        Assert.Equal(HttpStatusCode.Conflict, wrong.Status);
        Assert.Contains("指纹不符", wrong.Body);

        // 旧指纹正确 → 替换成功，旧确认失效，新确认登记
        var replaced = await AdoptOkAsync(client, new
        {
            gameId = "sokoban-toy", levelJson = LevelV2, overwrite = true, expectedOldHash = first.LevelHash
        });
        Assert.True(replaced.Replaced);
        Assert.Equal(LevelV2, File.ReadAllText(LevelFile(factory, "gen-200")));
        var list = JsonSerializer.Deserialize<List<Api.ConfirmationDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/confirmations"), TestJson.Options)!;
        Assert.Equal(2, list.Count);
        Assert.Contains(list, c => c.Id == first.ConfirmationId && c.State == "invalidated");   // 旧确认失效
        Assert.Contains(list, c => c.Id == replaced.ConfirmationId && c.State == "pending");
    }

    [Fact]
    public async Task 非法关卡与路径注入_拒绝且不落盘()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();

        // 格式非法：不写盘
        var bad = await AdoptAsync(client, new { gameId = "sokoban-toy", levelJson = "{oops" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);

        // 路径穿越：id 含 .. → 拒绝
        var traversal = """{"id":"../evil","grid":["######","#@$ .#","#    #","######"]}""";
        var evil = await AdoptAsync(client, new { gameId = "sokoban-toy", levelJson = traversal });
        Assert.Equal(HttpStatusCode.BadRequest, evil.Status);
        Assert.Contains("非法字符", evil.Body);

        // 关卡根目录里没有产出任何文件
        var root = Path.Combine(factory.Database.DataDir, "levels", "sokoban-toy");
        if (Directory.Exists(root))
        {
            Assert.Empty(Directory.GetFiles(root));
        }
    }

    [Fact]
    public async Task 半写故障_原文件完好_临时文件清理()
    {
        // 故障注入（约定3）：替换发布这一步抛异常 → 防线必须保住原文件、清干净临时文件
        var faulty = new FaultyFileIO();
        using var factory = new PlatformFactory().WithWebHostBuilder(b => b.ConfigureServices(
            s => s.Replace(ServiceDescriptor.Singleton<ILevelFileIO>(faulty))));
        var client = factory.CreateClient();

        // 先正常落一个文件（首次写入无替换，故障不触发）
        var first = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });
        var target = LevelFile(factory, "gen-200");

        // 第二次替换时 Move 失败：采纳报错，原文件仍是 V1，目录无临时残留
        var (status, _) = await AdoptAsync(client, new
        {
            gameId = "sokoban-toy", levelJson = LevelV2, overwrite = true, expectedOldHash = first.LevelHash
        });
        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal(LevelV1, File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, ".*.tmp-*"));
    }

    [Fact]
    public async Task 人工降级确认_通过后入基准_重复确认幂等()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var adopt = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1, source = "imported" });

        // 未通过就入基准 → 拒绝
        var early = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/approve",
            new StringContent("""{"approvedBy":"zhang"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);

        // 人工降级：win + 签认人 → passed（注明仅最终结果）
        var manual = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/manual-result",
            new StringContent("""{"outcome":"win","confirmedBy":"zhang","note":"照单执行通关"}""",
                Encoding.UTF8, "application/json"));
        manual.EnsureSuccessStatusCode();
        Assert.Contains("\"state\":\"passed\"", await manual.Content.ReadAsStringAsync());
        Assert.Contains("仅最终结果", await manual.Content.ReadAsStringAsync());

        // 入基准：字段来自关联的模型验证运行
        var approve = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/approve",
            new StringContent("""{"approvedBy":"zhang"}""", Encoding.UTF8, "application/json"));
        approve.EnsureSuccessStatusCode();
        var approved = JsonSerializer.Deserialize<Api.ApproveResponseDto>(
            await approve.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.False(approved.AlreadyExists);

        var baselines = JsonSerializer.Deserialize<List<Api.BaselineDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/baselines"), TestJson.Options)!;
        var baseline = Assert.Single(baselines);
        Assert.Equal("gen-200", baseline.LevelId);
        Assert.Equal("solved", baseline.Status);
        Assert.Equal(adopt.LevelHash, baseline.LevelHash);
        Assert.True(baseline.SolutionLength > 0);
        Assert.NotEmpty(baseline.TrajectoryHash);              // 有解关：轨迹摘要来自磁带
        Assert.Equal("zhang", baseline.ApprovedBy);
        Assert.Equal($"confirmation:{adopt.ConfirmationId}", baseline.ExecutionConfirmationRef);

        // 重复确认：幂等返回既有基准，不重复追加
        var again = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/approve",
            new StringContent("""{"approvedBy":"zhang"}""", Encoding.UTF8, "application/json"));
        again.EnsureSuccessStatusCode();
        var duplicated = JsonSerializer.Deserialize<Api.ApproveResponseDto>(
            await again.Content.ReadAsStringAsync(), TestJson.Options)!;
        Assert.True(duplicated.AlreadyExists);
        Assert.Equal(approved.BaselineId, duplicated.BaselineId);
        Assert.Single(JsonSerializer.Deserialize<List<Api.BaselineDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/baselines"), TestJson.Options)!);
    }

    [Fact]
    public async Task 报告导入_指纹不匹配拒绝()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var adopt = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        // 报告指向另一份关卡 → 拒绝（不能拿别人的执行结论确认本关）
        var mismatch = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/report",
            new StringContent("""{"levelHash":"sha256:not-this-level","outcome":"win"}""",
                Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        // 指纹匹配 + win → passed（自动模式）
        var ok = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/report",
            new StringContent(
                JsonSerializer.Serialize(new { levelHash = adopt.LevelHash, outcome = "win", gameBuildFingerprint = "toy-build-1" }),
                Encoding.UTF8, "application/json"));
        ok.EnsureSuccessStatusCode();
        var body = await ok.Content.ReadAsStringAsync();
        Assert.Contains("\"state\":\"passed\"", body);
        Assert.Contains("\"mode\":\"auto\"", body);
        Assert.Contains("toy-build-1", body);
        // 报告落档且确认引用它（证据可追溯）
        Assert.True(File.Exists(Path.Combine(factory.Database.DataDir, "reports", $"confirmation-{adopt.ConfirmationId}.json")));
    }

    [Fact]
    public async Task 文件被改_确认状态诚实标注且不可入基准()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        var adopt = await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1 });

        // 通过人工确认后再改文件（模拟策划手工微调）
        var manual = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/manual-result",
            new StringContent("""{"outcome":"win","confirmedBy":"zhang"}""", Encoding.UTF8, "application/json"));
        manual.EnsureSuccessStatusCode();
        File.WriteAllText(LevelFile(factory, "gen-200"), LevelV2);   // 外部编辑：同编号换成另一内容

        // 列表如实标注 hashChanged
        var list = JsonSerializer.Deserialize<List<Api.ConfirmationDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/confirmations"), TestJson.Options)!;
        Assert.Equal("hashChanged", list[0].FileStatus);

        // 旧确认不能入基准（levelHash 变了 → 模型验证与执行证据不再对应当前文件）
        var approve = await client.PostAsync($"/api/confirmations/{adopt.ConfirmationId}/approve",
            new StringContent("""{"approvedBy":"zhang"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
    }

    [Fact]
    public async Task 质检采纳_来源标记为导入()
    {
        using var factory = new PlatformFactory();
        var client = factory.CreateClient();
        await AdoptOkAsync(client, new { gameId = "sokoban-toy", levelJson = LevelV1, source = "imported" });

        var levels = JsonSerializer.Deserialize<List<Api.LevelDto>>(
            await client.GetStringAsync("/api/games/sokoban-toy/levels"), TestJson.Options)!;
        Assert.Equal("imported", levels.First(l => l.LevelId == "gen-200").Source);
    }

    // 故障注入 IO：替换已存在的目标文件时抛异常（制造"发布替换瞬间"事故；首次写入不受影响）
    private sealed class FaultyFileIO : FileLevelFileIO
    {
        private int _remaining = 1;

        public override void MoveOverwrite(string source, string target)
        {
            if (File.Exists(target) && _remaining > 0)
            {
                _remaining--;
                throw new IOException("注入故障：发布替换失败");
            }
            base.MoveOverwrite(source, target);
        }
    }
}
