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
        // slide-pig 已按三轮计划移出注册表（2026-11-04）：真实 PiggyFarm 项目改走「导入项目」流程，
        // 适配层由接入向导指引外部 AI 产出后在此登记（换玩法=改这里+重新构建，冻结决策不变）。
        // SlidePigFacade/Adapters.SlidePig 源码保留：作为 PiggyFarm 适配层的参考实现与内核搜索测试载体。
    };

    public IReadOnlyCollection<IGameFacade> Facades => _games.Values;

    // 按标识取门面；未注册返回 false
    public bool TryGet(string gameId, out IGameFacade facade)
    {
        return _games.TryGetValue(gameId, out facade!);
    }
}
