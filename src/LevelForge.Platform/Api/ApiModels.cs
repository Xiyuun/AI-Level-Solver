using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Platform.Api;

// ---- 玩法 ----

// 玩法列表项（顶栏下拉数据源）
// AdapterReady：适配层是否已注册（导入未接入的玩法为 false，前端据此禁用生成/求解并引导去向导）
public sealed record GameSummaryDto(
    string GameId, string DisplayName, string Status, string StatusName,
    string AdapterVer, string KernelVer, bool Gate2Applicable, long LevelCount,
    bool AdapterReady, string? EngineType);

// 玩法详情：接入产物全量（视觉映射/机制表/难度权重为解析后的 JSON 对象）
// ProjectProfile：导入项目的扫描画像（关卡目录候选/抽样文件/机器观察；静态注册玩法为 null）
public sealed record GameDetailDto(
    string GameId, string DisplayName, string Status, string StatusName,
    string AdapterVer, string AdapterHash, string KernelVer, string ContractVer,
    string LevelsRoot, bool Gate2Applicable, long LevelCount,
    JsonNode? VisualMapping, JsonNode? Mechanics, JsonNode? DifficultyWeights,
    string GameBrief, string FormatSpec, string RuleMatrix,
    string RegisteredAt, string UpdatedAt,
    bool AdapterReady, string? EngineType, string? ProjectRoot, JsonNode? ProjectProfile, JsonNode? ProjectAnalysis,
    string? PluginsRoot, string? CodeRoot);

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

// ---- 异步生成任务（SSE 进度）----

// 发起后台生成：返回任务号，进度走 SSE
public sealed record GenerationJobStartedDto(long JobId);

// 一条进度事件（前端进度层与流水线日志共用）
public sealed record GenerationEventDto(
    long Seq, long ElapsedMs, string Stage, string Message, int? AttemptIndex, string? FailureClass);

// 任务快照：运行中只给事件，终态附完整运行记录
public sealed record GenerationJobDto(
    long JobId, string Status, long ElapsedMs,
    IReadOnlyList<GenerationEventDto> Events,
    GenerationRunDto? Run, string? Error);

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

// ---- 三道门 ----

// 门1运行结论（report 为五项检查明细）
public sealed record Gate1RunResponseDto(long RecordId, bool Passed, JsonNode? Report);

// 门2运行请求
public sealed record Gate2RunRequestDto(string? LevelId);

// 门2运行结论（执行层失败时 firstDivergenceStep 为 null 且 message 说明原因）
public sealed record Gate2RunResponseDto(
    long RecordId, bool Passed, string Message, string LevelId, string LevelHash,
    int? FirstDivergenceStep, string? DivergenceDetail, bool? OutcomeMatch,
    string? TapeOutcome, string? HarnessOutcome, string? GameBuildFingerprint);

// 门3单关比对行
public sealed record Gate3RowDto(
    string LevelId, string LevelHash, string Verdict, string Status, string? BaselineStatus,
    int SolutionLength, int? BaselineSolutionLength, string TerminationReason,
    IReadOnlyList<string> Notes);

// 门3运行结论（match/red/yellow/new 计数与逐关明细）
public sealed record Gate3RunResponseDto(
    long RecordId, int Total, int Matched, int Red, int Yellow, int NewLevels,
    IReadOnlyList<Gate3RowDto> Rows);

// 验证记录项（三道门历史，指纹字段按门依赖可空）
public sealed record VerificationRecordDto(
    long Id, string GameId, string Gate, string Verdict,
    string? LevelHash, string? AdapterHash, string? KernelVer, string? ContractVer,
    string? RuleMatrixHash, string? GameBuildFingerprint, string? HarnessHash,
    int? SemanticSchemaVer, string? SamplesJson, string? SummaryJson, string ExecutedAt);

// ---- 接入向导 ----

// 向导步进更新请求（detail 为存档文本：资格报告/清单快照等）
public sealed record OnboardingStepRequestDto(string? Status, string? Detail = null);

// 玩法状态变更请求（分级接入：试用档先行、转正档补验）
public sealed record GameStatusRequestDto(string? Status, string? Note = null);

// 门2驱动配置登记请求（接入产物⑦）
public sealed record Gate2DriverRequestDto(
    string? Command,
    IReadOnlyList<string>? Args,
    int? TimeoutSeconds = null,
    bool? Applicable = null);

// 向导任务项
public sealed record OnboardingTaskDto(int Step, string Title, string Status, string? Detail, string UpdatedAt);

// 关卡导入请求：游戏项目关卡目录（绝对路径）
public sealed record ImportRequestDto(string Directory);

// ---- 项目导入（三轮 N10）----

// 导入请求：项目目录绝对路径（浏览器拿不到原生目录选择框，前端用路径输入 + 本地最近目录记忆）
public sealed record ProjectImportRequestDto(string? Directory);

// 导入结论：gameId 由目录名 slug 化；AlreadyImported=true 表示同目录重复导入（幂等）
public sealed record ProjectImportResultDto(
    string GameId, string DisplayName, string EngineType, string ProjectRoot, bool AlreadyImported);

// 重新扫描结论（向导里"项目内容变了"时触发；画像全文随玩法详情下发）
public sealed record ProjectRescanResultDto(string GameId, string EngineType, string ProjectRoot);

// 关卡根目录确认请求（向导①：用户从扫描候选中选定）
public sealed record LevelsRootRequestDto(string? LevelsRoot);

// 玩法代码根目录确认请求（向导①：用户确认"规则承载代码"位置）
public sealed record CodeRootRequestDto(string? CodeRoot);

// 深度分析结论 DTO（向导②：四问初稿/机制/清单初稿/风险/卫生度分级/工作量预估——全部待用户审核确认）
public sealed record ProjectAnalysisDto(
    string AnalyzedAt, string Model,
    IReadOnlyList<QualificationAnswerDto> FourQuestions,
    IReadOnlyList<string> Mechanics,
    IReadOnlyList<ChecklistDraftDto> ChecklistDraft,
    IReadOnlyList<string> Risks, string Summary,
    string HygieneGrade, string WorkEstimate);

public sealed record QualificationAnswerDto(string Question, string Answer, string Evidence);

public sealed record ChecklistDraftDto(string Group, string Text);

// 适配层检测结果 DTO（向导③"检测适配层"按钮：装载+绑定校验+自动门1）
public sealed record AdapterDetectResultDto(
    string GameId, string Loaded, string? DisplayName, string? AdapterVersion, string? AdapterHash,
    bool Gate1Passed, JsonNode? Gate1Report, string? Gate1Message);
