using System.Text.Json.Nodes;

namespace LevelForge.Kernel.Contracts;

// 一次求解中的单个操作记录：结构化 move 供执行，moveText 仅供展示（求解内核 §6）
public sealed record MoveRecord(
    JsonNode Move,           // 结构化动作（执行协议编码）
    string MoveText);        // 人类可读描述

// 搜索元数据：仅客观统计，不含难度评分；非默认全图统计（范围见 MetricsScope）
public sealed record SearchMetrics(
    long StatesExplored,          // 已出队并判定的状态数（目标在出队时判定，含目标本身）
    long UniqueStates,            // 去重后的已访问状态数（含前沿）
    long FrontierPeak,            // 前沿队列长度峰值
    long ElapsedMs,               // 求解耗时毫秒
    int SolutionLength,           // 解长度；无解/未知为 0
    double AvgBranchingFactor);   // 已展开状态的平均分支因子

// 求解请求：预算与取消由调用方声明；任一预算耗尽返回 ResourceLimit（未知），不冒充无解
public sealed record SolveRequest<TLevel>(
    TLevel Level,                              // 项目原生关卡数据
    TimeSpan? TimeBudget = null,               // 时间预算；null = 不限
    long? NodeBudget = null,                   // 展开节点数预算；null = 不限
    long? MemoryBudgetBytes = null,            // 内存预算（按状态数近似判定）；null = 不限
    bool FullEnumeration = false)              // 完整枚举模式：找到目标后继续至前沿耗尽
{
    public CancellationToken CancellationToken { get; init; }   // 取消令牌；不参与记录相等性
}

// 求解结果：五个独立维度分别回答"有没有解 / 最优性是否已证 / 图是否完整 / 为何停止 / 统计范围"（求解内核 §5）
// 消费方不得把其中任何一项升格为未声明的保证
public sealed record SolveResult(
    SolveStatus Status,                          // 求解结论
    Optimality Optimality,                       // 最优性
    bool GraphComplete,                          // 可达图是否已完整遍历
    TerminationReason TerminationReason,         // 终止原因
    MetricsScope MetricsScope,                   // 元数据统计范围
    IReadOnlyList<MoveRecord> Solution,          // 操作序列；仅 Status=Solved 时非空
    SearchMetrics? Metrics = null,               // 搜索元数据
    string? ErrorMessage = null);                // Invalid/Error 时的说明
