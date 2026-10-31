using System.Text.Json;
using System.Text.Json.Serialization;
using LevelForge.Platform.Api;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Storage;

var builder = WebApplication.CreateBuilder(args);

// 数据目录：默认仓库 data/（已 gitignore）；配置 LevelForge:DataDir 可覆盖（测试注入临时目录）
var dataDir = builder.Configuration["LevelForge:DataDir"] ?? "data";
var database = new AppDatabase(dataDir);

#region 服务装配（全部本地：SQLite + 文件，无外部依赖）

builder.Services.AddSingleton(database);
builder.Services.AddSingleton(GameRegistry.Instance);   // 静态注册表（冻结决策：随平台构建编译）
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<SolveRunStore>();
builder.Services.AddSingleton<PhraseStore>();
builder.Services.AddSingleton<TapeStore>();
builder.Services.AddHttpClient<LlmClient>();            // LLM 客户端：OpenAI 兼容协议，实例自带 key

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

// 建表 + 种子注册（首次启动自动完成；权威列每次启动对齐注册表）
database.Initialize();
GameSeeder.Seed(database, app.Services.GetRequiredService<GameRegistry>());

GameEndpoints.Map(app);
SolveEndpoints.Map(app);
SettingsEndpoints.Map(app);
PhraseEndpoints.Map(app);

// 根路径：服务标识（第 12 步起改为托管 web/ 前端构建产物）
app.MapGet("/", () => Results.Ok(new { name = "LevelForge Platform", status = "running" }));

#endregion

app.Run();

// 供 WebApplicationFactory 测试挂载（顶级语句生成的 Program 需 partial 声明才可被外部程序集引用）
public partial class Program { }
