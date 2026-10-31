using System.Collections.Concurrent;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Generation;

// few-shot 采样：从该项目历史关卡中按同难度采样 2~3 个（生成端出题分析 §2/§5：随机抽样避免同质化）
// 难度档用平台自己的难度模型对存量关求解得出（玩具全库毫秒级）；进程内缓存按 levelHash 复用
// 真实大项目可换落库缓存，接口不变（后置优化）
public sealed class FewShotSelector
{
    private const int SampleCount = 3;   // 2~3 个示例，取上限

    private readonly GameStore _games;

    // 进程内难度档缓存：levelHash → band（0=无解/未知，不会命中 1~3 目标档）
    private readonly ConcurrentDictionary<string, int> _bandCache = new();

    public FewShotSelector(GameStore games)
    {
        _games = games;
    }

    // 采样：优先取与目标难度同档的可解历史关；同档不足时回退到全部可解关（不阻塞生成）
    public IReadOnlyList<string> Sample(IGameFacade facade, int targetBand, Random? random = null)
    {
        random ??= Random.Shared;
        var levels = _games.ListLevels(facade.Registration.GameId);
        var solved = new List<(string Json, int Band)>();
        foreach (var level in levels)
        {
            var band = _bandCache.GetOrAdd(level.LevelHash, _ => SolveBand(facade, level.ContentJson));
            if (band >= 1)
            {
                solved.Add((level.ContentJson, band));
            }
        }
        var sameBand = solved.Where(l => l.Band == targetBand).ToList();
        var pool = sameBand.Count > 0 ? sameBand : solved;   // 同档空则回退全可解关
        return pool
            .OrderBy(_ => random.Next())
            .Take(SampleCount)
            .Select(l => l.Json)
            .ToList();
    }

    #region 内部工具

    // 求解一关取难度档；采样求解不落库——内部选例不是对用户承诺的验证运行
    private static int SolveBand(IGameFacade facade, string levelJson)
    {
        var outcome = facade.Solve(new SolveCommand(
            levelJson, System.Text.Encoding.UTF8.GetBytes(levelJson), "few-shot"));
        return outcome.Result.Status == SolveStatus.Solved ? outcome.Difficulty.Band : 0;
    }

    #endregion
}
