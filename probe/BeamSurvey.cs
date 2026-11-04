#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 滑动猪策略对拍普查：同一批关卡、同一预算，比较"贪心阶梯（基线）"与"束搜索"
// 运行：dotnet run probe/BeamSurvey.cs -- <关卡目录> [--秒 8] [--mode beam|greedy|both] [--宽 64]
// 目的：验证 M1 的搜索策略改动是否真的提高可解覆盖率（数据说话，不靠感觉）

using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using System.Diagnostics;

var dir = args.Length > 0 && Directory.Exists(args[0]) ? args[0]
    : Path.Combine(Path.GetTempPath(), "lf-levels-export");
double seconds = 8.0;
var idx = Array.IndexOf(args, "--秒");
if (idx >= 0 && double.TryParse(args[idx + 1], out var s)) seconds = s;
var mode = Array.IndexOf(args, "--mode") is var mi && mi >= 0 ? args[mi + 1] : "both";
var width = Array.IndexOf(args, "--宽") is var wi && wi >= 0 ? int.Parse(args[wi + 1]) : 64;
// 重启次数：束枯竭后换随机种子重跑（0 = 单次确定性运行）
var restarts = Array.IndexOf(args, "--重启") is var ri && ri >= 0 ? int.Parse(args[ri + 1]) : 0;
// 只跑指定编号（逗号分隔），空 = 全部
var onlyIdx = Array.IndexOf(args, "--only");
var only = onlyIdx >= 0
    ? args[onlyIdx + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet()
    : null;

var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slidepig", "survey", adapter, adapter);

var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f)
    .Where(f => only is null || only.Contains(LevelIdOf(f)))
    .ToList();
Console.WriteLine($"关卡 {files.Count} 个，预算 {seconds}s/关，模式 {mode}，束宽 {width}");

var greedySolved = 0; var greedyUnknown = 0;
var beamSolved = 0; var beamUnknown = 0;
var swAll = Stopwatch.StartNew();
foreach (var file in files)
{
    var levelId = LevelIdOf(file);
    var text = File.ReadAllText(file);
    var level = SlidePigLevel.FromJson(text);
    var (_, errors) = SlidePigValidator.ValidateJson(text);
    if (errors.Count > 0) { Console.WriteLine($"{levelId,4} INVALID {errors[0]}"); continue; }
    var state = adapter.LoadLevel(level);

    var greedyPart = "";
    if (mode is "both" or "greedy")
    {
        var (status, ms, len) = RunLadder(level, seconds);
        greedyPart = $"阶梯={Short(status),-14}{ms,6}ms 解长={len,3}";
        if (status == SolveStatus.Solved) greedySolved++; else greedyUnknown++;
    }
    var beamPart = "";
    if (mode is "both" or "beam" or "ladder")
    {
        // ladder 模式复刻门面 SolveDense 的三级阶梯（窄束 → 宽束 → 贪心），验证真实链路而非单策略
        var (status, ms, len, detail) = mode == "ladder" ? RunLadderFull(level, seconds) : RunBeam(level, seconds);
        beamPart = $"{(mode == "ladder" ? "门面" : "束"),-4}={Short(status),-14}{ms,6}ms 解长={len,3} {detail}";
        if (status == SolveStatus.Solved) beamSolved++; else beamUnknown++;
    }
    Console.WriteLine($"{levelId,4} pigs={state.Pigs.Count,3}  {greedyPart}  |  {beamPart}");
}
Console.WriteLine($"==== 共 {files.Count} 关，{swAll.Elapsed.TotalMinutes:F1}min ====");
if (mode is "both" or "greedy") Console.WriteLine($"阶梯：solved={greedySolved} 未知={greedyUnknown}");
if (mode is "both" or "beam") Console.WriteLine($"束搜索：solved={beamSolved} 未知={beamUnknown}");

