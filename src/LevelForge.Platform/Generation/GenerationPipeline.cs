using System.Diagnostics;
using System.Text;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Llm;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Generation;

// 生成流水线：LLM 出题 → 格式校验（修复重试≤2）→ 求解判卷 → 难度评估 → 失败分类回填重生
// 形态铁律（生成端出题分析 §8）：无 agent——每次 LLM 调用都是无状态单问单答，循环由本代码的控制流驱动
// 候选耗尽 = 该关生成失败，不据此断言需求不可满足；无解反馈只给已验证事实
public sealed class GenerationPipeline
{
    private const int MaxRepairs = 2;          // 格式修复重试上限（§3.2：校验失败带错误自动修复，上限 2 次）
    private const int MaxCandidateLimit = 10;  // 候选上限护栏（前端可调，但防误填爆量）

    private readonly GameRegistry _registry;
    private readonly SettingsStore _settings;
    private readonly SolveRunStore _runs;
    private readonly TapeStore _tapes;
    private readonly GenerationStore _store;
    private readonly FewShotSelector _fewShot;
    private readonly LlmClient _llm;

    public GenerationPipeline(
        GameRegistry registry, SettingsStore settings, SolveRunStore runs,
        TapeStore tapes, GenerationStore store, FewShotSelector fewShot, LlmClient llm)
    {
        _registry = registry;
        _settings = settings;
        _runs = runs;
        _tapes = tapes;
        _store = store;
        _fewShot = fewShot;
        _llm = llm;
    }

