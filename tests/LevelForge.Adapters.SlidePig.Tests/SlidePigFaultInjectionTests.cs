using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Search;
using Xunit;

namespace LevelForge.Adapters.SlidePig.Tests;

// 故障注入演示测试（第17步证据链的测试侧）：三类典型适配层缺陷 → 对应防线必须红
// 答辩对照表的机器可验证形态：每个故障一行"注入什么→谁抓→证据"
public class SlidePigFaultInjectionTests
{
    private static SlidePigLevel LoadFixture(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
        return SlidePigLevel.FromJson(File.ReadAllText(path));
    }

    // 基准：正常适配器在 1 号关与 71 号关的行为（故障对照的参照系）
    [Fact]
    public void 基准_正常适配器解1号关71号关()
    {
        var bundle = SlidePigFaultAdapter.CreateBundle(SlidePigFault.None);
        var l1 = LoadFixture("000001.json");
        var r1 = new AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove>()
            .Solve(bundle, new SolveRequest<SlidePigLevel>(l1) { TimeBudget = TimeSpan.FromSeconds(30) });
        Assert.Equal(SolveStatus.Solved, r1.Status);   // 6 步最优解
        Assert.Equal(6, r1.Solution.Count);

        var l71 = LoadFixture("000071.json");
        var r71 = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(MobilityTiebreak: true, ShuffleBudget: 32))
            .Solve(bundle, new SolveRequest<SlidePigLevel>(l71)
            {
                NodeBudget = 100_000_000,
                TimeBudget = TimeSpan.FromSeconds(120)
            });
        Assert.Equal(SolveStatus.Solved, r71.Status);  // 71 号关（106 实体含爆炸猪）贪心可解
    }

    // 故障① StateKey 漏计数维度 → 门1 键判别测试必须红并点名维度
    [Fact]
    public void 故障1_键漏计数维度_门1键判别抓()
    {
        var broken = SlidePigFaultAdapter.CreateBundle(SlidePigFault.StateKeyIgnoresCounters);
        var level = LoadFixture("000001.json");
        var state = broken.Game.LoadLevel(level);
        // 与门面 RunGate1Checks 同口径的维度样本：爆炸计数对（规则矩阵声明的全部可变维度）
        var keySamples = new[]
        {
            new StatePairSample<SlidePigState>("猪位置",
                state, broken.Game.ApplyMove(state, broken.Game.GetLegalMoves(state)[0])),
            new StatePairSample<SlidePigState>("爆炸计数", state,
                state with { Pigs = state.Pigs.Select(p => p.Id == 1 ? p with { Counter = p.Counter + 1 } : p).ToList() }),
            new StatePairSample<SlidePigState>("失败标志", state, state with { Failed = true })
        };
        var report = ComplianceChecks.CheckStateKeyDiscrimination(broken.Game.StateKey, keySamples);

        Assert.False(report.Passed);                                   // 防线必须红
        Assert.Contains(report.Issues, i => i.Contains("爆炸计数"));    // 且点名漏掉的维度
    }

    // 故障② 漏枚举操作（奇数猪冻结）→ 1 号关从可解变穷尽可证无解 → 门3 基准 diff 必红
    [Fact]
    public void 故障2_漏枚举奇数猪_1号关翻转为无解_门3diff必红()
    {
        var good = SlidePigFaultAdapter.CreateBundle(SlidePigFault.None);
        var broken = SlidePigFaultAdapter.CreateBundle(SlidePigFault.OddPigsFrozen);
        var level = LoadFixture("000001.json");
        var request = new SolveRequest<SlidePigLevel>(level) { TimeBudget = TimeSpan.FromSeconds(30) };
        var solver = new AStarSolver<SlidePigLevel, SlidePigState, SlidePigMove>();

        // 基准（正常适配器）：solved / 6 步——这是门3已确认基准的实际内容
        var baseline = solver.Solve(good, request);
        Assert.Equal(SolveStatus.Solved, baseline.Status);

        // 注入后：同一关、同版本、同配置 → 结论变化 solved→unsolvable_proven
        // 门3 的 diff 判定语义（验证门 §5.2）：相同输入版本配置下结论变化 = 红灯
        var after = solver.Solve(broken, request);
        Assert.Equal(SolveStatus.UnsolvableProven, after.Status);
        Assert.True(after.GraphComplete);                              // 穷尽证明（不是预算耗尽的未知）
    }

    // 故障③ 爆炸规则反转（碰撞不扣计数）→ 用故障适配器制带，正确模型回放其动作序列必现计数分歧
    // ——这正是门2 逐步语义比对将发现的"首分歧步"（真机版本见 evidence/step17）
    [Fact]
    public void 故障3_爆炸不扣减_磁带语义与正确模型分歧于首次爆炸碰撞()
    {
        var good = SlidePigFaultAdapter.CreateBundle(SlidePigFault.None);
        var broken = SlidePigFaultAdapter.CreateBundle(SlidePigFault.BoomNeverDecrements);
        var level = LoadFixture("000071.json");                       // 106 实体，含爆炸猪
        var solver = new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>(
            new GreedyDfsSolver<SlidePigLevel, SlidePigState, SlidePigMove>.Options(MobilityTiebreak: true, ShuffleBudget: 32));

        // 用故障适配器求解并"制带"（磁带语义 = 故障规则的产物）
        var brokenResult = solver.Solve(broken, new SolveRequest<SlidePigLevel>(level)
        {
            NodeBudget = 100_000_000,
            TimeBudget = TimeSpan.FromSeconds(120)
        });
        Assert.Equal(SolveStatus.Solved, brokenResult.Status);

        // 用正确适配器回放同一条动作序列：逐比较爆炸计数——首次碰撞处两模型必然分歧
        var goodExec = (IExecutionProtocol<SlidePigState, SlidePigMove>)good.Game;
        var brokenExec = (IExecutionProtocol<SlidePigState, SlidePigMove>)broken.Game;
        var correctState = good.Game.LoadLevel(level);
        var firstDivergence = -1;
        var divergenceDetail = "";
        for (var i = 0; i < brokenResult.Solution.Count; i++)
        {
            var move = goodExec.DecodeMove(brokenResult.Solution[i].Move);
            correctState = good.Game.ApplyMove(correctState, move);
            var expectedState = ReplayTo(broken, level,
                brokenResult.Solution.Take(i + 1).Select(s => goodExec.DecodeMove(s.Move)).ToList());
            // 比较两模型的爆炸计数序列
            var correctBoom = ExtractBoomCounters(goodExec.ProjectSemanticState(correctState));
            var expectedBoom = ExtractBoomCounters(brokenExec.ProjectSemanticState(expectedState));
            if (correctBoom != expectedBoom)
            {
                firstDivergence = i + 1;
                divergenceDetail = $"第 {i + 1} 步爆炸计数：正确模型 {correctBoom} vs 故障磁带 {expectedBoom}";
                break;
            }
        }
        Assert.True(firstDivergence > 0, "爆炸计数应在首次碰撞步分歧（若未分歧说明 71 号关解序列没触发碰撞，需换关）");
        Assert.Contains("爆炸计数", divergenceDetail);
    }

    #region 内部工具

    // 在指定适配层上顺序执行动作序列，返回终态
    private static SlidePigState ReplayTo(
        AdapterBundle<SlidePigLevel, SlidePigState, SlidePigMove> bundle,
        SlidePigLevel level, IReadOnlyList<SlidePigMove> moves)
    {
        var s = bundle.Game.LoadLevel(level);
        foreach (var m in moves)
        {
            s = bundle.Game.ApplyMove(s, m);
        }
        return s;
    }

    // 从语义投影中提取全部爆炸猪的计数串（id:counter 升序）
    private static string ExtractBoomCounters(System.Text.Json.Nodes.JsonNode semantic)
    {
        var parts = new List<string>();
        foreach (var pig in semantic["pigs"]!.AsArray())
        {
            if (pig!["type"]!.GetValue<string>() == "Boom")
            {
                parts.Add(pig["id"]!.GetValue<int>() + ":" + pig["counter"]!.GetValue<int>());
            }
        }
        return string.Join(",", parts.Order());
    }

    #endregion
}
