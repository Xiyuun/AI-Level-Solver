using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 生成运行行：generation_runs 表整行
public sealed record GenerationRunRow(
    long Id, string GameId, string PromptVersion, int TargetDifficulty,
    string? LevelNumber, string? SpecialRequirements, int CandidateLimit, long SolveTimeoutMs,
    string Status, string? FailureSummary, long? AdoptedCandidateId,
    string StartedAt, string? FinishedAt);

// 生成候选行：generation_candidates 表整行
public sealed record GenerationCandidateRow(
    long Id, long RunId, int AttemptIndex, string ContentJson,
    string FailureClass, string? FailureDetail, int? DifficultyBand, double? DifficultyScore,
    long? SolveRunId, long ElapsedMs, string CreatedAt);

// 生成存取：一次生成 = 一行运行 + 每次尝试一行候选（观测落库：失败原因 Top 榜的数据底座）
public sealed class GenerationStore
{
    private readonly AppDatabase _db;

    public GenerationStore(AppDatabase db)
    {
        _db = db;
    }

    // 起一条运行（status=running），返回新行 id
    public long InsertRun(string gameId, int targetDifficulty, string? levelNumber,
        string? specialRequirements, int candidateLimit, long solveTimeoutMs, string promptVersion)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO generation_runs (game_id, prompt_version, target_difficulty, level_number,
              special_requirements, candidate_limit, solve_timeout_ms, status, started_at)
            VALUES ($g, $pv, $td, $ln, $sr, $cl, $st, 'running', $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$pv", promptVersion);
        cmd.Parameters.AddWithValue("$td", targetDifficulty);
        cmd.Parameters.AddWithValue("$ln", (object?)levelNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sr", (object?)specialRequirements ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cl", candidateLimit);
        cmd.Parameters.AddWithValue("$st", solveTimeoutMs);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return (long)cmd.ExecuteScalar()!;
    }

    // 收口一条运行：终态/失败总结/被采纳候选
    public void FinishRun(long runId, string status, string? failureSummary, long? adoptedCandidateId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE generation_runs
            SET status = $s, failure_summary = $f, adopted_candidate_id = $a, finished_at = $now
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$f", (object?)failureSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", (object?)adoptedCandidateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.ExecuteNonQuery();
    }

    // 落一条候选，返回新行 id
    public long InsertCandidate(long runId, int attemptIndex, string? contentJson,
        string failureClass, string? failureDetail, int? difficultyBand, double? difficultyScore,
        long? solveRunId, long elapsedMs)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO generation_candidates (run_id, attempt_index, content_json, failure_class,
              failure_detail, difficulty_band, difficulty_score, solve_run_id, elapsed_ms, created_at)
            VALUES ($r, $i, $c, $fc, $fd, $db, $ds, $sr, $em, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$r", runId);
        cmd.Parameters.AddWithValue("$i", attemptIndex);
        cmd.Parameters.AddWithValue("$c", (object?)contentJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fc", failureClass);
        cmd.Parameters.AddWithValue("$fd", (object?)failureDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$db", (object?)difficultyBand ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ds", (object?)difficultyScore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sr", (object?)solveRunId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$em", elapsedMs);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return (long)cmd.ExecuteScalar()!;
    }

    // 取运行；不存在返回 null
    public GenerationRunRow? GetRun(long runId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, game_id, prompt_version, target_difficulty, level_number, special_requirements,
              candidate_limit, solve_timeout_ms, status, failure_summary, adopted_candidate_id,
              started_at, finished_at
            FROM generation_runs WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", runId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new GenerationRunRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6), reader.GetInt64(7), reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    // 某运行的候选列表（按尝试序）
    public IReadOnlyList<GenerationCandidateRow> ListCandidates(long runId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, run_id, attempt_index, content_json, failure_class, failure_detail,
              difficulty_band, difficulty_score, solve_run_id, elapsed_ms, created_at
            FROM generation_candidates WHERE run_id = $r ORDER BY attempt_index
            """;
        cmd.Parameters.AddWithValue("$r", runId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<GenerationCandidateRow>();
        while (reader.Read())
        {
            rows.Add(new GenerationCandidateRow(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.GetInt64(9), reader.GetString(10)));
        }
        return rows;
    }
}
