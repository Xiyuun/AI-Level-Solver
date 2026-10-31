using System.Text;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Api;

// 求解端点：存量关求解 / 质检粘贴求解（同一入口）、磁带获取
// 每次求解落 solve_runs（五维结论+metrics+指纹）；有解另落磁带文件+索引
public static class SolveEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 单关求解：levelId（存量关）或 levelJson（质检粘贴）二选一
        app.MapPost("/api/solve", (SolveRequestDto req, GameRegistry registry, GameStore games, SolveRunStore runs, TapeStore tapes) =>
        {
            if (string.IsNullOrWhiteSpace(req.GameId) || !registry.TryGet(req.GameId, out var facade))
            {
                return Results.NotFound(new { message = $"未知玩法：{req.GameId}" });
            }
            // 求解目标定位：两个都给=歧义，两个都不给=空请求
            if (req.LevelId is not null && req.LevelJson is not null)
            {
                return Results.BadRequest(new { message = "levelId 与 levelJson 只能二选一" });
            }
            string levelJson;
            string levelRef;
            if (req.LevelId is not null)
            {
                var level = games.FindLevel(req.GameId, req.LevelId);
                if (level is null)
                {
                    return Results.NotFound(new { message = $"关卡不存在：{req.LevelId}" });
                }
                levelJson = level.ContentJson;
                levelRef = level.LevelId;
            }
            else if (req.LevelJson is not null)
            {
                // 质检模式：项目原生格式直通、零转换（表现层_生成工作台 §1 验证关卡）
                levelJson = req.LevelJson;
                levelRef = facade.TryGetLevelId(levelJson) is { } id ? $"质检:{id}" : "质检:粘贴";
            }
            else
            {
                return Results.BadRequest(new { message = "缺少求解目标：levelId 或 levelJson" });
            }

            var bytes = Encoding.UTF8.GetBytes(levelJson);   // levelHash 口径：存储/粘贴原文的 UTF-8 字节
            var command = new SolveCommand(
                levelJson, bytes, levelRef,
                req.TimeBudgetMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
                req.NodeBudget, req.MemoryBudgetBytes, req.FullEnumeration);
            var outcome = facade.Solve(command);

            // 落库：先记 solve_run，有磁带再落文件+索引并回填（登记失败不影响已得出的求解结论返回）
            var levelHash = TapeHashes.LevelHash(bytes);
            var runId = runs.Insert(facade.Registration, levelRef, levelHash, outcome);
            long? tapeId = null;
            if (outcome.Tape is not null)
            {
                tapeId = tapes.Save(runId, outcome.Tape).Id;
                runs.AttachTape(runId, tapeId.Value);
            }
            return Results.Ok(ToResponse(runId, req.GameId, levelRef, levelHash, outcome, tapeId));
        });

        // 磁带获取：预览/回放/对拍共用的唯一执行记录；文件原样回传（已是 TapeJson 格式）
        app.MapGet("/api/tapes/{tapeId}", (long tapeId, TapeStore tapes) =>
        {
            var json = tapes.LoadJson(tapeId);
            return json is null
                ? Results.NotFound(new { message = $"磁带不存在：{tapeId}" })
                : Results.Content(json, "application/json");
        });
    }

    #region 内部工具

    // 求解产物 → API 响应（枚举由全局 JSON 选项输出小写字符串）
    private static SolveResponseDto ToResponse(
        long runId, string gameId, string levelRef, string levelHash, PlatformSolve outcome, long? tapeId)
    {
        var r = outcome.Result;
        var m = r.Metrics;
        var d = outcome.Difficulty;
        return new SolveResponseDto(
            runId, gameId, levelRef, levelHash,
            r.Status, r.Optimality, r.GraphComplete, r.TerminationReason, r.MetricsScope,
            r.Solution.Count,
            m is null ? null : new MetricsDto(
                m.StatesExplored, m.UniqueStates, m.FrontierPeak, m.ElapsedMs, m.SolutionLength, m.AvgBranchingFactor),
            new DifficultyDto(d.Band, DifficultyModel.BandName(d.Band), d.Score, d.ModelVersion, d.UsedFeatures, d.Notes),
            tapeId, r.ErrorMessage);
    }

    #endregion
}
