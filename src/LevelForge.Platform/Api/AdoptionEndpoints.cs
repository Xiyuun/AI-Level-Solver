using System.Text;
using System.Text.Json;
using LevelForge.Platform.Adoption;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Api;

// 采纳与执行确认端点：落盘→待确认→执行结论→入基准的完整生命周期（表现层_生成工作台 §4.1/§4.2）
public static class AdoptionEndpoints
{
    // 执行结局合法取值（磁带/harness 共用口径）
    private static readonly string[] Outcomes = ["win", "lose", "incomplete", "error"];

    public static void Map(IEndpointRouteBuilder app)
    {
        // 采纳落盘：生成候选/质检内容 → 安全写盘 + 登记待执行确认
        app.MapPost("/api/adopt", (AdoptRequestDto req, AdoptionService adoption) =>
        {
            var source = req.Source == LevelSource.Imported ? LevelSource.Imported : LevelSource.Generated;
            try
            {
                var outcome = adoption.Adopt(req.GameId ?? "", req.LevelJson ?? "", source, req.Overwrite, req.ExpectedOldHash);
                return Results.Ok(new AdoptResponseDto(
                    outcome.LevelId, outcome.LevelHash, outcome.RelativePath, outcome.SameContent,
                    outcome.Replaced, outcome.ConfirmationId, outcome.ConfirmationState, outcome.SolveRunId));
            }
            catch (AdoptionConflictException ex)
            {
                // 编号冲突：409 带旧指纹（前端展示后可带 ExpectedOldHash 重新提交替换）
                return Results.Conflict(new { message = ex.Message, conflictHash = ex.ConflictHash });
            }
            catch (AdoptionException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 确认列表：附当前文件核对状态（ok/missing/hashChanged——显示层不把旧确认当好状态）
        app.MapGet("/api/games/{gameId}/confirmations", (string gameId, ConfirmationStore confirmations, AdoptionService adoption) =>
        {
            var list = confirmations.ListByGame(gameId).Select(c =>
            {
                var (status, current) = adoption.CheckCurrentFile(gameId, c.LevelId, c.LevelHash);
                return new ConfirmationDto(
                    c.Id, c.GameId, c.LevelId, c.LevelHash, c.State, c.Mode,
                    c.Outcome, c.ConfirmedBy, c.ConfirmedAt, c.Note, c.SolveRunId,
                    status, current, c.CreatedAt, c.UpdatedAt);
            }).ToList();
            return Results.Ok(list);
        });

        // 人工降级结论：开发照单执行并签认后的录入（仅最终结果，验证门 §4.4）
        app.MapPost("/api/confirmations/{id}/manual-result", (long id, ManualResultRequestDto body,
            ConfirmationStore confirmations, AdoptionService adoption) =>
        {
            var confirmation = confirmations.Get(id);
            if (confirmation is null) return Results.NotFound(new { message = $"确认记录不存在：{id}" });
            if (body.Outcome is null || !Outcomes.Contains(body.Outcome) || body.Outcome == "error")
            {
                return Results.BadRequest(new { message = "结局必须是 win/lose/incomplete" });
            }
            if (string.IsNullOrWhiteSpace(body.ConfirmedBy))
            {
                return Results.BadRequest(new { message = "签认人不能为空" });
            }
            if (confirmation.State is not (ConfirmationState.Pending or ConfirmationState.Failed))
            {
                return Results.BadRequest(new { message = $"当前状态 {confirmation.State} 不可录入结论" });
            }
            var (status, _) = adoption.CheckCurrentFile(confirmation.GameId, confirmation.LevelId, confirmation.LevelHash);
            if (status != "ok")
            {
                return Results.BadRequest(new { message = $"当前文件状态 {status}，旧确认不再可用，请重新落盘验证" });
            }
            var note = $"人工降级：仅最终结果（{body.Note ?? "无备注"}）";
            confirmations.RecordManualResult(id, body.Outcome, body.ConfirmedBy, note);
            return Results.Ok(confirmations.Get(id));
        });

        // 导入执行报告（自动模式）：报告指纹必须与当前文件一致，不匹配拒绝确认
        app.MapPost("/api/confirmations/{id}/report", (long id, ExecutionReportDto body,
            ConfirmationStore confirmations, AdoptionService adoption, AppDatabase database) =>
        {
            var confirmation = confirmations.Get(id);
            if (confirmation is null) return Results.NotFound(new { message = $"确认记录不存在：{id}" });
            if (body.Outcome is null || !Outcomes.Contains(body.Outcome))
            {
                return Results.BadRequest(new { message = "结局必须是 win/lose/incomplete/error" });
            }
            if (!string.Equals(body.LevelHash, confirmation.LevelHash, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { message = "报告与确认记录的关卡指纹不匹配，拒绝确认" });
            }
            var (status, _) = adoption.CheckCurrentFile(confirmation.GameId, confirmation.LevelId, confirmation.LevelHash);
            if (status != "ok")
            {
                return Results.BadRequest(new { message = $"当前文件状态 {status}，报告不再适用，请重新落盘验证" });
            }
            // 报告原文落档（证据可追溯），确认记录引用它
            var reportDir = Path.Combine(database.DataDir, "reports");
            Directory.CreateDirectory(reportDir);
            var reportRef = Path.Combine("reports", $"confirmation-{id}.json");
            File.WriteAllText(Path.Combine(database.DataDir, reportRef),
                JsonSerializer.Serialize(new
                {
                    confirmationId = id,
                    levelHash = body.LevelHash,
                    outcome = body.Outcome,
                    gameBuildFingerprint = body.GameBuildFingerprint,
                    importedAt = AppDatabase.UtcNowIso
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            confirmations.ApplyReport(id, reportRef, body.Outcome, body.GameBuildFingerprint);
            return Results.Ok(confirmations.Get(id));
        });

        // 确认加入基准：仅当 游戏执行通过 + 当前文件未变 + 模型验证有效（运行指纹对齐）
        app.MapPost("/api/confirmations/{id}/approve", (long id, ApproveRequestDto body,
            ConfirmationStore confirmations, AdoptionService adoption, SolveRunStore runs,
            TapeStore tapes, BaselineStore baselines, GameRegistry registry) =>
        {
            var confirmation = confirmations.Get(id);
            if (confirmation is null) return Results.NotFound(new { message = $"确认记录不存在：{id}" });
            if (string.IsNullOrWhiteSpace(body.ApprovedBy))
            {
                return Results.BadRequest(new { message = "确认人不能为空" });
            }
            if (confirmation.State != ConfirmationState.Passed)
            {
                return Results.BadRequest(new { message = $"游戏执行未通过（当前 {confirmation.State}），不能入基准" });
            }
            var (status, _) = adoption.CheckCurrentFile(confirmation.GameId, confirmation.LevelId, confirmation.LevelHash);
            if (status != "ok")
            {
                return Results.BadRequest(new { message = $"当前文件状态 {status}，不能入基准" });
            }
            if (confirmation.SolveRunId is not { } runId || runs.Get(runId) is not { } run)
            {
                return Results.BadRequest(new { message = "缺少关联的模型验证运行，不能入基准" });
            }
            if (!registry.TryGet(confirmation.GameId, out var facade)
                || run.AdapterHash != facade.Registration.AdapterHash)
            {
                return Results.BadRequest(new { message = "适配层已变更（指纹不一致），需重新验证后再入基准" });
            }
            var tape = tapes.FindByRun(runId);
            var (baselineId, already) = baselines.Insert(
                confirmation.GameId, confirmation.LevelId, confirmation.LevelHash,
                facade.Registration.AdapterVersion, run.AdapterHash, run.KernelVer, run.ContractVer,
                confirmation.GameBuildFingerprint, run.RunConfigHash,
                run.Status, run.Optimality, run.GraphComplete, run.TerminationReason,
                run.SolutionLength, run.StatesExplored,
                tape?.TrajectoryHash ?? "", tape?.ArtifactHash,
                $"confirmation:{id}", body.ApprovedBy);
            return Results.Ok(new ApproveResponseDto(baselineId, already));
        });

        // 基准列表（门3 diff 对照物；第11步消费）
        app.MapGet("/api/games/{gameId}/baselines", (string gameId, BaselineStore baselines) =>
        {
            var list = baselines.ListByGame(gameId).Select(b => new BaselineDto(
                b.Id, b.GameId, b.LevelId, b.LevelHash, b.AdapterVer, b.AdapterHash, b.KernelVer,
                b.Status, b.Optimality, b.GraphComplete, b.TerminationReason,
                b.SolutionLength, b.StatesExplored, b.TrajectoryHash,
                b.ExecutionConfirmationRef, b.ApprovedBy, b.ApprovedAt, b.CreatedAt)).ToList();
            return Results.Ok(list);
        });
    }
}