// 基线：机动性排序 + 腾挪预算阶梯（8→16→32），每档平分预算（与 SlidePigFacade.SolveDense 同口径）
(SolveStatus, long, int) RunLadder(SlidePigLevel level, double totalSeconds)
{
    var sw = Stopwatch.StartNew();
    var tierSec = totalSeconds / 3;
    SolveResult best = new(SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
        TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "未运行");
    foreach (var budget in new[] { 8, 16, 32 })
    {
        var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(true, budget));
        best = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level, TimeSpan.FromSeconds(tierSec)));
        if (best.Status == SolveStatus.Solved) break;
    }
    return (best.Status, sw.ElapsedMilliseconds, best.Solution.Count);
}

// 门面阶梯：窄束 → 宽束 → 贪心（与 SlidePigFacade.SolveDense 同构，按剩余预算动态分配）
(SolveStatus, long, int, string) RunLadderFull(SlidePigLevel level, double totalSeconds)
{
    var sw = Stopwatch.StartNew();
    var totalMs = totalSeconds * 1000;
    long Remaining() => Math.Max(400, (long)(totalMs - sw.ElapsedMilliseconds));
    SolveResult best = new(SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
        TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "未运行");
    var expanded = 0L;

    var narrow = new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
        new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(Width: 64));
    var r = narrow.Solve(bundle, new SolveRequest<SlidePigLevel>(level, TimeSpan.FromMilliseconds(Remaining() * 60 / 100)));
    expanded += r.Metrics?.StatesExplored ?? 0;
    if (r.Status == SolveStatus.Solved) return (r.Status, sw.ElapsedMilliseconds, r.Solution.Count, $"展开={expanded}");
    best = r;

    if (Remaining() > 1500)
    {
        var wide = new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(Width: 256));
        r = wide.Solve(bundle, new SolveRequest<SlidePigLevel>(level, TimeSpan.FromMilliseconds(Remaining() * 70 / 100)));
        expanded += r.Metrics?.StatesExplored ?? 0;
        if (r.Status == SolveStatus.Solved) return (r.Status, sw.ElapsedMilliseconds, r.Solution.Count, $"展开={expanded}");
    }

    var tierMs = Math.Max(400, Remaining() / 3);
    foreach (var shuffle in new[] { 8, 16, 32 })
    {
        var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(true, shuffle));
        r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level, TimeSpan.FromMilliseconds(tierMs)));
        expanded += r.Metrics?.StatesExplored ?? 0;
        if (r.Status == SolveStatus.Solved) return (r.Status, sw.ElapsedMilliseconds, r.Solution.Count, $"展开={expanded}");
    }
    return (best.Status, sw.ElapsedMilliseconds, 0, $"展开={expanded}");
}

// 束搜索：宽度可调；restarts>0 时换种子多次重跑（每次平分预算），任一成功即返回
(SolveStatus, long, int, string) RunBeam(SlidePigLevel level, double totalSeconds)
{
    var sw = Stopwatch.StartNew();
    var runs = Math.Max(1, restarts);
    var perRun = totalSeconds / runs;
    SolveResult r = new(SolveStatus.ResourceLimit, Optimality.NotApplicable, false,
        TerminationReason.TimeLimit, MetricsScope.ExpandedPrefix, [], null, "未运行");
    long expandedTotal = 0;
    for (var i = 0; i < runs; i++)
    {
        var solver = new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new BeamSearchSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(
                Width: width, Seed: restarts > 0 ? i + 1 : null));
        r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level, TimeSpan.FromSeconds(perRun)));
        expandedTotal += r.Metrics?.StatesExplored ?? 0;
        if (r.Status == SolveStatus.Solved) break;
    }
    return (r.Status, sw.ElapsedMilliseconds, r.Solution.Count, $"展开={expandedTotal}");
}

string Short(SolveStatus status) => status switch
{
    SolveStatus.Solved => "solved",
    SolveStatus.ResourceLimit => "unknown",
    SolveStatus.UnsolvableProven => "proven-unsolvable",
    _ => status.ToString()
};

int LevelIdOf(string path)
{
    var name = Path.GetFileNameWithoutExtension(path);
    return int.TryParse(name.Replace("level-", ""), out var v) ? v : 0;
}
