using LevelForge.Platform.Database;
using LevelForge.Platform.Generation;

namespace LevelForge.Platform.Api;

// 生成端点：发起生成（同步跑完整轮重生循环）、运行历史查询
// 流水线实时日志推送到前端留给第13步（SSE/轮询届时定）；本轮先返回完整候选记录
public static class GenerationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 发起生成：LLM 出题 → 校验 → 判卷 → 重生，同步返回整轮结论
        app.MapPost("/api/generate", async (GenerateRequestDto req, GenerationPipeline pipeline,
            LevelForge.Platform.Facades.GameRegistry registry, HttpContext http) =>
        {
            // 未知玩法与 solve 端点口径一致：404（其余请求校验才是 400）
            if (string.IsNullOrWhiteSpace(req.GameId) || !registry.TryGet(req.GameId, out _))
            {
                return Results.NotFound(new { message = $"未知玩法：{req.GameId}" });
            }
            try
            {
                var summary = await pipeline.RunAsync(
                    new GenerationRequest(
                        req.GameId, req.TargetDifficulty, req.LevelNumber,
                        req.SpecialRequirements, req.CandidateLimit, req.SolveTimeoutMs),
                    http.RequestAborted);
                return Results.Ok(ToDto(summary));
            }
            catch (GenerationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 生成运行详情（历史记录/失败原因回看）
        app.MapGet("/api/generation/runs/{runId}", (long runId, GenerationStore store, SolveRunStore runs) =>
        {
            var run = store.GetRun(runId);
            if (run is null)
            {
                return Results.NotFound(new { message = $"生成运行不存在：{runId}" });
            }
            var candidates = store.ListCandidates(runId);
            var dto = new GenerationRunDto(
                run.Id, run.GameId, run.Status, run.TargetDifficulty,
                run.LevelNumber, run.SpecialRequirements,
                run.CandidateLimit, run.SolveTimeoutMs, run.PromptVersion,
                candidates.Select(c => new CandidateAttemptDto(
                    c.AttemptIndex, c.ContentJson, c.FailureClass, c.FailureDetail,
                    c.DifficultyBand, c.DifficultyScore, c.SolveRunId,
                    c.SolveRunId is { } id ? runs.Get(id)?.TapeId : null,
                    LlmCalls: 0, c.ElapsedMs)).ToList(),
                AdoptedAttemptOf(run, candidates), run.FailureSummary, RunElapsedMs(run));
            return Results.Ok(dto);
        });
    }

    #region 内部工具

    // 运行摘要 → DTO（TapeId 已在尝试记录里，直接映射）
    private static GenerationRunDto ToDto(GenerationRunSummary s)
    {
        return new GenerationRunDto(
            s.RunId, s.GameId, s.Status, s.TargetDifficulty,
            s.LevelNumber, s.SpecialRequirements,
            s.CandidateLimit, s.SolveTimeoutMs, s.PromptVersion,
            s.Candidates.Select(c => new CandidateAttemptDto(
                c.AttemptIndex, c.ContentJson, c.FailureClass, c.FailureDetail,
                c.DifficultyBand, c.DifficultyScore, c.SolveRunId, c.TapeId,
                c.LlmCalls, c.ElapsedMs)).ToList(),
            s.AdoptedAttempt, s.FailureSummary, s.TotalElapsedMs);
    }

    // 被采纳尝试序号：按 adopted_candidate_id 找到对应候选行
    private static int? AdoptedAttemptOf(GenerationRunRow run, IReadOnlyList<GenerationCandidateRow> candidates)
    {
        return run.AdoptedCandidateId is { } id
            ? candidates.FirstOrDefault(c => c.Id == id)?.AttemptIndex
            : null;
    }

    // 运行总耗时：从起止时间戳还原（历史查询无实时计时）；未收口按 0
    private static long RunElapsedMs(GenerationRunRow run)
    {
        if (run.FinishedAt is { } finished
            && DateTime.TryParse(finished, out var end)
            && DateTime.TryParse(run.StartedAt, out var start))
        {
            return (long)(end - start).TotalMilliseconds;
        }
        return 0;
    }

    #endregion
}
