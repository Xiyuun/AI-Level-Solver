using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Api;

// 项目导入端点（三轮计划 N10，用户期望流程第②步）：
// 选取设备上某一目录 → 识别为 Cocos/Unity 项目 → 目录名即项目名 → 落库为"接入中"玩法并进入接入向导
public static class ProjectEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 导入：目录不存在 400 / 引擎不识别 400 / 目录名撞名 409 / 同目录重复导入幂等返回 200
        app.MapPost("/api/projects/import", (ProjectImportRequestDto req, ProjectImporter importer) =>
        {
            if (string.IsNullOrWhiteSpace(req.Directory))
            {
                return Results.BadRequest(new { message = "缺少 directory 字段（项目目录绝对路径）" });
            }
            try
            {
                var outcome = importer.Import(req.Directory);
                return Results.Ok(new ProjectImportResultDto(
                    outcome.GameId, outcome.DisplayName, outcome.EngineType,
                    outcome.ProjectRoot, outcome.AlreadyImported));
            }
            catch (ProjectImportException ex)
            {
                var isConflict = ex.Message.Contains("撞名", StringComparison.Ordinal);
                return isConflict
                    ? Results.Conflict(new { message = ex.Message })
                    : Results.BadRequest(new { message = ex.Message });
            }
        });

        // 最近导入目录（前端快捷回填）
        app.MapGet("/api/projects/recent", (ProjectImporter importer) =>
            Results.Ok(importer.RecentDirectories()));

        // 重新扫描（向导内"项目内容变了"时触发；画像全文随玩法详情下发）
        app.MapPost("/api/games/{gameId}/rescan", (string gameId, ProjectImporter importer, GameStore games) =>
        {
            if (games.FindGame(gameId) is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            try
            {
                var profile = importer.Rescan(gameId);
                return Results.Ok(new ProjectRescanResultDto(gameId, profile.Engine, profile.ProjectRoot));
            }
            catch (ProjectImportException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        // 关卡根目录确认（向导①：用户从扫描候选中选定后回填）
        app.MapPut("/api/games/{gameId}/levels-root", (string gameId, LevelsRootRequestDto body, GameStore games) =>
        {
            if (games.FindGame(gameId) is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            if (string.IsNullOrWhiteSpace(body.LevelsRoot))
            {
                return Results.BadRequest(new { message = "缺少 levelsRoot" });
            }
            return games.UpdateLevelsRoot(gameId, body.LevelsRoot.Trim())
                ? Results.Ok(new { levelsRoot = body.LevelsRoot.Trim() })
                : Results.NotFound(new { message = $"未知玩法：{gameId}" });
        });

        // 删除导入项目（从0重测）：仅限导入的、未注册适配层、未转正的玩法；级联清全部登记数据
        app.MapDelete("/api/projects/{gameId}", (string gameId, ProjectImporter importer, GameStore games,
            GameRegistry registry, SettingsStore settings) =>
        {
            var game = games.FindGame(gameId);
            if (game is null)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            if (registry.TryGet(gameId, out _))
            {
                return Results.Conflict(new { message = "该玩法适配层已注册（内置或已接入），不可删除" });
            }
            if (string.IsNullOrWhiteSpace(game.ProjectRoot))
            {
                return Results.Conflict(new { message = "该玩法不是导入的项目，不可删除" });
            }
            if (game.Status == "production")
            {
                return Results.Conflict(new { message = "已转正玩法不可直接删除：先回退状态再删除" });
            }
            var (deleted, tapeFiles) = games.DeleteImportedGame(gameId);
            if (!deleted)
            {
                return Results.NotFound(new { message = $"未知玩法：{gameId}" });
            }
            // 磁带文件尽力而为清理
            var tapeDir = Path.Combine(importer.TapeDirectory(), "tapes");
            var removed = 0;
            foreach (var f in tapeFiles)
            {
                try
                {
                    var full = Path.IsPathRooted(f) ? f : Path.Combine(tapeDir, Path.GetFileName(f));
                    if (File.Exists(full)) { File.Delete(full); removed++; }
                }
                catch (IOException) { }
            }
            // 指针清理：活跃玩法指向被删玩法则回落；最近导入列表去掉该项目目录
            var active = settings.Get("ui.activeGame", "") ?? "";
            if (active == gameId)
            {
                var fallback = games.ListGames().FirstOrDefault()?.Id ?? "";
                if (fallback.Length > 0) settings.Set("ui.activeGame", fallback);
            }
            var recent = importer.RecentDirectories().Where(d => d != game.ProjectRoot).ToList();
            settings.Set("ui.recentProjects", recent);
            return Results.Ok(new { deleted = true, tapeFilesRemoved = removed });
        });
    }
}
