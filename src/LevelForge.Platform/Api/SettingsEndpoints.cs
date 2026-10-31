using System.Text.Json.Nodes;
using LevelForge.Platform.Database;
using LevelForge.Platform.Llm;

namespace LevelForge.Platform.Api;

// 设置端点：整表读写 + LLM 测试连接（表现层_设置）
public static class SettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 设置整表：key → 值（含 LLM 配置、生成默认值、界面状态）
        app.MapGet("/api/settings", (SettingsStore settings) => Results.Ok(settings.All()));

        // 设置合并更新：body 为 key→值 的对象，逐条覆写后返回全表
        app.MapPut("/api/settings", (Dictionary<string, JsonNode?> body, SettingsStore settings) =>
        {
            foreach (var (key, value) in body)
            {
                settings.Set(key, value);
            }
            return Results.Ok(settings.All());
        });

        // LLM 测试连接：body 可临时传入待验证配置（未保存先测），缺省用已存设置
        app.MapPost("/api/settings/llm/test", async (LlmTestRequestDto? body, SettingsStore settings, LlmClient llm) =>
        {
            var baseUrl = body?.BaseUrl ?? settings.Get<string>("llm.baseUrl", "");
            var apiKey = body?.ApiKey ?? settings.Get<string>("llm.apiKey", "");
            var model = body?.Model ?? settings.Get<string>("llm.model", "");
            var result = await llm.TestConnectionAsync(baseUrl ?? "", apiKey ?? "", model ?? "");
            return Results.Ok(new LlmTestResponseDto(result.Ok, result.LatencyMs, result.Error, result.ReplyPreview));
        });
    }
}
