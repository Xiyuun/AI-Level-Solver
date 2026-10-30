namespace LevelForge.Kernel.Contracts;

// 可选能力接口：适配层额外声明能力，内核据此升级策略（求解内核 §4）
// MVP 仅定义不消费——内核不支持的能力请求必须明确拒绝，不得静默降级

// 启发式能力：声明后（后置的）IDA* 才可能启用；估计必须可采纳（不高估），否则最优性保证失效
public interface IHeuristicAdapter<TState>
{
    // 从当前状态到目标的代价下界估计
    int EstimateToGoal(TState state);
}

// 随机复现能力草案：仅使一次随机运行可重现，不等于已具备概率搜索所需的转移/效用/结果契约；MVP 不启用
public interface ISeededRandomAdapter<TState, TMove>
{
    // 带种子的随机结算：同一 (state, move, seed) 恒得同一结果
    TState ApplyMoveSeeded(TState state, TMove move, ulong seed);
}

// 操作分类能力：声明后难度特征可统计道具使用
public interface IMoveCategorizer<TMove>
{
    // 返回操作类别
    MoveCategory Category(TMove move);
}

// 操作类别 v1：仅两类，玩法可按需扩展
public enum MoveCategory
{
    Normal,              // 普通操作
    Tool                 // 道具操作
}
