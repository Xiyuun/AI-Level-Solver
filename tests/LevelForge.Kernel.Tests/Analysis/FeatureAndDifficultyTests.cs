using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using LevelForge.Kernel.Tests.TestDoubles;
using Xunit;

namespace LevelForge.Kernel.Tests.Analysis;

// 第7步：特征提取与固定权重难度模型（求解内核 §7、难度校准 §1/§2）
public class FeatureAndDifficultyTests
{
    private static readonly BfsSolver<SyntheticLevel, SyntheticState, SyntheticMove> Solver = new();

    // 测试用固定权重：解长权重 1.0、分支 0、阈值 [4, 9]
    private static readonly DifficultyWeights Weights = new("test", "test-v1", 1.0, 0.0, [4.0, 9.0]);

    #region 特征提取

    [Fact]
    public void 特征提取_基础字段来自求解结果()
    {
        var level = new SyntheticLevel(0, 3, new[] { (0, 1), (0, 2), (1, 3), (2, 3) });
        var bundle = SyntheticGraphAdapter.CreateBundle();
        var result = Solver.Solve(bundle, new SolveRequest<SyntheticLevel>(level));

        var features = FeatureExtractor.Extract(bundle, result);

        Assert.Equal(2, features.SolutionLength);
        Assert.Equal(Optimality.UnitCostShortest, features.Optimality);
        Assert.Equal(4, features.UniqueStates);
        // 合成适配器未实现分类器 → ToolMoves 为 null（缺失，不是 0）
        Assert.Null(features.ToolMoves);
    }

    [Fact]
    public void 特征提取_实现分类器时统计道具操作()
    {
        // PurityAdapter 把偶数目标格归为道具：链 0→5 的解经过 1,2,3,4,5，偶数 {2,4} → 2 次
        var adapter = new PurityAdapter(PurityDefect.None);
        var bundle = new AdapterBundle<PurityLevel, PurityState, PurityMove>("p", "0", adapter, adapter);
        var result = new BfsSolver<PurityLevel, PurityState, PurityMove>().Solve(bundle, new SolveRequest<PurityLevel>(new PurityLevel(8, 5)));

        Assert.Equal(SolveStatus.Solved, result.Status);
        var features = FeatureExtractor.Extract(bundle, result);
        Assert.Equal(2, features.ToolMoves);
    }

    #endregion

    #region 难度模型

    [Fact]
    public void 难度模型_有解给档且带版本与特征清单()
    {
        var features = new DifficultyFeatures(3, Optimality.UnitCostShortest, 1.0, 10, 10, 2, 5, null);
        var assessment = DifficultyModel.Evaluate(features, SolveStatus.Solved, Weights);

        Assert.Equal(1, assessment.Band);                          // score = 3.0 < 4
        Assert.Equal(3.0, assessment.Score);
        Assert.Equal("test-v1", assessment.ModelVersion);
        Assert.Contains("SolutionLength", assessment.UsedFeatures);
    }

    [Fact]
    public void 难度模型_阈值边界分档正确()
    {
        // score 恰等于 t1=4 → 2 档；等于 t2=9 → 3 档
        var a = DifficultyModel.Evaluate(new DifficultyFeatures(4, Optimality.UnitCostShortest, 0, 0, 0, 0, 0, null), SolveStatus.Solved, Weights);
        var b = DifficultyModel.Evaluate(new DifficultyFeatures(9, Optimality.UnitCostShortest, 0, 0, 0, 0, 0, null), SolveStatus.Solved, Weights);
        Assert.Equal(2, a.Band);
        Assert.Equal(3, b.Band);
        Assert.Equal("标准", DifficultyModel.BandName(a.Band));
        Assert.Equal("烧脑", DifficultyModel.BandName(b.Band));
    }

    [Fact]
    public void 难度模型_无解未知取消不评分()
    {
        // 未知不计作无解，也不冒充高难度——超时不是难度证据
        var features = new DifficultyFeatures(0, Optimality.NotApplicable, 0, 999, 999, 0, 30000, null);
        var assessment = DifficultyModel.Evaluate(features, SolveStatus.ResourceLimit, Weights);
        Assert.Equal(0, assessment.Band);
        Assert.Contains(assessment.Notes, n => n.Contains("不适用"));
    }

    [Fact]
    public void 难度模型_缺失特征如实记录不填零()
    {
        var features = new DifficultyFeatures(2, Optimality.UnitCostShortest, 1.0, 5, 5, 1, 3, null);
        var assessment = DifficultyModel.Evaluate(features, SolveStatus.Solved, Weights);
        // ToolMoves=null 不影响分数（本模型未消费该特征），但必须在备注中说明缺失
        Assert.Equal(2.0, assessment.Score);
        Assert.Contains(assessment.Notes, n => n.Contains("未参与评分"));
    }

    #endregion
}
