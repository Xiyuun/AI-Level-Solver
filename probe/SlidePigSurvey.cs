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
    // 改进配置：机动性排序 + 腾挪预算小阶梯（8→16→32），每档平分关卡预算
    SolveResult r = new SolveResult(SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
        TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "未运行");
    var tierCap = seconds / 3;
    foreach (var budget in new[] { 8, 16, 32 })
    {
        r = SolveTier(level, budget, tierCap);
        if (r.Status == SolveStatus.Solved) break;
        var remainMs = seconds * 1000 - sw.ElapsedMilliseconds;
        if (remainMs < tierCap * 1000 * 0.2) break;                // 剩余预算不够再跑一档
    }
    sw.Stop();
    if (r.Status == SolveStatus.Solved) solved++;
    else if (r.Status == SolveStatus.ResourceLimit) resource++;
    else invalid++;
    Console.WriteLine($"{Path.GetFileName(file)} pigs={state.Pigs.Count,3} {r.Status,-18} " +
        $"{sw.ElapsedMilliseconds,7}ms 解长={r.Solution.Count,3} 展开={r.Metrics?.StatesExplored} 去重={r.Metrics?.UniqueStates}");
}
Console.WriteLine($"==== 共{solved + resource + invalid + unsupported}关：solved={solved} resourceLimit={resource} invalid={invalid} unsupported={unsupported}，总耗时 {swAll.Elapsed.TotalMinutes:F1}min ====");

// 单档求解（机动性 + 腾挪预算）
SolveResult SolveTier(SlidePigLevel lv, int budget, double sec)
{
    var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
        new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(MobilityTiebreak: true, ShuffleBudget: budget));
    return solver.Solve(bundle, new SolveRequest<SlidePigLevel>(lv)
    {
        NodeBudget = 20_000_000,
        TimeBudget = TimeSpan.FromSeconds(sec)
    });
}
