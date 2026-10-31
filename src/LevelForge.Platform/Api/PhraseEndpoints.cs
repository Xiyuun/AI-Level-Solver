using LevelForge.Platform.Database;

namespace LevelForge.Platform.Api;

// 短语端点：按玩法分组的特殊要求快捷短语（增删查；生成工作台 §4 设计决定 #1）
public static class PhraseEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // 玩法短语列表
        app.MapGet("/api/games/{gameId}/phrases", (string gameId, PhraseStore phrases) =>
            Results.Ok(phrases.List(gameId)
                .Select(p => new PhraseDto(p.Id, p.Text, p.SortOrder)).ToList()));

        // 新增短语
        app.MapPost("/api/games/{gameId}/phrases", (string gameId, PhraseCreateDto body, PhraseStore phrases) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
            {
                return Results.BadRequest(new { message = "短语内容不能为空" });
            }
            var (id, sortOrder) = phrases.Add(gameId, body.Text.Trim());
            return Results.Ok(new PhraseDto(id, body.Text.Trim(), sortOrder));
        });

        // 删除短语
        app.MapDelete("/api/phrases/{phraseId}", (long phraseId, PhraseStore phrases) =>
            phrases.Delete(phraseId)
                ? Results.NoContent()
                : Results.NotFound(new { message = $"短语不存在：{phraseId}" }));
    }
}