    // 跑完整轮生成：返回运行结论（含逐候选记录）；请求本身非法抛 GenerationException（不产生运行行）
    public async Task<GenerationRunSummary> RunAsync(
        GenerationRequest request, CancellationToken ct = default, Action<GenerationProgress>? progress = null)
    {
        var (facade, config, limit, timeoutMs) = Validate(request);
        var targetBand = request.TargetDifficulty;

        // 提示词组装：few-shot 同难度采样 + 机制范围 + 特殊要求（§3.1）
        // 玩法声明坐标制时改走"坐标出题"规格——格式由 Compose 保证（前后端全流程优化 §8.1）
        var composer = facade as ILevelComposer;
        var maxTokens = facade is ITokenBudgetHint hint ? hint.EstimateLevelTokens(request.LevelNumber) : 8192;
        var mechanicsNote = PromptBuilder.MechanicsNoteFor(facade.Registration.MechanicsJson, request.LevelNumber);
        var fewShot = ConvertExamples(composer, _fewShot.Sample(facade, targetBand));
        var baseMessages = PromptBuilder.Build(
            facade.Registration, targetBand,
            request.LevelNumber, mechanicsNote, request.SpecialRequirements,
            fewShot, composer?.CompositionSpec);
        // 每次生成一个随机种子 + 特殊要求哈希：保证"重新生成/换提示词"能得到不同候选，
        // 而不是 composer 每次返回同一张模板图
        var composeContext = new ComposeContext(
            request.LevelNumber, targetBand, request.SpecialRequirements, Random.Shared.Next());

        var runId = _store.InsertRun(request.GameId, targetBand,
            request.LevelNumber, request.SpecialRequirements, limit, timeoutMs, PromptBuilder.PromptVersion);
        var totalSw = Stopwatch.StartNew();
        var attempts = new List<CandidateAttempt>();
        progress?.Invoke(new GenerationProgress("integration",
            $"生成开始：目标难度 {targetBand} 档，候选上限 {limit}，求解超时 {timeoutMs}ms；模式 {(composer is null ? "原生 JSON" : "坐标制")}"));
        try
        {
            var adoptedCandidateId = 0L;
            var adoptedAttempt = (int?)null;
            (string Class, string Detail)? feedback = null;
            for (var attempt = 1; attempt <= limit; attempt++)
            {
                progress?.Invoke(new GenerationProgress("integration",
                    $"第 {attempt} 次候选开始（预算内第 {attempt}/{limit} 个）", attempt));
                var outcome = await RunAttemptAsync(
                    facade, runId, attempt, targetBand, baseMessages, feedback, config, timeoutMs,
                    composer, maxTokens, request.LevelNumber, composeContext, ct, progress);
                attempts.Add(outcome.Attempt);
                if (outcome.RunEnded)
                {
                    // API 失败：立即收口（基础设施问题不烧候选预算，与内容失败分开计数）
                    var apiSummary = $"LLM API 调用失败（第 {attempt} 次尝试）：{outcome.Attempt.FailureDetail}";
                    _store.FinishRun(runId, "failed", apiSummary, null);
                    progress?.Invoke(new GenerationProgress("integration", apiSummary, attempt, FailureClass.ApiError));
                    return BuildSummary(runId, request, limit, timeoutMs, "failed", attempts, null, apiSummary, totalSw);
                }
                if (outcome.Passed)
                {
                    adoptedCandidateId = outcome.CandidateId;
                    adoptedAttempt = attempt;
                    progress?.Invoke(new GenerationProgress("integration",
                        $"候选 #{attempt} 通过验证并采纳", attempt));
                    break;
                }
                feedback = (outcome.Attempt.FailureClass, outcome.Attempt.FailureDetail ?? "");
                progress?.Invoke(new GenerationProgress("integration",
                    $"候选 #{attempt} 未通过（{outcome.Attempt.FailureClass}），回填失败原因进入下一次重生",
                    attempt, outcome.Attempt.FailureClass));
            }
            if (adoptedAttempt is null)
            {
                // 候选耗尽：只说本次预算内没出符合条件的关，不断言需求不可满足（§3.3）
                var summary = $"候选已耗尽（{attempts.Count}/{limit}）——本次预算内未生成符合当前要求的关卡";
                _store.FinishRun(runId, "failed", summary, null);
                progress?.Invoke(new GenerationProgress("integration", summary));
                return BuildSummary(runId, request, limit, timeoutMs, "failed", attempts, null, summary, totalSw);
            }
            _store.FinishRun(runId, "succeeded", null, adoptedCandidateId);
            progress?.Invoke(new GenerationProgress("integration",
                $"生成成功：第 {adoptedAttempt} 个候选被采纳，总耗时 {totalSw.ElapsedMilliseconds}ms"));
            return BuildSummary(runId, request, limit, timeoutMs, "succeeded", attempts, adoptedAttempt, null, totalSw);
        }
        catch (OperationCanceledException)
        {
            _store.FinishRun(runId, "cancelled", "生成被取消", null);
            progress?.Invoke(new GenerationProgress("integration", "生成被取消"));
            return BuildSummary(runId, request, limit, timeoutMs, "cancelled", attempts, null, "生成被取消", totalSw);
        }
    }

    #region 请求校验

