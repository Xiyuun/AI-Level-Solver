using System.Text.Json;
using System.Text.Json.Serialization;
using LevelForge.Platform.Adoption;
using LevelForge.Platform.AdapterGen;
using LevelForge.Platform.Api;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Gates;
using LevelForge.Platform.Generation;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Plugins;
using LevelForge.Platform.Storage;

var builder = WebApplication.CreateBuilder(args);

// 数据目录：默认仓库 data/（已 gitignore）；配置 LevelForge:DataDir 可覆盖（测试注入临时目录）
var dataDir = builder.Configuration["LevelForge:DataDir"] ?? "data";
var database = new AppDatabase(dataDir);

#region 服务装配（全部本地：SQLite + 文件，无外部依赖）

builder.Services.AddSingleton(database);
builder.Services.AddSingleton(GameRegistry.Instance);   // 注册表：内置玩法编译期注册 + 插件玩法运行期装载（三轮 D7 解冻）
builder.Services.AddSingleton<PluginLoader>();  // 适配层插件装载器（数据目录 plugins/{gameId}/）
builder.Services.AddSingleton<AdapterGenRunner>();   // 生成执行器（生成→编译→回喂循环）
builder.Services.AddSingleton<AdapterGenJobService>();  // 适配层平台内生成（三轮 D9 解冻：生成→编译→回喂循环）
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<SolveRunStore>();
builder.Services.AddSingleton<PhraseStore>();
builder.Services.AddSingleton<TapeStore>();
builder.Services.AddSingleton<GenerationStore>();
builder.Services.AddSingleton<FewShotSelector>();
builder.Services.AddSingleton<GenerationPipeline>();
builder.Services.AddSingleton<GenerationJobService>();   // 异步生成任务 + SSE 进度（后台跑，可取消）
builder.Services.AddSingleton<ILevelFileIO, FileLevelFileIO>();   // 文件 IO 接缝（故障注入测试替换）
builder.Services.AddSingleton<LevelFileStore>();
builder.Services.AddSingleton<ConfirmationStore>();
builder.Services.AddSingleton<BaselineStore>();
builder.Services.AddSingleton<VerificationRecordStore>();
builder.Services.AddSingleton<OnboardingStore>();
builder.Services.AddSingleton<GateService>();
builder.Services.AddSingleton<AdoptionService>();
builder.Services.AddSingleton<LevelImporter>();       // 原生关卡导入（接入向导③：游戏项目目录 → 平台库）
builder.Services.AddSingleton<ProjectImporter>();     // 项目导入（三轮 N10：选目录 → 识别 Unity/Cocos → 登记玩法）
builder.Services.AddSingleton<ProjectAnalyzer>();     // 项目深度分析（三轮 N12 完整版 B：平台内 LLM 资格评估，解冻记录 §五 D8）
builder.Services.AddSingleton<LevelForge.Platform.Batch.BatchService>();   // 批量生产（同一流水线的调度层）
// LLM 客户端：OpenAI 兼容协议，实例自带 key。
// HttpClient.Timeout 必须置为无限：其默认值 100s 会先于 LlmClient 自己按 llm.timeoutSeconds 建的
// CancellationTokenSource（适配层生成下限 180s、可调到 300s）生效，长调用会被 100s 掐断并重试，
// 每轮重发整份提示词（费用翻倍），错误信息还会误报成"调用超时（180s）"
builder.Services.AddHttpClient<LlmClient>(c => c.Timeout = System.Threading.Timeout.InfiniteTimeSpan);

#endregion

// API JSON 口径与磁带一致：camelCase + 枚举小写字符串（TapeJson.Options 风格）
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

#region 启动初始化 + 端点映射

// 建表 + 插件装载 + 种子注册（首次启动自动完成；权威列每次启动对齐注册表——含插件玩法）
database.Initialize();
app.Services.GetRequiredService<PluginLoader>().LoadAll();   // 三轮 D7：plugins/{gameId}/ 全量装载，重启后插件依然生效
GameSeeder.Seed(database, app.Services.GetRequiredService<GameRegistry>());

// 工具仓库路径：向导③"路径 B"的外部编程助手需要它拼 ProjectReference（契约/内核工程）。
// 源码运行（dotnet run / 开发构建）时从运行目录向上找 LevelForge.slnx；发布版没有源码仓库则为空。
var toolRepoRoot = ResolveToolRepoRoot();
if (toolRepoRoot is not null)
{
    var settingsStore = app.Services.GetRequiredService<SettingsStore>();
    if (string.IsNullOrWhiteSpace(settingsStore.Get<string>("system.toolRepoRoot", "")))
    {
        settingsStore.Set("system.toolRepoRoot", toolRepoRoot);
    }
}

// 前端构建产物托管：开发模式找仓库 web/dist，自包含发布找 exe 同级 wwwroot
var devDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"));
var pubWwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var distDir = Directory.Exists(devDist) ? devDist : (Directory.Exists(pubWwwroot) ? pubWwwroot : null);
var hasDist = distDir is not null;
if (hasDist)
    {
        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distDir ?? string.Empty);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }

GameEndpoints.Map(app);
ProjectEndpoints.Map(app);
SolveEndpoints.Map(app);
SettingsEndpoints.Map(app);
PhraseEndpoints.Map(app);
GenerationEndpoints.Map(app);
AdoptionEndpoints.Map(app);
GateEndpoints.Map(app);
PlayEndpoints.Map(app);
OnboardingEndpoints.Map(app);
BatchEndpoints.Map(app);

if (hasDist)
{
    // SPA fallback：未知路径回 index.html（前端路由）
    app.MapFallback(async (HttpContext ctx) =>
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(Path.Combine(distDir ?? string.Empty, "index.html"));
    });
}
else
{
    // 无前端产物时根路径返回服务标识
    app.MapGet("/", () => Results.Ok(new { name = "LevelForge Platform", status = "running" }));
}

#endregion

app.Run();

// 工具仓库根：从运行目录/当前目录向上找 LevelForge.slnx（最多 8 层）；发布版找不到 → null
static string? ResolveToolRepoRoot()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LevelForge.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
    }
    return null;
}

// 供 WebApplicationFactory 测试挂载（顶级语句生成的 Program 需 partial 声明才可被外部程序集引用）
public partial class Program { }
