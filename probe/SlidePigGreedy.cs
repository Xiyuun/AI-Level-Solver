#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 贪心 DFS 探针：稠密关（61~122 猪）上测"找出任一可解路径"的速度（临时诊断工具）
// 运行：dotnet run probe/SlidePigGreedy.cs -- <关卡json路径> [秒]

using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using System.Diagnostics;

var path = args.Length > 0 ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels\000061.json";
var seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 30;

var level = SlidePigLevel.FromJson(File.ReadAllText(path));
var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slidepig", "probe", adapter, adapter);

Console.WriteLine($"关卡 {level.LevelId}：{level.MapWidth}x{level.MapHeight}，实体 {adapter.LoadLevel(level).Pigs.Count} 只");
var sw = Stopwatch.StartNew();
var result = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>().Solve(bundle,
    new SolveRequest<SlidePigLevel>(level)
    {
        NodeBudget = 100_000_000,
        TimeBudget = TimeSpan.FromSeconds(seconds)
    });
Console.WriteLine($"status={result.Status} optimality={result.Optimality} reason={result.TerminationReason}");
Console.WriteLine($"解长={result.Solution.Count} 展开={result.Metrics?.StatesExplored} 去重={result.Metrics?.UniqueStates} 栈峰={result.Metrics?.FrontierPeak} 耗时={sw.ElapsedMilliseconds}ms");
if (result.Solution.Count > 0)
{
    // 独立回放验证
    var st = adapter.LoadLevel(level);
    foreach (var rec in result.Solution) st = adapter.ApplyMove(st, adapter.DecodeMove(rec.Move));
    Console.WriteLine($"回放验证：{(adapter.IsGoal(st) ? "通关 ✓" : "未通关 ✗（求解器与规则不一致！）")}");
}
