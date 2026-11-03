// 磁带导出：求解指定关并输出 对拍工作目录（level.json + tape.json），供门2 harness 消费（临时工具）
// 运行：dotnet run probe/DumpTape.cs -- <关卡json路径> <输出目录>

using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using System.Text;

// 文件式应用默认禁用反射序列化——入口第一行打开（必须在任何 JsonSerializerOptions 首次使用前）
AppContext.SetSwitch("System.Text.Json.Serialization.JsonSerializerIsReflectionEnabled", true);

var levelPath = args.Length > 0 ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels\000001.json";
var outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "levelforge-gate2");
Directory.CreateDirectory(outDir);

var levelJson = File.ReadAllText(levelPath);
var level = SlidePigLevel.FromJson(levelJson);
var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slide-pig", "0.1.0", adapter, adapter);

// 稀疏关 A*（1 号关）；稠密关走机动性+预算阶梯（与门面同构的简化版：预算 8→32）
SolveResult result;
if (adapter.LoadLevel(level).Pigs.Count < 40)
{
    result = new AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove>().Solve(bundle,
        new SolveRequest<SlidePigLevel>(level) { NodeBudget = 20_000_000, TimeBudget = TimeSpan.FromSeconds(60) });
}
else
{
    result = new SolveResult(SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
        TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "未运行");
    foreach (var budget in new[] { 8, 16, 32 })
    {
        var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(MobilityTiebreak: true, ShuffleBudget: budget));
        result = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level)
        {
            NodeBudget = 100_000_000, TimeBudget = TimeSpan.FromSeconds(120)
        });
        if (result.Status == SolveStatus.Solved) break;
    }
}

Console.WriteLine($"status={result.Status} 解长={result.Solution.Count}");
if (result.Status != SolveStatus.Solved)
{
    Console.WriteLine("无可执行序列，放弃制带");
    return;
}

var bytes = File.ReadAllBytes(levelPath);
var tape = new ReplayEngine().RecordTape(bundle, level, bytes, result,
    new TapeMetadata(Path.GetFileNameWithoutExtension(levelPath), null, null));

File.Copy(levelPath, Path.Combine(outDir, "level.json"), true);
File.WriteAllText(Path.Combine(outDir, "tape.json"), TapeJson.Serialize(tape), new UTF8Encoding(false));
Console.WriteLine($"已输出：{outDir}\\level.json + tape.json（{tape.Steps.Count} 步，outcome={tape.Outcome}）");
