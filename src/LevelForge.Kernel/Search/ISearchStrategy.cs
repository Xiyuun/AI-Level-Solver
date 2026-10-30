using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Search;

// 搜索策略统一接口：换算法 = 换策略实现，契约不动（求解内核 §2 双向解耦的算法侧）
// 实现方自行处理预算/取消/适配层异常，并按五维度如实产出结果
public interface ISearchStrategy<TLevel, TState, TMove>
{
    // 执行一次求解：返回值携带结论、最优性、完成范围、终止原因与统计
    SolveResult Solve(AdapterBundle<TLevel, TState, TMove> bundle, SolveRequest<TLevel> request);
}
