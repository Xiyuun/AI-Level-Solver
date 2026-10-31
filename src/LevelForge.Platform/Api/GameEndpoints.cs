using System.Text.Json.Nodes;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Api;

// 玩法端点：列表/详情/当前/切换/关卡列表（顶栏玩法切换数据源，方案书 §3 中台实证）
public static class GameEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 玩法列表（含状态标识与关卡数）
        app.MapGet("/api/games", (GameStore games) =>
        {
            var list = games.ListGames().Select(g => new GameSummaryDto(
                g.Id, g.DisplayName, g.Status, GameStatus.DisplayNameOf(g.Status),
                g.AdapterVer, g.KernelVer, g.Gate2Applicable, g.LevelCount)).ToList();
            return Results.Ok(list);
        });

        // 玩法详情：接入产物全量
        app.MapGet("/api/games/{gameId}", (string gameId, GameStore games) =>
        {
            var g = games.FindGame(gameId);
            if (g is null) return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            return Results.Ok(new GameDetailDto(
                g.Id, g.DisplayName, g.Status, GameStatus.DisplayNameOf(g.Status),
                g.AdapterVer, g.AdapterHash, g.KernelVer, g.ContractVer,
                g.LevelsRoot, g.Gate2Applicable, g.LevelCount,
                ParseJson(g.VisualMappingJson), ParseJson(g.MechanicsJson), ParseJson(g.DifficultyWeightsJson),
                g.GameBrief, g.FormatSpec, g.RuleMatrix, g.RegisteredAt, g.UpdatedAt));
        });

        // 当前活跃玩法
        app.MapGet("/api/games/active", (SettingsStore settings) =>
            Results.Ok(new ActiveGameDto(settings.Get("ui.activeGame", ""))));

        // 切换活跃玩法（写设置，全局生效）
        app.MapPut("/api/games/{gameId}/activate", (string gameId, GameStore games, SettingsStore settings) =>
        {
            if (games.FindGame(gameId) is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            settings.Set("ui.activeGame", gameId);
            return Results.Ok(new ActiveGameDto(gameId));
        });

        // 玩法关卡列表
        app.MapGet("/api/games/{gameId}/levels", (string gameId, GameStore games) =>
        {
            if (games.FindGame(gameId) is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            var list = games.ListLevels(gameId)
                .Select(l => new LevelDto(l.LevelId, l.LevelHash, l.Source, l.CreatedAt)).ToList();
            return Results.Ok(list);
        });
    }

    #region 内部工具

    // 存库 JSON 串 → JSON 对象；损坏时降级为原文字符串值（不因一条坏产物挂掉详情页）
    private static JsonNode? ParseJson(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    #endregion
}
