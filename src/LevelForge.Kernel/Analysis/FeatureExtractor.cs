using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Analysis;

// 难度基础特征：只承载已实现的客观统计（求解内核 §7、难度校准 §1）
// 边界：UniqueStates 未完成全图时不得称"完整状态空间规模"；
// ToolMoves 为 null 表示玩法未实现 IMoveCategorizer——缺失特征不填 0 参与评分
public sealed record DifficultyFeatures(
    int SolutionLength,              // 解长；仅 Status=Solved 有意义
    Optimality Optimality,           // 解的最优性标注
    double AvgBranchingFactor,       // 已展开前缀的平均分支（非默认全图统计）
    long UniqueStates,               // 去重已访问状态数
    long StatesExplored,             // 已出队判定状态数
    long FrontierPeak,               // 前沿峰值
    long ElapsedMs,                  // 求解耗时
    int? ToolMoves);                 // 解中道具操作数；null=未实现分类器

// 特征提取器：随求解产出，MVP 难度模型仅消费这些字段（死局率属 GraphAnalyzer，后置）
// 事实与解释分离：本层只出客观特征，权重拟合与分级在难度模型/校准层
public static class FeatureExtractor
{
    // 从求解结果提取特征；适配层实现 IMoveCategorizer 时统计道具操作，否则 ToolMoves=null
    public static DifficultyFeatures Extract<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        SolveResult result)
    {
        int? toolMoves = null;
        // 道具统计仅在适配层声明了分类能力时才有值（"已实现的道具统计"）
        if (bundle.Game is IMoveCategorizer<TMove> categorizer)
        {
            var count = 0;
            foreach (var record in result.Solution)
            {
                var move = bundle.Execution.DecodeMove(record.Move);
                if (categorizer.Category(move) == MoveCategory.Tool) count++;
            }
            toolMoves = count;
        }
        var m = result.Metrics;
        return new DifficultyFeatures(
            result.Solution.Count,
            result.Optimality,
            m?.AvgBranchingFactor ?? 0,
            m?.UniqueStates ?? 0,
            m?.StatesExplored ?? 0,
            m?.FrontierPeak ?? 0,
            m?.ElapsedMs ?? 0,
            toolMoves);
    }
}
