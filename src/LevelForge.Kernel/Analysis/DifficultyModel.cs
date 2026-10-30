using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Analysis;

// 难度模型配置：每玩法一份固定权重与阈值（难度校准 §2 MVP 层——固定权重，运营期拟合后置）
// 模型版本必须随结论输出；权重拟合与分级校准在校准模块（数据驱动），不在内核
public sealed record DifficultyWeights(
    string GameId,                            // 归属玩法
    string ModelVersion,                      // 模型版本（结论必须带版本）
    double SolutionLengthWeight,              // 解长权重
    double BranchingWeight,                   // 平均分支权重
    IReadOnlyList<double> Thresholds);        // 三档边界（升序 2 个值）：score<t1→1 档，<t2→2 档，否则 3 档

// 难度评估结论：分数与档位都只是"规则估计"，不含玩家能力维度（通过率是唯一进入通道，后置）
public sealed record DifficultyAssessment(
    int Band,                                 // 1/2/3；0 = 不适用（无解/未知/取消）
    double Score,                             // 加权分；不适用时无意义
    string ModelVersion,                      // 输出必须携带的模型版本
    IReadOnlyList<string> UsedFeatures,       // 参与评分的特征清单
    IReadOnlyList<string> Notes);             // 边界说明（缺失特征/统计范围）

// 固定权重难度模型：MVP 形态（难度校准 §2：固定权重 + 阈值；可选 30 分钟手工分布对齐即改这份配置）
// 消费仅已实现的基础特征；ToolMoves 缺失时明确记录"未参与评分"，不填 0
public static class DifficultyModel
{
    // 三档中文名（展示层直接可用）
    private static readonly string[] BandNames = ["轻松", "标准", "烧脑"];

    // 评估：仅对 Status=Solved 的结果给档；score = 解长×w1 + 分支×w2
    public static DifficultyAssessment Evaluate(DifficultyFeatures features, SolveStatus status, DifficultyWeights weights)
    {
        var notes = new List<string>();
        // 无解/未知/取消/异常没有解长可言——不评分（未知不冒充高难，超时不当难度证据）
        if (status != SolveStatus.Solved)
        {
            return new DifficultyAssessment(0, 0, weights.ModelVersion, [],
                [$"不适用：求解结论为 {status}，难度估计仅在模型内有解时给出"]);
        }
        // 缺失特征如实记录，不参与评分
        if (features.ToolMoves is null)
        {
            notes.Add("道具特征缺失（未实现分类器），未参与评分");
        }
        var score = weights.SolutionLengthWeight * features.SolutionLength
                  + weights.BranchingWeight * features.AvgBranchingFactor;
        // 阈值分档：[t1, t2] 升序
        var band = score < weights.Thresholds[0] ? 1
                 : score < weights.Thresholds[1] ? 2
                 : 3;
        notes.Add("统计范围：已展开前缀；规则估计，未经玩家通过率校准");
        var used = new List<string> { "SolutionLength", "AvgBranchingFactor" };
        if (features.ToolMoves is not null) used.Add("ToolMoves");
        return new DifficultyAssessment(band, score, weights.ModelVersion, used, notes);
    }

    // 档位中文名
    public static string BandName(int band)
    {
        return band is >= 1 and <= 3 ? BandNames[band - 1] : "不适用";
    }
}
