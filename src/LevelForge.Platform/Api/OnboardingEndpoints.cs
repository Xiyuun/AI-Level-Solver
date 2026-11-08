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
        // 转正门禁（验证门 §5.3/§6）：六步全 done + 门1/门2/门3 证据齐全且与当前适配层指纹一致，
        // 门3 最新一次无红灯。证据绑版本：适配层换过（hash 不符）就视为旧证据，必须重跑对应门。
        app.MapPost("/api/games/{gameId}/status", (string gameId, GameStatusRequestDto body,
            OnboardingStore onboarding, VerificationRecordStore records, GameStore games) =>
        {
            if (body.Status is null ||
                body.Status is not (GameStatus.Onboarding or GameStatus.Trial or GameStatus.Production))
            {
                return Results.BadRequest(new { message = "状态必须是 onboarding/trial/production" });
            }
            if (body.Status == GameStatus.Production)
            {
                var game = games.FindGame(gameId);
                if (game is null)
                {
                    return Results.NotFound(new { message = $"未知玩法：{gameId}" });
                }
                var tasks = onboarding.List(gameId);
                var missing = tasks.Where(t => t.Status != "done").Select(t => t.Step).ToList();
                if (tasks.Count != 6 || missing.Count > 0)
                {
                    var detail = missing.Count == 0 ? "六步数据不完整" : string.Join("、", missing.Select(s => $"第{s}步"));
                    return Results.Conflict(new { message = $"生产档要求六步全部 done；未完成：{detail}" });
                }

                var gate1 = records.List(gameId, "gate1").FirstOrDefault();
                if (gate1 is null || gate1.Verdict != "pass")
                {
                    return Results.Conflict(new { message = "门1 未通过：生产档要求门1 五项零豁免全绿" });
                }
                if (!string.Equals(gate1.AdapterHash, game.AdapterHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new { message = "门1 记录不是当前适配层（指纹不一致）：适配层换过，必须重跑门1" });
                }

                if (game.Gate2Applicable)
                {
                    var gate2 = records.List(gameId, "gate2").FirstOrDefault();
                    if (gate2 is null)
                    {
                        return Results.Conflict(new { message = "门2 适用但尚无对拍记录：先跑门2，或登记门2不适用" });
                    }
                    if (gate2.Verdict == "fail")
                    {
                        return Results.Conflict(new { message = "门2 最新一次对拍未通过：先分诊修复（适配层/harness/关卡数据/游戏本体）" });
                    }
                    if (!string.Equals(gate2.AdapterHash, game.AdapterHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Conflict(new { message = "门2 记录不是当前适配层（指纹不一致）：适配层换过，必须重跑门2" });
                    }
                }

                var gate3 = records.List(gameId, "gate3").FirstOrDefault();
                if (gate3 is null)
                {
                    return Results.Conflict(new { message = "尚未运行门3：生产档要求全量基准 diff，且最新一次无红灯" });
                }
                if (gate3.Verdict == "fail")
                {
                    return Results.Conflict(new
                    {
                        message = "门3 最新一次全量回归存在红灯，禁止转正：先修复规则差异，或确认后更新基准再重跑门3",
                    });
                }
                if (!string.Equals(gate3.AdapterHash, game.AdapterHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new { message = "门3 记录不是当前适配层（指纹不一致）：适配层换过，必须重跑门3" });
                }
            }
            if (!onboarding.UpdateGameStatus(gameId, body.Status))
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            return Results.Ok(new { status = body.Status });
        });

        // 门2驱动配置登记（接入产物⑦；向导①登记表单落库，替代手工 SQL）
        // applicable=false 时允许不带命令——那是"确认门2不适用"的登记（导入项目的默认态，三轮 N11）
        app.MapPost("/api/games/{gameId}/gate2-driver", (
            string gameId, Gate2DriverRequestDto body, OnboardingStore onboarding) =>
        {
            var confirmingNotApplicable = body.Applicable == false;
            if (!confirmingNotApplicable &&
                (string.IsNullOrWhiteSpace(body.Command) || body.Args is null || body.Args.Count == 0))
            {
                return Results.BadRequest(new { message = "驱动配置需要 command 与非空 args（若门2不适用请显式传 applicable=false）" });
            }
            if (confirmingNotApplicable)
            {
                if (!onboarding.UpdateGate2Driver(gameId, JsonSerializer.Serialize(new
                {
                    command = "",
                    args = Array.Empty<string>(),
                    timeoutSeconds = body.TimeoutSeconds ?? 180
                }, LevelForge.Kernel.Replay.TapeJson.Options), applicable: false))
                {
                    return Results.NotFound(new { message = $"未知玩法：{gameId}" });
                }
                return Results.Ok(new { message = "已登记：门2不适用", applicable = false });
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
