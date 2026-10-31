using System.Collections.Concurrent;
using System.Text;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Storage;

// 落盘结论：Ok=false 时 Error 给原因；冲突时 ConflictHash 带旧文件指纹（前端替换确认用）
public sealed record LevelPersistOutcome(
    bool Ok,
    string? Error,                       // 失败原因（格式/越界/指纹不符）
    string? LevelId,                     // 关卡编号
    string? RelativePath,                // 相对数据目录的文件路径
    string? LevelHash,                   // 拟写入/已写入字节的 sha256
    bool SameContent,                    // 同编号同内容已存在（幂等采纳：不重写不重复登记）
    string? ConflictHash);               // 编号冲突时已有文件的指纹

// 安全落盘引擎：表现层_生成工作台 §4.1 六条流程的唯一实现（单关/质检/批量共用）
// ①冻结产物 ②限定目录 ③编号冲突处理 ④防半写 ⑤核对最终文件 ⑥生命周期推进（登记在调用方）
public sealed class LevelFileStore
{
    private readonly AppDatabase _db;
    private readonly ILevelFileIO _io;

    // 同编号写入串行化：并发采纳同一编号时指纹复查才有效（§4.1③）
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new();

    public LevelFileStore(AppDatabase db, ILevelFileIO io)
    {
        _db = db;
        _io = io;
    }

    // 落盘一份原生格式关卡；任何一步不过关都不碰目标文件
    public LevelPersistOutcome Persist(IGameFacade facade, string contentJson, bool overwrite, string? expectedOldHash)
    {
        // ① 冻结产物：先过格式校验——写入的是经过验证的合法关卡原文（不是试玩中途态）
        var errors = facade.FormatErrors(contentJson);
        if (errors.Count > 0)
        {
            return Fail($"候选未通过格式校验：{string.Join("；", errors)}");
        }
        var levelId = facade.TryGetLevelId(contentJson);
        if (string.IsNullOrWhiteSpace(levelId))
        {
            return Fail("关卡缺少 id 字段，无法确定文件名");
        }
        // ② 限定目录：id 必须是安全文件名（拒绝路径注入/穿越），目标必须落在解析后的关卡根内
        if (!IsSafeLevelId(levelId))
        {
            return Fail($"关卡编号含非法字符（只允许字母数字._-）：{levelId}");
        }
        var root = Path.GetFullPath(Path.Combine(_db.DataDir, facade.Registration.LevelsRootDir));
        var target = Path.GetFullPath(Path.Combine(root, $"{levelId}.json"));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("目标路径越出关卡根目录");   // 双保险：id 净化后不应触发
        }
        var bytes = Encoding.UTF8.GetBytes(contentJson);
        var hash = TapeHashes.LevelHash(bytes);
        System.IO.Directory.CreateDirectory(root);

        var gate = PathLocks.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            // ③ 编号冲突：同内容幂等放行；不覆盖时带旧指纹退回；替换前复查旧指纹
            string? replacedHash = null;
            if (_io.Exists(target))
            {
                var existingHash = TapeHashes.LevelHash(_io.ReadAllBytes(target));
                if (existingHash == hash)
                {
                    // 同编号同内容：不重写盘（幂等），调用方据此跳过重复登记
                    return new LevelPersistOutcome(true, null, levelId, RelativeOf(root, target), hash, SameContent: true, null);
                }
                if (!overwrite)
                {
                    return new LevelPersistOutcome(false, $"编号 {levelId} 已存在（内容不同），默认不覆盖", levelId, RelativeOf(root, target), hash, false, existingHash);
                }
                if (expectedOldHash is null || !string.Equals(expectedOldHash, existingHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new LevelPersistOutcome(false, "旧文件指纹不符，替换被拒绝（请刷新旧指纹后再次确认）", levelId, RelativeOf(root, target), hash, false, existingHash);
                }
                replacedHash = existingHash;
            }
            // ④ 防半写：同目录临时文件 → 回读校验 → 原子替换；任何失败清理现场、不碰目标
            var temp = Path.Combine(root, $".{levelId}.json.tmp-{Guid.NewGuid():N}");
            try
            {
                _io.WriteAllBytes(temp, bytes);
                if (!_io.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes))
                {
                    throw new IOException("临时文件回读校验失败");
                }
                _io.MoveOverwrite(temp, target);
            }
            catch
            {
                TryDelete(temp);
                throw;   // 不降级成直接写目标文件；原文件（若存在）保持完好
            }
            // ⑤ 核对最终文件：发布后重读，字节哈希必须与候选一致
            if (TapeHashes.LevelHash(_io.ReadAllBytes(target)) != hash)
            {
                throw new IOException("发布后校验失败：最终文件哈希与候选不一致");
            }
            return new LevelPersistOutcome(true, null, levelId, RelativeOf(root, target), hash, SameContent: false, replacedHash);
        }
        finally
        {
            gate.Release();
        }
    }

    // 按编号取当前文件内容；文件缺失返回 null（调用方按"文件缺失/被改"标异常）
    public byte[]? ReadCurrent(IGameFacade facade, string levelId)
    {
        if (!IsSafeLevelId(levelId)) return null;
        var target = Path.Combine(_db.DataDir, facade.Registration.LevelsRootDir, $"{levelId}.json");
        return _io.Exists(target) ? _io.ReadAllBytes(target) : null;
    }

    #region 内部工具

    // 失败结论速记
    private static LevelPersistOutcome Fail(string error) => new(false, error, null, null, null, false, null);

    // 安全编号：字母数字开头，仅字母数字._-，且不含 ..（拒绝路径注入与穿越）
    private static bool IsSafeLevelId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Contains("..")) return false;
        var ok = id[0] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');
        if (!ok) return false;
        return id.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-');
    }

    // 相对路径（统一分隔符，回给前端与落库）
    private static string RelativeOf(string root, string target) => target.Substring(root.Length + 1);

    // 临时文件清理（尽力而为，失败不再抛）
    private void TryDelete(string path)
    {
        try { _io.Delete(path); }
        catch (IOException) { }
    }

    #endregion
}
