using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Platform.Api;

// ---- 玩法 ----

// 玩法列表项（顶栏下拉数据源）
public sealed record GameSummaryDto(
    string GameId, string DisplayName, string Status, string StatusName,
    string AdapterVer, string KernelVer, bool Gate2Applicable, long LevelCount);

// 玩法详情：接入产物全量（视觉映射/机制表/难度权重为解析后的 JSON 对象）
public sealed record GameDetailDto(
    string GameId, string DisplayName, string Status, string StatusName,
    string AdapterVer, string AdapterHash, string KernelVer, string ContractVer,
    string LevelsRoot, bool Gate2Applicable, long LevelCount,
    JsonNode? VisualMapping, JsonNode? Mechanics, JsonNode? DifficultyWeights,
    string GameBrief, string FormatSpec, string RuleMatrix,
    string RegisteredAt, string UpdatedAt);

// 当前活跃玩法
public sealed record ActiveGameDto(string GameId);

// 关卡列表项
public sealed record LevelDto(string LevelId, string LevelHash, string Source, string CreatedAt);

// ---- 求解 ----

// 求解请求：存量关（levelId）或质检粘贴（levelJson）二选一；预算可选
public sealed record SolveRequestDto(
    string? GameId,
    string? LevelId,
    string? LevelJson,                          // 质检模式：项目原生格式直通（零转换）
    long? TimeBudgetMs = null,
    long? NodeBudget = null,
    long? MemoryBudgetBytes = null,
    bool FullEnumeration = false);

// 求解响应：五维结论 + metrics + 难度评估 + 磁带索引（枚举小写字符串输出）
public sealed record SolveResponseDto(
    long RunId, string GameId, string LevelRef, string LevelHash,
    SolveStatus Status, Optimality Optimality, bool GraphComplete,
    TerminationReason TerminationReason, MetricsScope MetricsScope,
    int SolutionLength, MetricsDto? Metrics, DifficultyDto Difficulty,
    long? TapeId, string? ErrorMessage);

// 搜索元数据镜像（Metrics 序列化形态）
public sealed record MetricsDto(
    long StatesExplored, long UniqueStates, long FrontierPeak,
    long ElapsedMs, int SolutionLength, double AvgBranchingFactor);

// 难度评估（规则估计，带模型版本与边界说明）
public sealed record DifficultyDto(
    int Band, string BandName, double Score, string ModelVersion,
    IReadOnlyList<string> UsedFeatures, IReadOnlyList<string> Notes);

// ---- 设置与短语 ----

// LLM 测试连接请求：字段可空——空则用已存设置测试
public sealed record LlmTestRequestDto(string? BaseUrl, string? ApiKey, string? Model);

// LLM 测试连接结论
public sealed record LlmTestResponseDto(bool Ok, long LatencyMs, string? Error, string? ReplyPreview);

// 短语创建请求
public sealed record PhraseCreateDto(string Text);

// 短语项
public sealed record PhraseDto(long Id, string Text, int SortOrder);

// ---- 生成 ----

// 生成请求：目标难度 1/2/3；预算参数可空（缺省用设置页默认值）
public sealed record GenerateRequestDto(
    string? GameId,
    int TargetDifficulty,
    string? LevelNumber = null,
    string? SpecialRequirements = null,
    int? CandidateLimit = null,
    long? SolveTimeoutMs = null);

// 生成候选项（响应形态；contentJson 为提取后的候选原文）
public sealed record CandidateAttemptDto(
    int AttemptIndex, string? ContentJson, string FailureClass, string? FailureDetail,
    int? DifficultyBand, double? DifficultyScore, long? SolveRunId, long? TapeId,
    int LlmCalls, long ElapsedMs);

// 生成运行详情（POST /api/generate 响应与 GET /api/generation/runs/{id} 共用）
public sealed record GenerationRunDto(
    long RunId, string GameId, string Status, int TargetDifficulty,
    string? LevelNumber, string? SpecialRequirements,
    int CandidateLimit, long SolveTimeoutMs, string PromptVersion,
    IReadOnlyList<CandidateAttemptDto> Candidates,
    int? AdoptedAttempt, string? FailureSummary, long TotalElapsedMs);

// ---- 采纳与执行确认 ----

// 采纳落盘请求：source = generated（生成候选）| imported（质检/手摆导入）
public sealed record AdoptRequestDto(
    string? GameId,
    string? LevelJson,
    string? Source = null,
    bool Overwrite = false,
    string? ExpectedOldHash = null);

// 采纳结论：落盘与登记的完整引用
public sealed record AdoptResponseDto(
    string LevelId, string LevelHash, string FilePath, bool SameContent, bool Replaced,
    long ConfirmationId, string ConfirmationState, long? SolveRunId);

// 执行确认项（fileStatus：ok 一致 / missing 文件缺失 / hashChanged 内容已变）
public sealed record ConfirmationDto(
    long Id, string GameId, string LevelId, string LevelHash, string State, string Mode,
    string? Outcome, string? ConfirmedBy, string? ConfirmedAt, string? Note, long? SolveRunId,
    string FileStatus, string? CurrentFileHash, string CreatedAt, string UpdatedAt);

// 人工降级结论请求（验证门 §4.4：注明"仅最终结果"）
public sealed record ManualResultRequestDto(
    string? Outcome, string? ConfirmedBy, string? Note = null, string? GameBuildFingerprint = null);

// 执行报告导入请求（harness 报告的最小集：指纹 + 结局）
public sealed record ExecutionReportDto(string? LevelHash, string? Outcome, string? GameBuildFingerprint = null);

// 确认入基准请求
public sealed record ApproveRequestDto(string? ApprovedBy);

// 入基准结论（AlreadyExists=true 表示重复确认幂等返回）
public sealed record ApproveResponseDto(long BaselineId, bool AlreadyExists);

// 基准项（验证门 §5.1 LevelBaseline）
public sealed record BaselineDto(
    long Id, string GameId, string LevelId, string LevelHash,
    string AdapterVer, string AdapterHash, string KernelVer,
    string Status, string Optimality, bool GraphComplete, string TerminationReason,
    int SolutionLength, long StatesExplored, string TrajectoryHash,
    string? ExecutionConfirmationRef, string? ApprovedBy, string? ApprovedAt, string CreatedAt);
