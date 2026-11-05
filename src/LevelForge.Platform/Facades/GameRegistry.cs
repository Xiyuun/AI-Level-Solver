namespace LevelForge.Platform.Facades;

// 玩法注册表：编译期静态注册（内置参考玩法）+ 运行期插件动态注册（三轮 D7 解冻）。
// 插件：数据目录 plugins/{gameId}/ 下的门面程序集，由 PluginLoader 在启动时与向导"检测适配层"时装入。
// 换内置玩法仍是改这里+重新构建；插件玩法全程零构建。
public sealed class GameRegistry
{
    // 单例：平台 DI 注册用
    public static GameRegistry Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, IGameFacade> _games = new()
    {
        ["sokoban-toy"] = new SokobanToyFacade()
        // slide-pig 已按三轮计划移出注册表（2026-11-04）：真实 PiggyFarm 项目改走「导入项目」流程，
        // 适配层由接入向导指引外部 AI 产出后经插件装载进入本表。
        // SlidePigFacade/Adapters.SlidePig 源码保留：作为 PiggyFarm 适配层的参考实现与内核搜索测试载体。
    };

    public IReadOnlyCollection<IGameFacade> Facades
    {
        get { lock (_lock) return _games.Values.ToArray(); }
    }

    // 按标识取门面；未注册返回 false
    public bool TryGet(string gameId, out IGameFacade facade)
    {
        lock (_lock)
        {
            return _games.TryGetValue(gameId, out facade!);
        }
    }

    // 运行期动态注册（插件装载）：已存在时覆盖（适配层升级重检测场景），线程安全
    public void RegisterDynamic(IGameFacade facade)
    {
        lock (_lock)
        {
            _games[facade.Registration.GameId] = facade;
        }
    }
}
