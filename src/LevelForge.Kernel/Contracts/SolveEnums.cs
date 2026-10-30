namespace LevelForge.Kernel.Contracts;

// 求解结论五维度的枚举定义（求解内核 §5：status / optimality / graphComplete / terminationReason / metricsScope）
// graphComplete 为布尔不在本文件；此处的枚举是全工具的唯一取值来源

// 求解结论状态
public enum SolveStatus
{
    Solved,              // 找到目标；最优性另见 Optimality
    UnsolvableProven,    // 完整遍历有限可达图且前沿耗尽未发现目标——唯一合法的"无解"
    ResourceLimit,       // 时间/节点/内存预算耗尽，本次未得出结论；未知，不计作无解
    Cancelled,           // 调用方取消
    Invalid,             // 非法输入（关卡解析失败等）
    Error                // 适配层或内核异常
}

// 最优性声明
public enum Optimality
{
    UnitCostShortest,    // 动作等成本下的最少动作解（BFS 默认早停即可给出）
    HeuristicOptimal,    // 可采纳启发式下的最优解（IDA*，后置）
    NotProven,           // 有解但最优性未证明
    NotApplicable        // 无解/未知/取消等不适用最优性表述的场景
}

// 终止原因：具体化 ResourceLimit / Cancelled / Invalid / Error
public enum TerminationReason
{
    GoalFound,           // 首个目标即停（早停，图未完整）
    FrontierEmpty,       // 前沿耗尽（完整枚举的唯一正常完成态）
    TimeLimit,           // 时间预算耗尽
    NodeLimit,           // 节点预算耗尽
    MemoryLimit,         // 内存预算耗尽（按状态数近似判定，见 BfsSolver 说明）
    UserCancelled,       // 用户取消
    InvalidInput,        // 非法关卡输入
    AdapterError         // 适配层抛出异常
}

// 搜索元数据的统计范围
public enum MetricsScope
{
    ExpandedPrefix,      // 仅统计到首个目标/中断为止的已展开前缀
    FullReachableGraph   // 完整可达图
}

// 执行结局：解法磁带与 harness 报告共用取值
public enum ExecutionOutcome
{
    Win,                 // 通关
    Lose,                // 判负
    Incomplete,          // 序列执行完但未出胜负
    Error                // 执行过程出错
}