    // 校验并解析运行参数：玩法/LLM 配置/难度档/预算护栏
    private (IGameFacade Facade, LlmConfig Config, int Limit, long TimeoutMs) Validate(GenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GameId) || !_registry.TryGet(request.GameId, out var facade))
        {
            throw new GenerationException($"未知玩法：{request.GameId}");
        }
        // 请求参数先判（与玩法无关的输入错误优先于基础设施问题，报错才不会被误导）
        if (request.TargetDifficulty is < 1 or > 3)
        {
            throw new GenerationException("目标难度必须是 1/2/3");
        }
        var config = LlmConfig.Resolve(_settings);
        if (!config.IsConfigured)
        {
            throw new GenerationException("未配置 LLM：请先在设置页填写地址与模型");
        }
        var limit = Math.Clamp(request.CandidateLimit ?? _settings.Get("gen.candidateLimit", 3), 1, MaxCandidateLimit);
        var timeoutMs = request.SolveTimeoutMs ?? _settings.Get<long>("gen.solveTimeoutMs", 30000);
        return (facade, config, limit, timeoutMs);
    }

    #endregion

    #region 单次尝试

    // 单次尝试结论：Attempt 记录 + 是否通过 + 是否需要立即终止运行 + 候选行 id
    private sealed record AttemptOutcome(CandidateAttempt Attempt, bool Passed, bool RunEnded, long CandidateId);

    // 一次尝试：LLM 出题与格式修复 → 求解判卷 → 难度评估 → 分类落库
    private async Task<AttemptOutcome> RunAttemptAsync(
        IGameFacade facade, long runId, int attempt, int targetBand,
        List<ChatMessage> baseMessages, (string Class, string Detail)? feedback,
        LlmConfig config, long timeoutMs,
        ILevelComposer? composer, int maxTokens, string? levelNumber, ComposeContext composeContext,
        CancellationToken ct, Action<GenerationProgress>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var messages = new List<ChatMessage>(baseMessages);
        if (feedback is { } fb)
        {
            messages.Add(PromptBuilder.FeedbackMessage(attempt - 1, fb.Class, fb.Detail));
        }

        // 阶段1：LLM 出题 + 格式校验 + 修复重试（同一尝试内最多 1+MaxRepairs 次调用）
        var calls = 0;
        string? content = null;
        var formatErrors = new List<string>();
        for (var call = 0; call <= MaxRepairs; call++)
        {
            // 修复轮降温度求确定性（0.8 → 0.2）：同一错误连错两次说明"发散"没帮上忙
            var temperature = call == 0 ? PromptBuilder.TemperatureValue : 0.2;
            progress?.Invoke(new GenerationProgress("llm",
                call == 0
                    ? $"第 {attempt} 次出题（候选 #{attempt}，模型调用中）"
                    : $"第 {attempt} 次出题 · 格式修复重试 {call}/{MaxRepairs}（降温度重发）", attempt));
            // 关闭思考：思考与正文共用 max_tokens，长 JSON 任务里思考会吃光预算导致正文为空
            // （实测格式失败的一大来源；LlmClient 对不认该字段的网关会自动去掉重发）
            var chat = await _llm.ChatAsync(config, messages, temperature, maxTokens,
                jsonMode: true, ct: ct, disableThinking: true);
            calls++;
            if (!chat.Ok)
            {
                // API 层失败：记一条观测候选并立即终止运行
                var apiAttempt = new CandidateAttempt(attempt, null, FailureClass.ApiError,
                    chat.Error, null, null, null, null, calls, sw.ElapsedMilliseconds);
                var apiId = PersistCandidate(runId, apiAttempt);
                progress?.Invoke(new GenerationProgress("llm",
                    $"LLM 调用失败：{chat.Error}", attempt, FailureClass.ApiError));
                return new AttemptOutcome(apiAttempt, Passed: false, RunEnded: true, apiId);
            }
            // 每次调用给一个新种子：同一尝试内的修复轮也不要产出完全相同的候选
            var attemptContext = composeContext with { Seed = Random.Shared.Next() };
            (formatErrors, content) = ValidateCandidate(facade, composer, chat.Content, levelNumber, attemptContext);
            if (formatErrors.Count == 0)
            {
                progress?.Invoke(new GenerationProgress("llm",
                    $"第 {attempt} 次出题通过格式校验（{chat.LatencyMs}ms，{calls} 次调用）", attempt));
                break;
            }
            progress?.Invoke(new GenerationProgress("llm",
                $"格式未通过：{formatErrors[0]}", attempt, FailureClass.FormatInvalid));
            if (call < MaxRepairs)
            {
                // 修复重试：回喂 assistant 原文 + 校验错误（§3.2）
                messages.Add(new ChatMessage("assistant", chat.Content));
                messages.Add(PromptBuilder.RepairMessage(formatErrors));
            }
        }

        // 格式通过后再过玩法声明的质量门：可解但无设计（如全部同朝向/规则网格）不进求解与难度评估
        if (formatErrors.Count == 0 && content is not null && facade is ILevelQualityGate qualityGate)
        {
            var quality = qualityGate.QualityErrors(content);
            if (quality.Count > 0)
            {
                var qualityDetail = $"设计质量未通过：{string.Join("；", quality.Take(3))}";
                var rejected = new CandidateAttempt(attempt, content, FailureClass.ConstraintViolation,
                    qualityDetail, null, null, null, null, calls, sw.ElapsedMilliseconds);
                var id = PersistCandidate(runId, rejected);
                progress?.Invoke(new GenerationProgress("constraint", qualityDetail, attempt, FailureClass.ConstraintViolation));
                return new AttemptOutcome(rejected, Passed: false, RunEnded: false, id);
            }
        }

        // 格式仍未通过：本尝试以 format_invalid 收场，失败详情回填下一轮
        if (formatErrors.Count > 0)
        {
            var formatDetail = GenerationFeedback.FormatInvalid(formatErrors, calls - 1);
            var failed = new CandidateAttempt(attempt, content, FailureClass.FormatInvalid,
                formatDetail, null, null, null, null, calls, sw.ElapsedMilliseconds);
            var id = PersistCandidate(runId, failed);
            return new AttemptOutcome(failed, Passed: false, RunEnded: false, id);
        }

        // 阶段2：求解判卷（预算=单候选超时；诚实语义——未知不冒充无解）
        var bytes = Encoding.UTF8.GetBytes(content!);   // 格式已通过，content 必非空
        var levelRef = facade.TryGetLevelId(content!) is { } levelId ? $"生成:{levelId}" : $"生成:尝试{attempt}";
        progress?.Invoke(new GenerationProgress("solver",
            $"第 {attempt} 次候选进入求解判卷（预算 {timeoutMs}ms）", attempt));
        var outcome = facade.Solve(new SolveCommand(
            content!, bytes, levelRef, TimeBudget: TimeSpan.FromMilliseconds(timeoutMs)));
        var solveRunId = _runs.Insert(facade.Registration, levelRef, TapeHashes.LevelHash(bytes), outcome);
        long? tapeId = null;
        if (outcome.Tape is not null)
        {
            tapeId = _tapes.Save(solveRunId, outcome.Tape).Id;
            _runs.AttachTape(solveRunId, tapeId.Value);
        }
        progress?.Invoke(new GenerationProgress("solver",
            $"求解结论：{outcome.Result.Status}｜解长 {outcome.Result.Solution.Count}｜"
            + $"探索 {outcome.Result.Metrics?.StatesExplored ?? 0} 状态｜{outcome.Result.Metrics?.ElapsedMs ?? 0}ms",
            attempt));

        // 阶段3：分类（难度档命中才放行；玩具未实现约束检查器——如实不虚构机检，特殊要求仅进提示词）
        var status = outcome.Result.Status;
        string failureClass;
        string detail;
        var passed = false;
        var band = outcome.Difficulty.Band;
        var score = outcome.Difficulty.Score;
        if (status == SolveStatus.Solved)
        {
            if (band == targetBand)
            {
                passed = true;
                failureClass = FailureClass.None;
                detail = GenerationFeedback.Passed(outcome);
            }
            else
            {
                failureClass = FailureClass.DifficultyMiss;
                detail = GenerationFeedback.DifficultyMiss(band, score, targetBand);
            }
        }
        else
        {
            failureClass = status switch
            {
                SolveStatus.UnsolvableProven => FailureClass.Unsolvable,
                SolveStatus.ResourceLimit => FailureClass.ResourceLimit,
                SolveStatus.Invalid => FailureClass.FormatInvalid,
                _ => FailureClass.SolveError
            };
            detail = status switch
            {
                SolveStatus.UnsolvableProven => GenerationFeedback.Unsolvable(outcome),
                SolveStatus.ResourceLimit => GenerationFeedback.ResourceLimit(timeoutMs),
                SolveStatus.Invalid => $"求解阶段判定非法输入：{outcome.Result.ErrorMessage}",
                _ => $"求解过程异常：{outcome.Result.ErrorMessage}"
            };
        }
        var record = new CandidateAttempt(
            attempt, content, failureClass, detail,
            band == 0 ? null : band, band == 0 ? null : score,
            solveRunId, tapeId, calls, sw.ElapsedMilliseconds);
        var candidateId = PersistCandidate(runId, record);
        // 难度/分类结论也进事件流：前端进度层能显示"卡在哪一关的哪个环节"
        progress?.Invoke(new GenerationProgress(
            "difficulty",
            band == 0
                ? $"候选 #{attempt} 未评难度（求解结论 {status}）"
                : $"候选 #{attempt} 难度评估 {score:F1}（第 {band} 档），目标第 {targetBand} 档",
            attempt, passed ? null : failureClass));
        return new AttemptOutcome(record, passed, RunEnded: false, candidateId);
    }

    #endregion

    #region 内部工具

    // few-shot 示例统一成坐标制（与要求的输出格式一致）；全部转换失败时退回原生示例
    // 宁可格式不一致，也不能没有示例——few-shot 是提示词里唯一"长什么样"的锚点
    private static IReadOnlyList<string> ConvertExamples(ILevelComposer? composer, IReadOnlyList<string> native)
    {
        if (composer is null)
        {
            return native;
        }
        var converted = native
            .Select(composer.DescribeAsCoordinates)
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
        // 坐标制没有可用示例就"不给示例"，绝不回退原生格式：原生 95 猪 JSON 会撑爆上下文、
        // 并让模型照抄原生几何（与 CompositionSpec 要求的输出格式冲突）
        return converted;
    }

    // 候选校验：玩法声明坐标制时，先从"设计意图"拼装出原生关卡，再过一次原生校验器
    // 双重校验的意义：拼装器自身也可能有 bug，原生校验是最后一道闸（不因中间层存在就跳过）
    // 返回 (错误清单, 可用的原生关卡 JSON)；错误为空时 Content 必非空
    private static (List<string> Errors, string? Content) ValidateCandidate(
        IGameFacade facade, ILevelComposer? composer, string? modelOutput, string? levelNumber,
        ComposeContext composeContext)
    {
        var extracted = LlmJson.ExtractJson(modelOutput ?? "");
        if (extracted is null)
        {
            return (["输出中未找到 JSON 对象（请只输出一个 JSON 对象，不要解释文字）"], null);
        }
        if (composer is null)
        {
            return (facade.FormatErrors(extracted).ToList(), extracted);
        }
        var composed = composer.Compose(extracted, levelNumber, composeContext);
        if (composed.LevelJson is null)
        {
            return (composed.Errors.ToList(), null);
        }
        var errors = facade.FormatErrors(composed.LevelJson).ToList();
        return (errors, errors.Count == 0 ? composed.LevelJson : null);
    }

    // 落一条候选
    private long PersistCandidate(long runId, CandidateAttempt attempt)
    {
        return _store.InsertCandidate(runId, attempt.AttemptIndex, attempt.ContentJson,
            attempt.FailureClass, attempt.FailureDetail, attempt.DifficultyBand,
            attempt.DifficultyScore, attempt.SolveRunId, attempt.ElapsedMs);
    }

    // 组装运行结论
    private static GenerationRunSummary BuildSummary(
        long runId, GenerationRequest request, int limit, long timeoutMs,
        string status, IReadOnlyList<CandidateAttempt> candidates, int? adoptedAttempt,
        string? failureSummary, Stopwatch sw)
    {
        return new GenerationRunSummary(
            runId, request.GameId, status, request.TargetDifficulty,
            request.LevelNumber, request.SpecialRequirements, limit, timeoutMs,
            PromptBuilder.PromptVersion, candidates, adoptedAttempt, failureSummary,
            sw.ElapsedMilliseconds);
    }

    #endregion
}
