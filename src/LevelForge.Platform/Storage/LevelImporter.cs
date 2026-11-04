using System.Text;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Storage;

// 单关导入结论：三栏面板（成功/跳过/失败）的数据源，逐关可查
public sealed record LevelImportEntry(
    string File,                                        // 源文件名
    string? LevelId,                                    // 能提取到的关卡编号（失败时可能为 null）
    string Outcome,                                     // imported | skipped | failed
    string? Reason);                                    // 跳过/失败原因；成功为 null

// 关卡导入结论：逐关明细 + 汇总（导入是接入向导③"历史关卡导入"的实现体）
// 明细的动机：过去只返回计数与"前 20 条消息"，110 关里 35 关被跳过却说不清是哪 35 关、为什么
public sealed record LevelImportResult(
    int Imported,                                       // 成功导入数
    int SkippedUnsupported,                             // v1 子集外跳过数（如实统计，不静默）
    int Failed,                                         // 解析/校验失败数
    IReadOnlyList<LevelImportEntry> Entries,            // 逐关明细（前端三栏 + 导出跳过清单）
    IReadOnlyList<string> Messages);                    // 兼容旧口径的摘要行（前 20 条）

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
        var entries = new List<LevelImportEntry>();
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            var json = File.ReadAllText(file);
            var levelId = facade.TryGetLevelId(json);
            var errors = facade.FormatErrors(json);
            // 子集外类型与格式错误分开统计（前者是范围声明，后者是数据问题）
            if (errors.Count > 0)
            {
                if (errors.Any(e => e.Contains("v1 支持子集")))
                {
                    unsupported++;
                    var reason = errors.First(e => e.Contains("v1"));
                    messages.Add($"{name}：子集外类型，跳过（{reason}）");
                    entries.Add(new LevelImportEntry(name, levelId, "skipped", reason));
                }
                else
                {
                    failed++;
                    messages.Add($"{name}：校验失败（{errors[0]}）");
                    entries.Add(new LevelImportEntry(name, levelId, "failed", errors[0]));
                }
                continue;
            }
            if (levelId is null)
            {
                failed++;
                messages.Add($"{name}：无法提取关卡编号");
                entries.Add(new LevelImportEntry(name, null, "failed", "无法提取关卡编号"));
                continue;
            }
            var hash = TapeHashes.LevelHash(Encoding.UTF8.GetBytes(json));
            _games.UpsertLevel(gameId, levelId, hash, json, "imported");
            imported++;
            entries.Add(new LevelImportEntry(name, levelId, "imported", null));
        }
        return new LevelImportResult(imported, unsupported, failed, entries, messages.Take(20).ToList());
    }
}
