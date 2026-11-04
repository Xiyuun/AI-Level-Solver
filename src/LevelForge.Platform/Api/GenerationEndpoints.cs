using System.Text.Json;
using LevelForge.Platform.Database;
using LevelForge.Platform.Generation;

namespace LevelForge.Platform.Api;

// 生成端点：同步整轮生成（保留兼容）、运行历史查询、异步任务 + SSE 实时进度
// 异步通道是工作台主路径（前后端全流程优化 §8.4）；同步接口留给脚本与既有测试
public static class GenerationEndpoints
{
    // SSE 手工序列化口径：与全局 API 一致（camelCase）
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    public static void Map(IEndpointRouteBuilder app)
    {
        // 发起生成：LLM 出题 → 校验 → 判卷 → 重生，同步返回整轮结论
        app.MapPost("/api/generate", async (GenerateRequestDto req, GenerationPipeline pipeline,
            LevelForge.Platform.Facades.GameRegistry registry, GameStore games, HttpContext http) =>
        {
            // 未知玩法与 solve 端点口径一致：404；已导入未接入的玩法给可操作的 409
            var (_, problem) = GameAccess.Resolve(req.GameId, games, registry);
            if (problem is not null) return problem;
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

        // ---- 异步生成任务（前后端全流程优化 §8.4）----
        // 动机：整轮生成可达数十秒到数分钟，同步接口会让前端死屏；改为立即返回任务号 + SSE 实时进度 + 可取消

        // 发起：202 + 任务号（请求校验错误也落成任务终态，不阻塞调用方）
        app.MapPost("/api/generate/jobs", (GenerateRequestDto req, GenerationJobService jobs,
            LevelForge.Platform.Facades.GameRegistry registry, GameStore games) =>
        {
            var (_, problem) = GameAccess.Resolve(req.GameId, games, registry);
            if (problem is not null) return problem;
            if (string.IsNullOrWhiteSpace(req.GameId) || !registry.TryGet(req.GameId, out _))
            {
                return Results.NotFound(new { message = $"未知玩法：{req.GameId}" });
            }
            var job = jobs.Start(new GenerationRequest(
                req.GameId, req.TargetDifficulty, req.LevelNumber,
                req.SpecialRequirements, req.CandidateLimit, req.SolveTimeoutMs));
            return Results.Accepted($"/api/generate/jobs/{job.JobId}", new GenerationJobStartedDto(job.JobId));
        });

        // 快照：轮询/断线重连用（终态附完整运行记录）
        app.MapGet("/api/generate/jobs/{jobId:long}", (long jobId, GenerationJobService jobs) =>
        {
            var job = jobs.Get(jobId);
            return job is null
                ? Results.NotFound(new { message = $"生成任务不存在：{jobId}" })
                : Results.Ok(ToJobDto(job.Snapshot()));
        });

        // 取消：中断 LLM 调用与求解，任务落 cancelled
        app.MapPost("/api/generate/jobs/{jobId:long}/cancel", (long jobId, GenerationJobService jobs) =>
        {
            var job = jobs.Get(jobId);
            if (job is null)
            {
                return Results.NotFound(new { message = $"生成任务不存在：{jobId}" });
            }
            job.Cancel();
            return Results.Ok(new { message = "已请求取消" });
        });

        // SSE：先回放已发生事件再持续推送，最后一条 event: done 携带完整结论
        app.MapGet("/api/generate/jobs/{jobId:long}/events",
            async (long jobId, GenerationJobService jobs, HttpContext http) =>
        {
            var job = jobs.Get(jobId);
            if (job is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsync($"生成任务不存在：{jobId}");
                return;
            }
            http.Response.ContentType = "text/event-stream; charset=utf-8";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";   // 反向代理不缓冲，保证实时
            try
            {
                await foreach (var evt in job.Subscribe(http.RequestAborted))
                {
                    var payload = JsonSerializer.Serialize(ToEventDto(evt), SseJson);
                    await http.Response.WriteAsync(
                        $"event: progress\nid: {evt.Seq}\ndata: {payload}\n\n", http.RequestAborted);
                    await http.Response.Body.FlushAsync(http.RequestAborted);
                }
                var final = JsonSerializer.Serialize(ToJobDto(job.Snapshot()), SseJson);
                await http.Response.WriteAsync($"event: done\ndata: {final}\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // 客户端断开：正常收场；任务仍在后台跑，可重新订阅
            }
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

    // 进度事件 → DTO
    private static GenerationEventDto ToEventDto(GenerationEvent e)
    {
        return new GenerationEventDto(e.Seq, e.ElapsedMs, e.Stage, e.Message, e.AttemptIndex, e.FailureClass);
    }

    // 任务快照 → DTO（终态时附运行记录）
    private static GenerationJobDto ToJobDto(GenerationJobSnapshot snap)
    {
        return new GenerationJobDto(
            snap.JobId, snap.Status, snap.ElapsedMs,
            snap.Events.Select(ToEventDto).ToList(),
            snap.Run is null ? null : ToDto(snap.Run),
            snap.Error);
    }

    #endregion
}
