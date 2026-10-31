using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Api;

// 试玩步进请求：从初态执行完整动作序列（无会话设计，前端每步带全量重放）
public sealed record PlayStepRequestDto(string? GameId, string? LevelJson, JsonArray? Moves);

// 试玩局面响应
public sealed record PlayStateDto(
    JsonNode Snapshot,
    IReadOnlyList<PlayMoveDto> LegalMoves,
    bool IsGoal,
    int MovesExecuted);

// 合法动作项
public sealed record PlayMoveDto(JsonNode Move, string MoveText);

// 试玩端点：后端步进（渲染规范 §6 MVP 形态——前端发结构化 move，服务在适配层执行返回快照）
// 与求解同一份适配层代码；moveText 仅展示
public static class PlayEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 开局：加载关卡 → 初始快照 + 合法动作
        app.MapPost("/api/play/start", (PlayStepRequestDto req, GameRegistry registry) =>
            Handle(registry, req.GameId, req.LevelJson, []));

        // 步进：带完整动作序列重放到当前局（每步请求都从初态执行，天然可回退——前端减序列即可）
        app.MapPost("/api/play/step", (PlayStepRequestDto req, GameRegistry registry) =>
            Handle(registry, req.GameId, req.LevelJson, req.Moves ?? []));
    }

    #region 内部工具

    // 统一处理：玩法/关卡校验 → 门面执行 → 序列化
    private static IResult Handle(GameRegistry registry, string? gameId, string? levelJson, JsonArray? moves)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !registry.TryGet(gameId, out var facade))
        {
            return Results.NotFound(new { message = $"未知玩法：{gameId}" });
        }
        if (string.IsNullOrWhiteSpace(levelJson))
        {
            return Results.BadRequest(new { message = "缺少关卡内容 levelJson" });
        }
        try
        {
            var moveList = moves?.Cast<JsonNode>().ToList() ?? [];
            var state = facade.Play(levelJson, moveList);
            return Results.Ok(new PlayStateDto(
                JsonSerializer.SerializeToNode(state.Snapshot, TapeJsonOptions())!,
                state.LegalMoves.Select(m => new PlayMoveDto(m.Move.DeepClone(), m.MoveText)).ToList(),
                state.IsGoal,
                state.MovesExecuted));
        }
        catch (PlayException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    // 快照序列化：与磁带一致（camelCase）
    private static JsonSerializerOptions TapeJsonOptions() => LevelForge.Kernel.Replay.TapeJson.Options;

    #endregion
}
