#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 滑动猪批量求解普查：全部关卡 A* 30s 预算，输出 覆盖分布（临时诊断工具）
// 运行：dotnet run probe/SlidePigSurvey.cs [--目录] [--秒]

using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using System.Diagnostics;

var dir = args.Length > 0 && Directory.Exists(args[0]) ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels";
var seconds = 30.0;
var idx = Array.IndexOf(args, "--秒");
if (idx >= 0 && double.TryParse(args[idx + 1], out var s)) seconds = s;

var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slidepig", "survey", adapter, adapter);
var solver = new AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove>();

var solved = 0; var resource = 0; var unsupported = 0; var invalid = 0;
var swAll = Stopwatch.StartNew();
foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
{
    var level = SlidePigLevel.FromJson(File.ReadAllText(file));
    var (_, errors) = SlidePigValidator.ValidateJson(File.ReadAllText(file));
    if (errors.Count > 0)
    {
        var unsupportedType = errors.Any(e => e.Contains("v1"));
        if (unsupportedType) { unsupported++; Console.WriteLine($"{Path.GetFileName(file)} UNSUPPORTED"); }
        else { invalid++; Console.WriteLine($"{Path.GetFileName(file)} INVALID {errors[0]}"); }
        continue;
    }
    var state = adapter.LoadLevel(level);
    var sw = Stopwatch.StartNew();
    var r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level)
    {
        NodeBudget = 20_000_000,
        TimeBudget = TimeSpan.FromSeconds(seconds)
    });
    sw.Stop();
    if (r.Status == SolveStatus.Solved) solved++;
    else if (r.Status == SolveStatus.ResourceLimit) resource++;
    else invalid++;
    Console.WriteLine($"{Path.GetFileName(file)} pigs={state.Pigs.Count,3} {r.Status,-18} " +
        $"{sw.ElapsedMilliseconds,7}ms 解长={r.Solution.Count,3} 展开={r.Metrics?.StatesExplored} 去重={r.Metrics?.UniqueStates}");
}
Console.WriteLine($"==== 共{solved + resource + invalid + unsupported}关：solved={solved} resourceLimit={resource} invalid={invalid} unsupported={unsupported}，总耗时 {swAll.Elapsed.TotalMinutes:F1}min ====");
