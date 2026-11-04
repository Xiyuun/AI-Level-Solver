using LevelForge.Platform.Batch;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Api;

// 批量生产端点（表现层_批量生产 §2 功能全景的 API 层）
// 前端页面可关：任务在后端队列执行，单机进程在任务就在
public static class BatchEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 发起批量任务
        app.MapPost("/api/batch/start", async (BatchRequestDto req, BatchService batch,
            GameRegistry registry, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(req.GameId) || !registry.TryGet(req.GameId, out _))
            {
                return Results.NotFound(new { message = $"未知玩法：{req.GameId}" });
            }
            try
            {
                var status = batch.Start(new BatchRequest(
                    req.GameId, req.FromLevel, req.ToLevel,
                    req.Difficulty1Count, req.Difficulty2Count, req.Difficulty3Count,
                    req.SpecialRequirements, req.Concurrency, req.CandidateLimit, req.SolveTimeoutMs));
                return Results.Ok(status);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 查询任务状态（前端轮询）
        app.MapGet("/api/batch/{taskId}/status", (long taskId, BatchService batch) =>
        {
            var status = batch.GetStatus(taskId);
            return status is null
                ? Results.NotFound(new { message = $"批量任务不存在：{taskId}" })
                : Results.Ok(status);
        });

        // 取消任务（排队关不再执行、进行中的做完当前关）
        app.MapPost("/api/batch/{taskId}/cancel", (long taskId, BatchService batch) =>
        {
            return batch.Cancel(taskId)
                ? Results.Ok(new { message = "取消指令已发出（进行中的关会做完）" })
                : Results.NotFound(new { message = $"批量任务不存在：{taskId}" });
        });

        // 批量入库：指定编号段内已生成的关写入游戏项目目录
        app.MapPost("/api/batch/{taskId}/adopt", (long taskId, BatchAdoptDto req, BatchService batch) =>
        {
            var results = batch.AdoptRange(taskId, req.FromLevel, req.ToLevel, req.Overwrite);
            return Results.Ok(new
            {
                total = results.Count,
                ok = results.Count(r => r.Ok),
                failed = results.Count(r => !r.Ok),
                details = results.Select(r => new { r.LevelNumber, r.Ok, r.Error }).ToList()
            });
        });

        // 导出全部成功关（JSON 数组；前端组 zip 或直接下载）
        app.MapGet("/api/batch/{taskId}/export", (long taskId, BatchService batch) =>
        {
            var items = batch.ExportSolved(taskId);
            return Results.Ok(items.Select(i => new { i.LevelNumber, i.ContentJson }).ToList());
        });

        // 重试失败关
        app.MapPost("/api/batch/{taskId}/retry", (long taskId, BatchService batch) =>
        {
            var status = batch.RetryFailed(taskId);
            return status is null
                ? Results.NotFound(new { message = $"批量任务不存在：{taskId}" })
                : Results.Ok(status);
        });
    }
}

// 批量任务请求 DTO
public sealed record BatchRequestDto(
    string GameId,
    int FromLevel,
    int ToLevel,
    int Difficulty1Count,
    int Difficulty2Count,
    int Difficulty3Count,
    string? SpecialRequirements = null,
    int? Concurrency = null,
    int? CandidateLimit = null,
    long? SolveTimeoutMs = null);

// 批量入库请求
public sealed record BatchAdoptDto(
    int FromLevel,
    int ToLevel,
    bool Overwrite = false);
