using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Platform.Database;
using LevelForge.Platform.Gates;
using LevelForge.Kernel.Replay;

namespace LevelForge.Platform.Api;

// 三道门端点：门1运行卡 / 门2对拍 / 门3基准 diff + 验证记录查询（验证门 §2）
public static class GateEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 门1：五项合规检查（依赖扫描/键判别/纯函数/确定性/内核自测），全绿零豁免
        app.MapPost("/api/games/{gameId}/gates/1/run", (string gameId, GateService gates) =>
        {
            try
            {
                var result = gates.RunGate1(gameId);
                return Results.Ok(new Gate1RunResponseDto(
                    result.RecordId, result.Passed,
                    JsonSerializer.SerializeToNode(result.Report, TapeJson.Options)));
            }
            catch (GateException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 门2：真机对拍单关（子进程+文件交换；玩具不适用）
        app.MapPost("/api/games/{gameId}/gates/2/run", (string gameId, Gate2RunRequestDto req, GateService gates) =>
        {
            try
            {
                var result = gates.RunGate2(gameId, req.LevelId ?? "");
                return Results.Ok(new Gate2RunResponseDto(
                    result.RecordId, result.Passed, result.Message, result.LevelId, result.LevelHash,
                    result.Verdict?.FirstDivergenceStep, result.Verdict?.DivergenceDetail,
                    result.Verdict?.OutcomeMatch, result.Verdict?.TapeOutcome, result.Verdict?.HarnessOutcome,
                    result.GameBuildFingerprint));
            }
            catch (GateNotApplicableException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (GateException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 门3：全量回归（基准 diff；新跑结果不自动入基准）
        app.MapPost("/api/games/{gameId}/gates/3/run", (string gameId, GateService gates) =>
        {
            try
            {
                var result = gates.RunGate3(gameId);
                return Results.Ok(new Gate3RunResponseDto(
                    result.RecordId, result.Total, result.Matched, result.Red, result.Yellow, result.NewLevels,
                    result.Rows.Select(r => new Gate3RowDto(
                        r.LevelId, r.LevelHash, r.Verdict, r.Status, r.BaselineStatus,
                        r.SolutionLength, r.BaselineSolutionLength, r.TerminationReason, r.Notes)).ToList()));
            }
            catch (GateException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 验证记录查询（可按门过滤）：?gate=gate1|gate2|gate3
        app.MapGet("/api/games/{gameId}/verification-records",
            (string gameId, string? gate, VerificationRecordStore records) =>
        {
            var list = records.List(gameId, gate).Select(r => new VerificationRecordDto(
                r.Id, r.GameId, r.Gate, r.Verdict,
                r.LevelHash, r.AdapterHash, r.KernelVer, r.ContractVer,
                r.RuleMatrixHash, r.GameBuildFingerprint, r.HarnessHash,
                r.SemanticSchemaVer, r.SamplesJson, r.SummaryJson, r.ExecutedAt)).ToList();
            return Results.Ok(list);
        });
    }
}
