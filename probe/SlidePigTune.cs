#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 贪心 DFS 改进实验：机动性平级排序 × 腾挪预算迭代加深（临时诊断工具）
// 运行：dotnet run probe/SlidePigTune.cs -- <关卡json路径> [总秒]

using LevelForge.Adapters.SlidePig;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using System.Diagnostics;

var path = args.Length > 0 ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels\000061.json";
var totalSeconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 120;

var level = SlidePigLevel.FromJson(File.ReadAllText(path));
var adapter = new SlidePigAdapter();
var bundle = new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slidepig", "probe", adapter, adapter);
Console.WriteLine($"关卡 {level.LevelId}：实体 {adapter.LoadLevel(level).Pigs.Count} 只，总预算 {totalSeconds:F0}s");

// 单次实验：给定选项跑 solve，返回（结论, 耗时ms, 解长, minH）
(SolveStatus, long, int, int) Run(GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options opt, double sec)
{
    var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(opt);
    var sw = Stopwatch.StartNew();
    var r = solver.Solve(bundle, new SolveRequest<SlidePigLevel>(level)
    {
        NodeBudget = 200_000_000,
        TimeBudget = TimeSpan.FromSeconds(sec)
    });
    return (r.Status, sw.ElapsedMilliseconds, r.Solution.Count, solver.MinHeuristicSeen);
}

var swAll = Stopwatch.StartNew();

// E1：机动性平级（无预算），30s
var (st1, ms1, len1, mh1) = Run(new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(MobilityTiebreak: true), 30);
Console.WriteLine($"E1 机动性排序      : {st1,-14} {ms1,7}ms 解长={len1,3} minH={mh1}");

// E2：机动性 + 腾挪预算迭代加深；每档上限=min(第3参默认90s, 剩余)
var tierCap = args.Length > 2 && double.TryParse(args[2], out var tc) ? tc : 90;
foreach (var budget in new[] { 8, 16, 32, 64, 128, 256, 512, 1024 })
{
    var remain = totalSeconds - swAll.Elapsed.TotalSeconds;
    if (remain <= 3) { Console.WriteLine($"E2 预算={budget,-5}: 总预算用尽，停止加深"); break; }
    var cap = Math.Min(remain, tierCap);                           // 每档时限（默认 90s）
    var (st, ms, len, mh) = Run(new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(
        MobilityTiebreak: true, ShuffleBudget: budget), cap);
    Console.WriteLine($"E2 预算={budget,-5}: {st,-14} {ms,7}ms 解长={len,3} minH={mh}");
    if (st == SolveStatus.Solved)
    {
        Console.WriteLine($"==== 攻破！腾挪预算 {budget}，总耗时 {swAll.Elapsed.TotalSeconds:F1}s ====");
        return;
    }
    // 该档超时（未穷尽）：更大预算是超集，只会更慢——停止加深；自然穷尽则继续加深
    if (ms >= cap * 1000 * 0.9) { Console.WriteLine("（该档超时，停止加深）"); break; }
}
Console.WriteLine($"==== 未攻破（总耗时 {swAll.Elapsed.TotalSeconds:F1}s）====");
