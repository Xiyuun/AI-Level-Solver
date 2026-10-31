using System.Text;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using LevelForge.Platform.Storage;

namespace LevelForge.Platform.Adoption;

// 采纳结论：落盘 + 登记 + 确认引用一次拿全
public sealed record AdoptOutcome(
    string LevelId, string LevelHash, string RelativePath,
    bool SameContent, bool Replaced,           // 同内容幂等 / 替换了旧文件
    long ConfirmationId, string ConfirmationState, long? SolveRunId);

// 采纳失败（请求/校验层）：端点转 400
public sealed class AdoptionException(string message) : Exception(message);

// 编号冲突（含旧指纹，前端替换确认用）：端点转 409
public sealed class AdoptionConflictException(string message, string? conflictHash)
    : Exception(message)
{
    public string? ConflictHash { get; } = conflictHash;
}

// 采纳服务：安全落盘六条流程之上的生命周期编排（表现层_生成工作台 §4.1⑥ + §4.2）
// 单关生成候选 / 质检粘贴共用同一入口；重复采纳同一文件不重复写盘、不重复登记
public sealed class AdoptionService
{
    private readonly GameRegistry _registry;
    private readonly LevelFileStore _files;
    private readonly GameStore _games;
    private readonly SolveRunStore _runs;
    private readonly TapeStore _tapes;
    private readonly ConfirmationStore _confirmations;

    public AdoptionService(
        GameRegistry registry, LevelFileStore files, GameStore games,
        SolveRunStore runs, TapeStore tapes, ConfirmationStore confirmations)
    {
        _registry = registry;
        _files = files;
        _games = games;
        _runs = runs;
        _tapes = tapes;
        _confirmations = confirmations;
    }

    // 采纳落盘：persist → （幂等短路）→ 模型验证求解 → levels 登记 → 旧确认失效 → 登记待执行确认
    public AdoptOutcome Adopt(string gameId, string contentJson, string source, bool overwrite, string? expectedOldHash)
    {
        if (string.IsNullOrWhiteSpace(gameId) || !_registry.TryGet(gameId, out var facade))
        {
            throw new AdoptionException($"未知玩法：{gameId}");
        }
        // 落盘（六条流程在 LevelFileStore 内闭环；失败不碰目标文件）
        var persist = _files.Persist(facade, contentJson, overwrite, expectedOldHash);
        if (!persist.Ok)
        {
            if (persist.ConflictHash is not null)
            {
                throw new AdoptionConflictException(persist.Error!, persist.ConflictHash);
            }
            throw new AdoptionException(persist.Error!);
        }
        var levelId = persist.LevelId!;
        var levelHash = persist.LevelHash!;

        // 幂等短路：同文件已有未失效确认 → 什么都不做（不重写盘、不重复登记）
        if (persist.SameContent && _confirmations.FindActiveByHash(gameId, levelId, levelHash) is { } existing)
        {
            return new AdoptOutcome(levelId, levelHash, persist.RelativePath!, true, false,
                existing.Id, existing.State, existing.SolveRunId);
        }

        // 模型验证求解（最终内容 = 候选字节，落盘第⑤步已核对哈希一致，验证对本文件有效）
        var bytes = Encoding.UTF8.GetBytes(contentJson);
        var solve = facade.Solve(new SolveCommand(contentJson, bytes, levelId));
        var solveRunId = _runs.Insert(facade.Registration, levelId, levelHash, solve);
        if (solve.Tape is not null)
        {
            var tapeId = _tapes.Save(solveRunId, solve.Tape).Id;
            _runs.AttachTape(solveRunId, tapeId);
        }

        // 登记 levels（落盘成功但此处失败时：重试 Adopt 走 SameContent 路径恢复登记，不重复写盘）
        _games.UpsertLevel(gameId, levelId, levelHash, contentJson, source);

        // 文件内容变化：该编号旧确认全部失效（§4.2）
        _confirmations.InvalidateByLevel(gameId, levelId, levelHash, "文件内容已变化，旧确认失效");

        var confirmationId = _confirmations.InsertPending(gameId, levelId, levelHash, solveRunId);
        return new AdoptOutcome(levelId, levelHash, persist.RelativePath!, persist.SameContent,
            persist.ConflictHash is not null, confirmationId, "pending", solveRunId);
    }

    // 当前文件核对：ok=与确认一致 / missing=文件没了 / hashChanged=内容变了（显示层诚实标注）
    public (string Status, string? CurrentHash) CheckCurrentFile(string gameId, string levelId, string expectedHash)
    {
        if (!_registry.TryGet(gameId, out var facade))
        {
            return ("unknownGame", null);
        }
        var bytes = _files.ReadCurrent(facade, levelId);
        if (bytes is null) return ("missing", null);
        var hash = TapeHashes.LevelHash(bytes);
        return (hash == expectedHash ? "ok" : "hashChanged", hash);
    }
}
