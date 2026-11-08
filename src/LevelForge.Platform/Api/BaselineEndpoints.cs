using System.Text;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Api;

// 基准管理端点（⑥门3的对照物）：
// - 状态：每关是否已有"当前适配层指纹 + 当前内核版本"的基准，以及最新运行结论
// - 人工签认：门2不适用的降级路径——用户逐关在游戏本体执行通过后，用确认人+说明签认，
//   平台据此写执行确认并落基准（不是门3自证；由人声明"游戏执行通过"）
public static class BaselineEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/games/{gameId}/baselines/status", (
            string gameId, GameStore games, BaselineStore baselines, SolveRunStore runs, GameRegistry registry) =>
        {
            var game = games.FindGame(gameId);
            if (game is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            if (!registry.TryGet(gameId, out var facade))
            {
                return Results.BadRequest(new { message = "适配层未接入：先用向导③上传并检测" });
            }
            var reg = facade.Registration;
            var rows = games.ListLevels(gameId).Select(level =>
            {
                var baseline = baselines.FindExisting(gameId, level.LevelId, reg.AdapterHash, reg.KernelVersion);
                var run = runs.FindLatestForLevel(gameId, level.LevelId, reg.AdapterHash);
                return new
                {
                    levelId = level.LevelId,
                    hasBaseline = baseline is not null,
                    baselineId = baseline?.Id,
                    runId = run?.Id,
                    runStatus = run?.Status,
                    solutionLength = run?.SolutionLength,
                };
            }).ToList();
            var confirmed = rows.Count(r => r.hasBaseline);
            return Results.Ok(new
            {
                adapterHash = reg.AdapterHash,
                kernelVer = reg.KernelVersion,
                total = rows.Count,
                confirmed,
                missing = rows.Count - confirmed,
                rows,
            });
        });

        app.MapPost("/api/games/{gameId}/baselines/manual", (
            string gameId, ManualBaselineRequestDto body,
            GameStore games, GameRegistry registry, SolveRunStore runs, TapeStore tapes,
            ConfirmationStore confirmations, BaselineStore baselines) =>
        {
            var game = games.FindGame(gameId);
            if (game is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            if (!registry.TryGet(gameId, out var facade))
            {
                return Results.BadRequest(new { message = "适配层未接入：先用向导③上传并检测" });
            }
            var confirmedBy = body.ConfirmedBy?.Trim();
            if (string.IsNullOrWhiteSpace(confirmedBy))
            {
                return Results.BadRequest(new { message = "确认人不能为空（会写进执行确认与基准审计）" });
            }
            var note = body.Note?.Trim();
            if (string.IsNullOrWhiteSpace(note) || note.Length < 10)
            {
                return Results.BadRequest(new { message = "签认说明至少 10 个字：写清依据（如逐关在游戏本体执行通过）" });
            }

            var reg = facade.Registration;
            var levels = games.ListLevels(gameId).ToList();
            var targets = body.LevelIds is { Count: > 0 }
                ? levels.Where(l => body.LevelIds.Contains(l.LevelId)).ToList()
                : levels;

            var created = 0;
            var skipped = 0;
            var failed = 0;
            var errors = new List<string>();
            foreach (var level in targets)
            {
                try
                {
                    if (baselines.FindExisting(gameId, level.LevelId, reg.AdapterHash, reg.KernelVersion) is not null)
                    {
                        skipped++;
                        continue;
                    }
                    // 优先复用门3/求解已产生的当前版本运行；没有才现跑一次（8s 上限，预算耗尽如实记录）
                    var run = runs.FindLatestForLevel(gameId, level.LevelId, reg.AdapterHash);
                    if (run is null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(level.ContentJson);
                        var solve = facade.Solve(new SolveCommand(
                            level.ContentJson, bytes, level.LevelId, TimeSpan.FromSeconds(8)));
                        var runId = runs.Insert(reg, level.LevelId, level.LevelHash, solve);
                        if (solve.Tape is not null)
                        {
                            runs.AttachTape(runId, tapes.Save(runId, solve.Tape).Id);
                        }
                        run = runs.Get(runId);
                        if (run is null)
                        {
                            failed++;
                            errors.Add($"{level.LevelId}：运行记录写入失败");
                            continue;
                        }
                    }

                    var confirmationId = confirmations.InsertPending(gameId, level.LevelId, level.LevelHash, run.Id);
                    confirmations.RecordManualResult(confirmationId, "win", confirmedBy, note);
                    var tape = run.TapeId is not null ? tapes.FindByRun(run.Id) : null;
                    baselines.Insert(
                        gameId, level.LevelId, level.LevelHash,
                        reg.AdapterVersion, reg.AdapterHash, run.KernelVer, run.ContractVer,
                        gameBuildFingerprint: null, runConfigHash: run.RunConfigHash,
                        run.Status, run.Optimality, run.GraphComplete, run.TerminationReason,
                        run.SolutionLength, run.StatesExplored,
                        tape?.TrajectoryHash ?? "", tape?.ArtifactHash,
                        $"confirmation:{confirmationId}", confirmedBy);
                    created++;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{level.LevelId}：{ex.Message}");
                }
            }

            return Results.Ok(new ManualBaselineResponseDto(
                created, skipped, failed, reg.AdapterHash, reg.KernelVersion, errors));
        });
    }
}
