#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 滑动猪求解探针：全量枚举判定可解性 + 性能测量（临时诊断工具，不进解决方案）
// 运行：仓库根 dotnet run probe/SlidePigProbe.cs -- <关卡json路径> [分钟]

using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Adapters.SlidePig;
using System.Diagnostics;

var path = args.Length > 0 ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels\000001.json";
var minutes = args.Length > 1 && double.TryParse(args[1], out var m) ? m : 10;

var level = SlidePigLevel.FromJson(File.ReadAllText(path));
var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slidepig", "probe", adapter, adapter);

Console.WriteLine($"关卡 {level.LevelId}：{level.MapWidth}x{level.MapHeight}，猪 {level.Pigs.Count} 只");
var sw = Stopwatch.StartNew();
var result = new BfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>().Solve(bundle,
    new SolveRequest<SlidePigLevel>(level)
    {
        NodeBudget = 50_000_000,
        TimeBudget = TimeSpan.FromMinutes(minutes)
    });
Console.WriteLine($"status={result.Status} optimality={result.Optimality} complete={result.GraphComplete} reason={result.TerminationReason}");
Console.WriteLine($"解长={result.Solution.Count} 展开={result.Metrics?.StatesExplored} 去重={result.Metrics?.UniqueStates} 前沿峰={result.Metrics?.FrontierPeak} 耗时={sw.ElapsedMilliseconds}ms");
if (result.Solution.Count > 0)
{
    Console.WriteLine("操作序列：" + string.Join(" → ", result.Solution.Select(s => s.MoveText)));
}
