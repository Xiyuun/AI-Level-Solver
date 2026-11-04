using LevelForge.Platform.Database;
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
    }
}
