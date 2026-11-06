using System.Text.Json.Nodes;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Api;

// 玩法端点：列表/详情/当前/切换/关卡列表（顶栏玩法切换数据源，方案书 §3 中台实证）
public static class GameEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 玩法列表（含状态标识与关卡数；AdapterReady 供前端区分"已接入/接入中"）
        app.MapGet("/api/games", (GameStore games, GameRegistry registry) =>
        {
            var list = games.ListGames().Select(g => new GameSummaryDto(
                g.Id, g.DisplayName, g.Status, GameStatus.DisplayNameOf(g.Status),
                g.AdapterVer, g.KernelVer, g.Gate2Applicable, g.LevelCount,
                registry.TryGet(g.Id, out _), g.EngineType)).ToList();
            return Results.Ok(list);
        });

        // 玩法详情：接入产物全量 + 扫描画像（导入项目）
        app.MapGet("/api/games/{gameId}", (string gameId, GameStore games, GameRegistry registry, AppDatabase db) =>
        {
            var g = games.FindGame(gameId);
            if (g is null) return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            return Results.Ok(new GameDetailDto(
                g.Id, g.DisplayName, g.Status, GameStatus.DisplayNameOf(g.Status),
                g.AdapterVer, g.AdapterHash, g.KernelVer, g.ContractVer,
                g.LevelsRoot, g.Gate2Applicable, g.LevelCount,
                ParseJson(g.VisualMappingJson), ParseJson(g.MechanicsJson), ParseJson(g.DifficultyWeightsJson),
                g.GameBrief, g.FormatSpec, g.RuleMatrix, g.RegisteredAt, g.UpdatedAt,
                registry.TryGet(g.Id, out _), g.EngineType, g.ProjectRoot,
                g.ProjectProfileJson is null ? null : ParseJson(g.ProjectProfileJson),
                g.ProjectAnalysisJson is null ? null : ParseJson(g.ProjectAnalysisJson),
                g.ProjectRoot is null ? null : Path.Combine(db.DataDir, "plugins", g.Id),
                g.CodeRoot));
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

        // 原生关卡导入（接入向导③：从游戏项目关卡目录批量入库；幂等，子集外如实跳过并计数）
        app.MapPost("/api/games/{gameId}/import-levels", async (
            string gameId, Storage.LevelImporter importer, GameStore games, GameRegistry registry, HttpContext http) =>
        {
            if (games.FindGame(gameId) is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            // 关卡导入需要适配层的格式校验器：未接入的玩法给可操作的提示而不是笼统报错
            var (_, problem) = GameAccess.Resolve(gameId, games, registry);
            if (problem is not null) return problem;
            ImportRequestDto? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<ImportRequestDto>();
            }
            catch (System.Text.Json.JsonException)
            {
                body = null;
            }
            if (body is null || string.IsNullOrWhiteSpace(body.Directory))
            {
                return Results.BadRequest(new { message = "缺少 directory 字段（游戏项目关卡目录绝对路径）" });
            }
            try
            {
                var result = importer.ImportDirectory(gameId, body.Directory);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
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
