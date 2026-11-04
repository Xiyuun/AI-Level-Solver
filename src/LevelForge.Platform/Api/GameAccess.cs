using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Api;

// 玩法访问护栏：区分"未知玩法"与"已导入但适配层未接入"
// 导入项目（三轮计划 N10/N11）落库时不带门面——生成/求解/采纳/门1 对这类玩法必须给出可操作的提示，
// 而不是笼统的 404 未知玩法（那会让用户以为导入失败了）
public static class GameAccess
{
    public const string FacadelessMessage = "该玩法的适配层尚未接入：请到「接入向导」完成适配层制作并重新构建平台后重试";

    // 解析玩法门面：已注册返回门面；未知玩法与空 id 由调用方按原口径处理（返回 null, null）
    public static (IGameFacade? Facade, IResult? Problem) Resolve(
        string? gameId, GameStore games, GameRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return (null, null);   // 空 id：交给调用方原有的参数校验
        }
        if (registry.TryGet(gameId, out var facade))
        {
            return (facade, null);
        }
        // 库里有行但没有门面 = 已导入未接入
        if (games.FindGame(gameId) is not null)
        {
            return (null, Results.Conflict(new { message = FacadelessMessage }));
        }
        return (null, null);   // 未知玩法：交给调用方原有的 404
    }
}
