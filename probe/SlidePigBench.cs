#:project ../src/Adapters/LevelForge.Adapters.SlidePig/LevelForge.Adapters.SlidePig.csproj
#:project ../src/LevelForge.Kernel/LevelForge.Kernel.csproj

// 滑动猪性能微基准：定位 ApplyMove / StateKey / GetLegalMoves 的单次耗时（临时诊断工具）
// 运行：dotnet run probe/SlidePigBench.cs -- <关卡json路径>

using LevelForge.Adapters.SlidePig;
using System.Diagnostics;

var path = args.Length > 0 ? args[0]
    : @"E:\Projects\UnityProjects\PiggyFarm\piggyfarm\Assets\Main\FrameWorkExtras\LevelData\Luban~\Datas\Levels\000061.json";

var level = SlidePigLevel.FromJson(File.ReadAllText(path));
var adapter = new SlidePigAdapter();
var state = adapter.LoadLevel(level);
Console.WriteLine($"关卡 {level.LevelId}：{level.MapWidth}x{level.MapHeight}，猪 {state.Pigs.Count} 只");

// 预热
var moves = adapter.GetLegalMoves(state);
foreach (var m in moves) _ = adapter.ApplyMove(state, m);
_ = adapter.StateKey(state);

const int N = 50_000;
var sw = Stopwatch.StartNew();
for (var i = 0; i < N; i++) _ = adapter.GetLegalMoves(state);
sw.Stop();
Console.WriteLine($"GetLegalMoves: {sw.Elapsed.TotalMicroseconds / N:F1} µs/次（合法 {adapter.GetLegalMoves(state).Count} 个）");

var move = moves[0];
sw.Restart();
for (var i = 0; i < N; i++) _ = adapter.ApplyMove(state, move);
sw.Stop();
Console.WriteLine($"ApplyMove:     {sw.Elapsed.TotalMicroseconds / N:F1} µs/次");

var next = adapter.ApplyMove(state, move);
sw.Restart();
for (var i = 0; i < N; i++) _ = adapter.StateKey(next);
sw.Stop();
Console.WriteLine($"StateKey:      {sw.Elapsed.TotalMicroseconds / N:F1} µs/次（键长 {adapter.StateKey(next).Length}）");
