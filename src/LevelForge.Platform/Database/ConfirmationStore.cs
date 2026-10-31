using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 执行确认状态机取值（表现层_生成工作台 §4.2）
public static class ConfirmationState
{
    public const string Pending = "pending";             // 未执行（落盘后登记）
    public const string Running = "running";             // 执行中（门2/执行器运行）
    public const string Passed = "passed";               // 游戏执行通过
    public const string Failed = "failed";               // 执行失败
    public const string Invalidated = "invalidated";     // 内容/依赖变化已失效
}

// 执行确认行：execution_confirmations 表整行
public sealed record ConfirmationRow(
    long Id, string GameId, string LevelId, string LevelHash, string State, string Mode,
    string? GameBuildFingerprint, string? ReportRef, string? Outcome, string? ConfirmedBy,
    string? ConfirmedAt, string? Note, long? SolveRunId, string CreatedAt, string UpdatedAt);

// 执行确认存取：当前文件在游戏实际执行的状态机（落盘→pending；执行结论→passed/failed；内容变化→invalidated）
public sealed class ConfirmationStore
{
    private readonly AppDatabase _db;

    public ConfirmationStore(AppDatabase db)
    {
        _db = db;
    }

    // 登记待执行确认（落盘后调用）；solveRunId 关联落盘时的模型验证运行
    public long InsertPending(string gameId, string levelId, string levelHash, long? solveRunId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO execution_confirmations (game_id, level_id, level_hash, state, mode,
              solve_run_id, note, created_at, updated_at)
            VALUES ($g, $l, $h, 'pending', 'none', $s, $note, $now, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$h", levelHash);
        cmd.Parameters.AddWithValue("$s", (object?)solveRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", "待游戏执行确认");
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return (long)cmd.ExecuteScalar()!;
    }

    // 取单条；不存在返回 null
    public ConfirmationRow? Get(long id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    // 玩法下全部确认（按登记序倒序）
    public IReadOnlyList<ConfirmationRow> ListByGame(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE game_id = $g ORDER BY id DESC";
        cmd.Parameters.AddWithValue("$g", gameId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<ConfirmationRow>();
        while (reader.Read()) rows.Add(Map(reader));
        return rows;
    }

    // 同编号同内容且未失效的最新确认（幂等采纳复用：重复采纳不重复登记）
    public ConfirmationRow? FindActiveByHash(string gameId, string levelId, string levelHash)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + """
             WHERE game_id = $g AND level_id = $l AND level_hash = $h AND state != 'invalidated'
            ORDER BY id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$h", levelHash);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    // 文件内容变化：该编号其余确认全部失效（§4.2：内容或依赖变化已失效）
    public void InvalidateByLevel(string gameId, string levelId, string exceptHash, string note)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE execution_confirmations
            SET state = 'invalidated', note = $note, updated_at = $now
            WHERE game_id = $g AND level_id = $l AND level_hash != $h AND state != 'invalidated'
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$h", exceptHash);
        cmd.Parameters.AddWithValue("$note", note);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.ExecuteNonQuery();
    }

    // 人工降级结论：注明"仅最终结果"（验证门 §4.4）；win→passed，其余→failed
    public void RecordManualResult(long id, string outcome, string confirmedBy, string? note)
    {
        var state = outcome == "win" ? ConfirmationState.Passed : ConfirmationState.Failed;
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE execution_confirmations
            SET state = $s, mode = 'manual', outcome = $o, confirmed_by = $by, confirmed_at = $now,
                note = COALESCE($note, note), updated_at = $now
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$o", outcome);
        cmd.Parameters.AddWithValue("$by", confirmedBy);
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // 导入执行报告（自动模式）：报告已通过指纹校验后落结论
    public void ApplyReport(long id, string reportRef, string outcome, string? gameBuildFingerprint)
    {
        var state = outcome == "win" ? ConfirmationState.Passed : ConfirmationState.Failed;
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE execution_confirmations
            SET state = $s, mode = 'auto', outcome = $o, report_ref = $ref,
                game_build_fingerprint = COALESCE($gbf, game_build_fingerprint),
                confirmed_at = $now, updated_at = $now
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$s", state);
        cmd.Parameters.AddWithValue("$o", outcome);
        cmd.Parameters.AddWithValue("$ref", reportRef);
        cmd.Parameters.AddWithValue("$gbf", (object?)gameBuildFingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // 标记执行中（门2/执行器启动时）
    public void MarkRunning(long id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE execution_confirmations SET state = 'running', updated_at = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    #region 内部工具

    // 统一列序（与 Map 对齐）
    private const string SelectSql = """
        SELECT id, game_id, level_id, level_hash, state, mode, game_build_fingerprint, report_ref,
          outcome, confirmed_by, confirmed_at, note, solve_run_id, created_at, updated_at
        FROM execution_confirmations
        """;

    // 行映射
    private static ConfirmationRow Map(SqliteDataReader reader)
    {
        return new ConfirmationRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12),
            reader.GetString(13), reader.GetString(14));
    }

    #endregion
}
