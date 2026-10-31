using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Database;
using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Storage;

// 磁带索引行：tapes 表行（文件路径相对 data 目录）
public sealed record TapeRow(
    long Id, long RunId, string FilePath, string TrajectoryHash, string ArtifactHash, string CreatedAt);

// 磁带仓库：磁带 JSON 落文件（唯一持久化形态）+ tapes 表索引
// 渲染/回放/对拍统一从这里取同一份文件——预览与对拍引用同一关卡及动作记录（方案书 §7 证据可追溯）
public sealed class TapeStore
{
    private readonly AppDatabase _db;

    public TapeStore(AppDatabase db)
    {
        _db = db;
    }

    // 保存：先插索引取 id → 写文件 → 回填路径；写盘失败删索引不留半截记录
    public TapeRow Save(long runId, SolutionTape tape)
    {
        var json = TapeJson.Serialize(tape);
        using var conn = _db.Open();
        long id;
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO tapes (run_id, file_path, trajectory_hash, artifact_hash, created_at)
                VALUES ($r, '', $th, $ah, $now);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$r", runId);
            insert.Parameters.AddWithValue("$th", tape.TrajectoryHash);
            insert.Parameters.AddWithValue("$ah", tape.ArtifactHash);
            insert.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
            id = (long)insert.ExecuteScalar()!;
        }
        // 文件名用 id：一条运行至多一条磁带，天然唯一且可追溯
        var relative = Path.Combine("tapes", $"{id}.tape.json");
        var fullPath = Path.Combine(_db.DataDir, relative);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, json);
        }
        catch
        {
            using var rollback = conn.CreateCommand();
            rollback.CommandText = "DELETE FROM tapes WHERE id = $id";
            rollback.Parameters.AddWithValue("$id", id);
            rollback.ExecuteNonQuery();
            throw;   // 写盘失败如实上抛，不登记成功（表现层_生成工作台 §4.1 的"不降级"精神）
        }
        using var update = conn.CreateCommand();
        update.CommandText = "UPDATE tapes SET file_path = $p WHERE id = $id";
        update.Parameters.AddWithValue("$p", relative);
        update.Parameters.AddWithValue("$id", id);
        update.ExecuteNonQuery();
        return new TapeRow(id, runId, relative, tape.TrajectoryHash, tape.ArtifactHash, AppDatabase.UtcNowIso);
    }

    // 读磁带 JSON 原文；不存在返回 null（文件丢失也返回 null——索引在文件不在 = 异常态，由调用方暴露）
    public string? LoadJson(long tapeId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT file_path FROM tapes WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", tapeId);
        if (cmd.ExecuteScalar() is not string relative) return null;
        var fullPath = Path.Combine(_db.DataDir, relative);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }
}
