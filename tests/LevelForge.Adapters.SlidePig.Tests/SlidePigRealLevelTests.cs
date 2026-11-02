using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using Xunit;

namespace LevelForge.Adapters.SlidePig.Tests;

// 真实关卡求解验证（PiggyFarm Luban Levels 原始数据）+ 门1合规
// 策略与门面同构：稀疏关 A*（最优）、稠密关贪心 DFS（任一解）——普查事实：110 关中除 1 号关（6 猪）
// 外全部 61~122 实体；稠密关短预算内只承诺"可解或未知"（诚实语义：未知不计作无解）
public class SlidePigRealLevelTests
{
    private static readonly SlidePigAdapter Adapter = new();
    private static readonly AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove> AStar = new();
    private static readonly GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove> Greedy = new();

    // 读取夹具关卡
    private static SlidePigLevel LoadFixture(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
        return SlidePigLevel.FromJson(File.ReadAllText(path));
    }

    // 适配层包
    private static AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> Bundle()
    {
        return new AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove>("slide-pig", "0.1.0", Adapter, Adapter);
    }

    // 求解并做独立回放验证（不采信求解器自述）
    private static SolveResult SolveAndVerify(SlidePigLevel level, bool dense, int seconds)
    {
        // 策略与门面同构：稠密关贪心 DFS（任一解），稀疏关 A*（最优解）
        ISearchStrategy<SlidePigLevel, SlidePigState, SlidePigMove> strategy =
            dense ? Greedy : AStar;
        var result = strategy.Solve(Bundle(), new SolveRequest<SlidePigLevel>(level)
        {
            NodeBudget = 20_000_000,
            TimeBudget = TimeSpan.FromSeconds(seconds)
        });
        if (result.Status == SolveStatus.Solved)
        {
            var s = Adapter.LoadLevel(level);
            foreach (var rec in result.Solution)
            {
                s = Adapter.ApplyMove(s, Adapter.DecodeMove(rec.Move));
            }
            Assert.True(Adapter.IsGoal(s), "解序列回放未通关——求解器与规则引擎不一致");
        }
        return result;
    }

    [Fact]
    public void 稀疏真实关_1号关A星最优解()
    {
        var level = LoadFixture("000001.json");                       // 6 猪
        var result = SolveAndVerify(level, dense: false, 30);

        Assert.Equal(SolveStatus.Solved, result.Status);
        Assert.Equal(Optimality.UnitCostShortest, result.Optimality); // 稀疏关 A*：最优性成立
        Assert.Equal(6, result.Solution.Count);                        // 6 猪全清
    }

    [Theory]
    [InlineData("000013.json")]      // Long（78 实体，纯 Long+Normal）
    [InlineData("000031.json")]      // Lock（92 实体）
    [InlineData("000061.json")]      // Cage（95 实体）
    [InlineData("000071.json")]      // Boom（106 实体）
    [InlineData("000091.json")]      // Spring（56 实体）
    public void 稠密真实关_贪心DFS短预算内可解或未知_绝不误报(string file)
    {
        // 普查实测：稠密关状态空间巨大；6 秒预算内诚实接受两种结论之一
        var level = LoadFixture(file);
        var result = SolveAndVerify(level, dense: true, 6);

        Assert.True(result.Status is SolveStatus.Solved or SolveStatus.ResourceLimit,
            $"意外结论：{result.Status}（{result.ErrorMessage}）");
        if (result.Status == SolveStatus.Solved)
        {
            // 贪心解的最优性必须如实标注 NotProven
            Assert.Equal(Optimality.NotProven, result.Optimality);
        }
        else
        {
            // 未知不计作无解（诚实语义铁律）
            Assert.NotEqual(SolveStatus.UnsolvableProven, result.Status);
        }
    }

    [Fact]
    public void 磁带录制_1号关结局与步数正确()
    {
        var level = LoadFixture("000001.json");
        var result = AStar.Solve(Bundle(), new SolveRequest<SlidePigLevel>(level)
        {
            TimeBudget = TimeSpan.FromSeconds(30)
        });
        Assert.Equal(SolveStatus.Solved, result.Status);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "000001.json"));
        var tape = new ReplayEngine().RecordTape(Bundle(), level, bytes, result, new TapeMetadata("000001", null, null));

        Assert.Equal(ExecutionOutcome.Win, tape.Outcome);
        Assert.Equal(result.Solution.Count, tape.Steps.Count);
        Assert.Equal(result.Solution.Select(s => s.MoveText), tape.Steps.Select(s => s.MoveText));
    }

    [Fact]
    public void 门1合规_五项全绿()
    {
        var level = LoadFixture("000001.json");
        var state = Adapter.LoadLevel(level);
        var input = new LevelForge.Kernel.Hosting.HostingCheckInput<SlidePigLevel, SlidePigState, SlidePigMove>(
            Bundle(),
            typeof(SlidePigAdapter).Assembly,
            new[]
            {
                // 维度判别样本：猪位置 / 爆炸计数 / 失败标志（朝向全程不变，不构成可变维度）
                new LevelForge.Kernel.Hosting.StatePairSample<SlidePigState>("猪位置",
                    state, Adapter.ApplyMove(state, Adapter.GetLegalMoves(state)[0])),
                new LevelForge.Kernel.Hosting.StatePairSample<SlidePigState>("失败标志", state, state with { Failed = true })
            },
            (state, Adapter.GetLegalMoves(state)[0]),
            level,
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "000001.json")));

        var report = new LevelForge.Kernel.Hosting.AdapterHosting().RunAll(input);
        Assert.True(report.AllPassed, string.Join("；",
            report.DependencyScan.Issues.Concat(report.StateKeyDiscrimination.Issues)
            .Concat(report.Purity.Issues).Concat(report.Determinism.Issues).Concat(report.KernelSelfTest.Issues)));
    }
}
