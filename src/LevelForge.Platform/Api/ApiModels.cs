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
