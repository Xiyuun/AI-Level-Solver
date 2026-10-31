using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Generation;

// 失败分类（generation_candidates.failure_class 取值；生成端出题分析 §3.3）
// API 失败与内容失败分开计数：api_error 是基础设施问题，其余是内容问题
public static class FailureClass
{
    public const string None = "none";                           // 通过（被采纳）
    public const string ApiError = "api_error";                  // LLM 调用失败（网络/HTTP/超时）
    public const string FormatInvalid = "format_invalid";        // 格式校验未通过（含修复重试后）
    public const string Unsolvable = "unsolvable";               // 模型内已证无解（穷尽证明）
    public const string ResourceLimit = "resource_limit";        // 预算耗尽未知（不计作无解）
    public const string DifficultyMiss = "difficulty_miss";      // 有解但难度档不命中目标
    public const string ConstraintViolation = "constraint_violation";   // 违反特殊要求（需检查器，玩具未实现）
    public const string SolveError = "solve_error";              // 求解过程异常（Error 结论，如实单列）
}

// 生成请求（服务层）：目标难度 1/2/3 + 编号/特殊要求 + 可覆盖的预算参数
public sealed record GenerationRequest(
    string GameId,
    int TargetDifficulty,
    string? LevelNumber = null,
    string? SpecialRequirements = null,
    int? CandidateLimit = null,
    long? SolveTimeoutMs = null);

// 单次尝试的完整记录（响应与落库共用的形状）
public sealed record CandidateAttempt(
    int AttemptIndex,                    // 第几次尝试（1 起）
    string? ContentJson,                 // 最后一次模型输出（提取后的 JSON；api_error 时为 null）
    string FailureClass,                 // FailureClass 常量
    string? FailureDetail,               // 失败原因/通过说明（回填给下一轮的自然语言来源）
    int? DifficultyBand,                 // 评到档位才有；null=未评
    double? DifficultyScore,
    long? SolveRunId,                    // 判卷求解的运行记录引用
    long? TapeId,                        // 有解候选的磁带（预览用）
    int LlmCalls,                        // 本次尝试消耗的 LLM 调用数（含修复重试）
    long ElapsedMs);

// 整轮生成结论
public sealed record GenerationRunSummary(
    long RunId, string GameId, string Status,          // succeeded|failed|cancelled
    int TargetDifficulty, string? LevelNumber, string? SpecialRequirements,
    int CandidateLimit, long SolveTimeoutMs, string PromptVersion,
    IReadOnlyList<CandidateAttempt> Candidates,
    int? AdoptedAttempt,                 // 被采纳尝试序号；null=无
    string? FailureSummary,              // 候选耗尽/API失败等总结
    long TotalElapsedMs);

// 生成请求校验失败（端点转 400；不是运行失败——运行失败落在 GenerationRunSummary 里）
public sealed class GenerationException(string message) : Exception(message);

// 失败反馈构造：只给已验证事实，不编修复因果（生成端出题分析 §3.3"无解反馈首版只给已验证事实"）
public static class GenerationFeedback
{
    // 模型内无解：穷尽证明的客观事实 + 统计数字，不推导"某实体封堵"
    public static string Unsolvable(PlatformSolve outcome)
    {
        var unique = outcome.Result.Metrics?.UniqueStates ?? 0;
        return $"该候选在指定规则与资源条件下完成可达图遍历，未发现目标状态；已访问状态数 {unique}（详见运行记录）";
    }

    // 预算耗尽：明确"未知"，绝不表述为无解
    public static string ResourceLimit(long timeoutMs)
    {
        return $"达到求解时间上限（{timeoutMs}ms），本次未得出结论；未知不计作无解";
    }

    // 难度不达标：给分数与偏差方向（步数/实体层面的建议，标注为建议而非诊断）
    public static string DifficultyMiss(int band, double score, int target)
    {
        var direction = target > band
            ? "请提高挑战：增加实体、利用阻挡关系或延长必需操作序列"
            : "请降低难度：减少实体或缩短必需操作序列";
        return $"难度评估 {score:F1}（第 {band} 档 {DifficultyModel.BandName(band)}），未命中目标第 {target} 档；{direction}";
    }

    // 格式不合法：把校验器的错误清单原样回喂
    public static string FormatInvalid(IReadOnlyList<string> errors, int repairs)
    {
        return $"输出不是合法关卡 JSON（含 {repairs} 次修复重试）：{string.Join("；", errors)}；请严格按格式说明书输出";
    }

    // 通过说明（候选记录的 detail 也落"为什么通过"，报告可追溯）
    public static string Passed(PlatformSolve outcome)
    {
        var r = outcome.Result;
        return $"求解验证通过：解长 {r.Solution.Count}，最优性 {r.Optimality}；难度评估 {outcome.Difficulty.Score:F1}（第 {outcome.Difficulty.Band} 档），命中目标";
    }
}
