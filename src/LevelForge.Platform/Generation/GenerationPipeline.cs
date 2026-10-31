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
    public async Task<GenerationRunSummary> RunAsync(GenerationRequest request, CancellationToken ct = default)
    {
        var (facade, config, limit, timeoutMs) = Validate(request);
        var targetBand = request.TargetDifficulty;

        // 提示词组装：few-shot 同难度采样 + 机制范围 + 特殊要求（§3.1）
        var mechanicsNote = PromptBuilder.MechanicsNoteFor(facade.Registration.MechanicsJson, request.LevelNumber);
        var baseMessages = PromptBuilder.Build(
            facade.Registration, targetBand,
            request.LevelNumber, mechanicsNote, request.SpecialRequirements,
            _fewShot.Sample(facade, targetBand));

        var runId = _store.InsertRun(request.GameId, targetBand,
            request.LevelNumber, request.SpecialRequirements, limit, timeoutMs, PromptBuilder.PromptVersion);
        var totalSw = Stopwatch.StartNew();
        var attempts = new List<CandidateAttempt>();
        try
        {
            var adoptedCandidateId = 0L;
            var adoptedAttempt = (int?)null;
            (string Class, string Detail)? feedback = null;
            for (var attempt = 1; attempt <= limit; attempt++)
            {
                var outcome = await RunAttemptAsync(
                    facade, runId, attempt, targetBand, baseMessages, feedback, config, timeoutMs, ct);
                attempts.Add(outcome.Attempt);
                if (outcome.RunEnded)
                {
                    // API 失败：立即收口（基础设施问题不烧候选预算，与内容失败分开计数）
                    var apiSummary = $"LLM API 调用失败（第 {attempt} 次尝试）：{outcome.Attempt.FailureDetail}";
                    _store.FinishRun(runId, "failed", apiSummary, null);
                    return BuildSummary(runId, request, limit, timeoutMs, "failed", attempts, null, apiSummary, totalSw);
                }
                if (outcome.Passed)
                {
                    adoptedCandidateId = outcome.CandidateId;
                    adoptedAttempt = attempt;
                    break;
                }
                feedback = (outcome.Attempt.FailureClass, outcome.Attempt.FailureDetail ?? "");
            }
            if (adoptedAttempt is null)
            {
                // 候选耗尽：只说本次预算内没出符合条件的关，不断言需求不可满足（§3.3）
                var summary = $"候选已耗尽（{attempts.Count}/{limit}）——本次预算内未生成符合当前要求的关卡";
                _store.FinishRun(runId, "failed", summary, null);
                return BuildSummary(runId, request, limit, timeoutMs, "failed", attempts, null, summary, totalSw);
            }
            _store.FinishRun(runId, "succeeded", null, adoptedCandidateId);
            return BuildSummary(runId, request, limit, timeoutMs, "succeeded", attempts, adoptedAttempt, null, totalSw);
        }
        catch (OperationCanceledException)
        {
            _store.FinishRun(runId, "cancelled", "生成被取消", null);
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
        var config = LlmConfig.Resolve(_settings);
        if (!config.IsConfigured)
        {
            throw new GenerationException("未配置 LLM：请先在设置页填写地址与模型");
        }
        if (request.TargetDifficulty is < 1 or > 3)
        {
            throw new GenerationException("目标难度必须是 1/2/3");
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
        LlmConfig config, long timeoutMs, CancellationToken ct)
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
            var chat = await _llm.ChatAsync(config, messages, PromptBuilder.TemperatureValue, ct: ct);
            calls++;
            if (!chat.Ok)
            {
                // API 层失败：记一条观测候选并立即终止运行
                var apiAttempt = new CandidateAttempt(attempt, null, FailureClass.ApiError,
                    chat.Error, null, null, null, null, calls, sw.ElapsedMilliseconds);
                var apiId = PersistCandidate(runId, apiAttempt);
                return new AttemptOutcome(apiAttempt, Passed: false, RunEnded: true, apiId);
            }
            content = LlmJson.ExtractJson(chat.Content);
            formatErrors = content is null
                ? ["输出中未找到 JSON 对象（请只输出关卡 JSON 原文）"]
                : facade.FormatErrors(content).ToList();
            if (formatErrors.Count == 0)
            {
                break;
            }
            if (call < MaxRepairs)
            {
                // 修复重试：回喂 assistant 原文 + 校验错误（§3.2）
                messages.Add(new ChatMessage("assistant", chat.Content));
                messages.Add(PromptBuilder.RepairMessage(formatErrors));
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
        var outcome = facade.Solve(new SolveCommand(
            content!, bytes, levelRef, TimeBudget: TimeSpan.FromMilliseconds(timeoutMs)));
        var solveRunId = _runs.Insert(facade.Registration, levelRef, TapeHashes.LevelHash(bytes), outcome);
        long? tapeId = null;
        if (outcome.Tape is not null)
        {
            tapeId = _tapes.Save(solveRunId, outcome.Tape).Id;
            _runs.AttachTape(solveRunId, tapeId.Value);
        }

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
        return new AttemptOutcome(record, passed, RunEnded: false, candidateId);
    }

    #endregion

    #region 内部工具

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
