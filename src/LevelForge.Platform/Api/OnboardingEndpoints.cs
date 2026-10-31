using System.Text.Json;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Gates;

namespace LevelForge.Platform.Api;

// 接入向导端点：六步任务查询/步进、玩法状态变更（试用档↔转正）、门2驱动配置登记
// 薄页面版（冻结决策）：平台只存档与编排，适配层生成在外部编码 agent + skill 完成（第15步）
public static class OnboardingEndpoints
{
    // 向导步状态合法取值
    private static readonly string[] StepStatuses = ["todo", "doing", "done", "blocked"];

    public static void Map(IEndpointRouteBuilder app)
    {
        // 六步任务列表
        app.MapGet("/api/games/{gameId}/onboarding", (string gameId, OnboardingStore onboarding) =>
        {
            var tasks = onboarding.List(gameId);
            if (tasks.Count == 0) return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            return Results.Ok(tasks.Select(t => new OnboardingTaskDto(
                t.Step, t.Title, t.Status,
                t.DetailJson is null ? null : TryUnquote(t.DetailJson), t.UpdatedAt)).ToList());
        });

        // 步进更新（状态 + 可选存档：资格报告原文/审核清单快照等）
        app.MapPost("/api/games/{gameId}/onboarding/{step:int}", (
            string gameId, int step, OnboardingStepRequestDto body, OnboardingStore onboarding) =>
        {
            if (body.Status is null || !StepStatuses.Contains(body.Status))
            {
                return Results.BadRequest(new { message = "步状态必须是 todo/doing/done/blocked" });
            }
            if (step is < 1 or > 6)
            {
                return Results.BadRequest(new { message = "步号必须是 1~6" });
            }
            if (!onboarding.UpdateStep(gameId, step, body.Status, Quote(body.Detail)))
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            return Results.Ok(new { message = "已更新" });
        });

        // 玩法状态变更（分级接入 §5：试用档先行、转正档补验；上线注册/回退）
        app.MapPost("/api/games/{gameId}/status", (string gameId, GameStatusRequestDto body, OnboardingStore onboarding) =>
        {
            if (body.Status is null ||
                body.Status is not (GameStatus.Onboarding or GameStatus.Trial or GameStatus.Production))
            {
                return Results.BadRequest(new { message = "状态必须是 onboarding/trial/production" });
            }
            if (!onboarding.UpdateGameStatus(gameId, body.Status))
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            return Results.Ok(new { status = body.Status });
        });

        // 门2驱动配置登记（接入产物⑦；向导①登记表单落库，替代手工 SQL）
        app.MapPost("/api/games/{gameId}/gate2-driver", (
            string gameId, Gate2DriverRequestDto body, OnboardingStore onboarding) =>
        {
            if (string.IsNullOrWhiteSpace(body.Command) || body.Args is null || body.Args.Count == 0)
            {
                return Results.BadRequest(new { message = "驱动配置需要 command 与非空 args" });
            }
            try
            {
                var driver = JsonSerializer.Serialize(new
                {
                    command = body.Command,
                    args = body.Args,
                    timeoutSeconds = body.TimeoutSeconds ?? 180
                }, LevelForge.Kernel.Replay.TapeJson.Options);
                if (!onboarding.UpdateGate2Driver(gameId, driver, body.Applicable ?? true))
                {
                    return Results.NotFound(new { message = $"未知玩法：{gameId}" });
                }
                return Results.Ok(new { message = "已登记", applicable = body.Applicable ?? true });
            }
            catch (GateException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });
    }

    #region 内部工具

    // 存档文本按 JSON 字符串落库（detail_json 列语义：JSON 值）
    private static string? Quote(string? text) =>
        text is null ? null : JsonSerializer.Serialize(text);

    // 读取时还原为纯文本（JSON 字符串 → 文本）
    private static string? TryUnquote(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    #endregion
}
