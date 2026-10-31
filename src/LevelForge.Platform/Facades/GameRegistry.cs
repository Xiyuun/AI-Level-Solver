namespace LevelForge.Platform.Facades;

// 玩法注册表：静态注册适配层的运行时目录（冻结决策：不做动态装载，换玩法=改这里+重新构建）
// "产物上传"流程（接入向导）最终也落到此表：文件进 src/Adapters/ + 此处登记
public sealed class GameRegistry
{
    // 单例：平台 DI 注册用
    public static GameRegistry Instance { get; } = new();

    private readonly Dictionary<string, IGameFacade> _games = new()
    {
        ["sokoban-toy"] = new SokobanToyFacade()
        // 第16步：["slide-pig"] = new SlidePigFacade(...)
    };

    public IReadOnlyCollection<IGameFacade> Facades => _games.Values;

    // 按标识取门面；未注册返回 false
    public bool TryGet(string gameId, out IGameFacade facade)
    {
        return _games.TryGetValue(gameId, out facade!);
    }
}
