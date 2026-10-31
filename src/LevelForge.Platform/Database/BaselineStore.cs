using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 基准行：baselines 表整行（验证门 §5.1 LevelBaseline 全字段）
public sealed record BaselineRow(
    long Id, string GameId, string LevelId, string LevelHash,
    string AdapterVer, string AdapterHash, string KernelVer, string ContractVer,
    string? GameBuildFingerprint, string? HarnessHash, string? RunConfigHash,
    string Status, string Optimality, bool GraphComplete, string TerminationReason,
    int SolutionLength, long StatesExplored, string TrajectoryHash, string? ArtifactHash,
    string? ExecutionConfirmationRef, string? ApprovedBy, string? ApprovedAt, string CreatedAt);

// 基准存取：人工确认后追加（版本维度唯一，不静默覆盖；重复确认不重复追加）
public sealed class BaselineStore
{
    private readonly AppDatabase _db;

    public BaselineStore(AppDatabase db)
    {
        _db = db;
    }

    // 入基准（幂等）：同 (玩法,编号,适配指纹,内核版本) 已存在时不追加，返回既有行 id 与 already 标记
    public (long Id, bool AlreadyExists) Insert(
        string gameId, string levelId, string levelHash,
        string adapterVer, string adapterHash, string kernelVer, string contractVer,
        string? gameBuildFingerprint, string? runConfigHash,
        string status, string optimality, bool graphComplete, string terminationReason,
        int solutionLength, long statesExplored,
        string trajectoryHash, string? artifactHash,
        string? executionConfirmationRef, string approvedBy)
    {
        if (FindExisting(gameId, levelId, adapterHash, kernelVer) is { } existing)
        {
            return (existing.Id, true);   // 重复确认相同文件与证据不重复追加
        }
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO baselines (game_id, level_id, level_hash, adapter_ver, adapter_hash, kernel_ver,
              contract_ver, game_build_fingerprint, harness_hash, run_config_hash, status, optimality,
              graph_complete, termination_reason, solution_length, states_explored, trajectory_hash,
              artifact_hash, execution_confirmation_ref, approved_by, approved_at, created_at)
            VALUES ($g, $l, $lh, $av, $ah, $kv, $cv, $gbf, NULL, $rch, $st, $op, $gc, $tr, $sl, $se,
              $th, $ar, $ecr, $by, $now, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$lh", levelHash);
        cmd.Parameters.AddWithValue("$av", adapterVer);
        cmd.Parameters.AddWithValue("$ah", adapterHash);
        cmd.Parameters.AddWithValue("$kv", kernelVer);
        cmd.Parameters.AddWithValue("$cv", contractVer);
        cmd.Parameters.AddWithValue("$gbf", (object?)gameBuildFingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rch", (object?)runConfigHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$op", optimality);
        cmd.Parameters.AddWithValue("$gc", graphComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$tr", terminationReason);
        cmd.Parameters.AddWithValue("$sl", solutionLength);
        cmd.Parameters.AddWithValue("$se", statesExplored);
        cmd.Parameters.AddWithValue("$th", trajectoryHash);
        cmd.Parameters.AddWithValue("$ar", (object?)artifactHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ecr", (object?)executionConfirmationRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$by", approvedBy);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return ((long)cmd.ExecuteScalar()!, false);
    }

    // 同版本维度找既有基准（幂等判定）
    public BaselineRow? FindExisting(string gameId, string levelId, string adapterHash, string kernelVer)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE game_id = $g AND level_id = $l AND adapter_hash = $ah AND kernel_ver = $kv LIMIT 1";
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$ah", adapterHash);
        cmd.Parameters.AddWithValue("$kv", kernelVer);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    // 玩法下全部基准（门3 diff 的对照物；第11步消费）
    public IReadOnlyList<BaselineRow> ListByGame(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE game_id = $g ORDER BY level_id, id";
        cmd.Parameters.AddWithValue("$g", gameId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<BaselineRow>();
        while (reader.Read()) rows.Add(Map(reader));
        return rows;
    }

    // 按编号取最新基准（单关 diff 用）
    public BaselineRow? FindLatestByLevel(string gameId, string levelId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE game_id = $g AND level_id = $l ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    #region 内部工具

    // 统一列序（与 Map 对齐）
    private const string SelectSql = """
        SELECT id, game_id, level_id, level_hash, adapter_ver, adapter_hash, kernel_ver, contract_ver,
          game_build_fingerprint, harness_hash, run_config_hash, status, optimality, graph_complete,
          termination_reason, solution_length, states_explored, trajectory_hash, artifact_hash,
          execution_confirmation_ref, approved_by, approved_at, created_at
        FROM baselines
        """;

    // 行映射
    private static BaselineRow Map(SqliteDataReader reader)
    {
        return new BaselineRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11), reader.GetString(12), reader.GetInt64(13) != 0,
            reader.GetString(14), reader.GetInt32(15), reader.GetInt64(16),
            reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.GetString(22));
    }

    #endregion
}
