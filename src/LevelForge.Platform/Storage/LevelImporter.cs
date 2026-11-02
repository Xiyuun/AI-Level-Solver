using System.Text;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Storage;

// 关卡导入结论：逐关结果 + 汇总（导入是接入向导③"历史关卡导入"的实现体）
public sealed record LevelImportResult(
    int Imported,                                       // 成功导入数
    int SkippedUnsupported,                             // v1 子集外跳过数（如实统计，不静默）
    int Failed,                                         // 解析/校验失败数
    IReadOnlyList<string> Messages);                    // 逐关消息（前若干条，防刷屏）

// 原生关卡导入器：从游戏项目的关卡目录读 JSON，静态校验后入库（接入向导 §3 产出物 #3）
// 只读源目录 + 只写平台库：不改动游戏项目任何文件；内容哈希随导入登记
public sealed class LevelImporter
{
    private readonly GameStore _games;
    private readonly GameRegistry _registry;

    public LevelImporter(GameStore games, GameRegistry registry)
    {
        _games = games;
        _registry = registry;
    }

    // 导入目录下全部 *.json（按文件名排序，编号即文件名）；重复导入幂等（同 levelHash 不重复计）
    public LevelImportResult ImportDirectory(string gameId, string directory)
    {
        if (!_registry.TryGet(gameId, out var facade))
        {
            throw new InvalidOperationException($"未知玩法：{gameId}");
        }
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"关卡目录不存在：{directory}");
        }
        var imported = 0;
        var unsupported = 0;
        var failed = 0;
        var messages = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f))
        {
            var json = File.ReadAllText(file);
            var levelId = facade.TryGetLevelId(json);
            var errors = facade.FormatErrors(json);
            // 子集外类型与格式错误分开统计（前者是范围声明，后者是数据问题）
            if (errors.Count > 0)
            {
                if (errors.Any(e => e.Contains("v1 支持子集")))
                {
                    unsupported++;
                    messages.Add($"{Path.GetFileName(file)}：子集外类型，跳过（{errors.First(e => e.Contains("v1"))}）");
                }
                else
                {
                    failed++;
                    messages.Add($"{Path.GetFileName(file)}：校验失败（{errors[0]}）");
                }
                continue;
            }
            if (levelId is null)
            {
                failed++;
                messages.Add($"{Path.GetFileName(file)}：无法提取关卡编号");
                continue;
            }
            var hash = TapeHashes.LevelHash(Encoding.UTF8.GetBytes(json));
            _games.UpsertLevel(gameId, levelId, hash, json, "imported");
            imported++;
        }
        return new LevelImportResult(imported, unsupported, failed, messages.Take(20).ToList());
    }
}
