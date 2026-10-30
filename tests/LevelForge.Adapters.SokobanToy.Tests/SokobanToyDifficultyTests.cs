using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Search;
using Xunit;

namespace LevelForge.Adapters.SokobanToy.Tests;

// 玩具默认难度权重的排序合理性：简单关分数与档位低于复杂关（分布对齐的手工验证）
public class SokobanToyDifficultyTests
{
    private static readonly SokobanToyAdapter Adapter = new();
    private static readonly BfsSolver<SokobanLevel, SokobanState, SokobanMove> Solver = new();

    // 求解指定预制关并评估难度
    private static DifficultyAssessment Assess(int index)
    {
        var node = JsonNode.Parse(SokobanToyContent.LevelJsons[index])!.AsObject();
        var level = new SokobanLevel(node["id"]!.GetValue<string>(), node["grid"]!.AsArray().Select(g => g!.GetValue<string>()).ToArray());
        var bundle = new AdapterBundle<SokobanLevel, SokobanState, SokobanMove>("sokoban-toy", "0.1.0", Adapter, Adapter);
        var result = Solver.Solve(bundle, new SolveRequest<SokobanLevel>(level));
        var features = FeatureExtractor.Extract(bundle, result);
        return DifficultyModel.Evaluate(features, result.Status, SokobanToyContent.DefaultDifficultyWeights);
    }

    [Fact]
    public void 简单关与复杂关_分数与档位单调()
    {
        // toy-001（1 步推箱）应显著易于 toy-009（3 箱规划）
        var easy = Assess(0);
        var hard = Assess(8);
        Assert.Equal(1, easy.Band);
        Assert.Equal(3, hard.Band);
        Assert.True(easy.Score < hard.Score);
    }

    [Fact]
    public void 已知无解关_不给出难度档()
    {
        var unsolvable = Assess(9);
        Assert.Equal(0, unsolvable.Band);
    }
}
