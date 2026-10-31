using System.Text.Json;
using LevelForge.Platform.Facades;
using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 求解运行行：solve_runs 表整行（五维结论 + metrics + 指纹 + 磁带索引）
public sealed record SolveRunRow(
    long Id, string GameId, string LevelRef, string LevelHash,
    string AdapterHash, string KernelVer, string ContractVer, string? RunConfigHash,
    string Status, string Optimality, bool GraphComplete, string TerminationReason, string MetricsScope,
    int SolutionLength, long StatesExplored, long UniqueStates, long FrontierPeak,
    long ElapsedMs, double AvgBranching, string? ErrorMessage, long? TapeId, string CreatedAt);

// 求解运行存取：一次求解 = 一行记录；结论按维度落列，维度间不互相升格
public sealed class SolveRunStore
{
    private readonly AppDatabase _db;

    // 枚举落库口径：camelCase 字符串（与 API JSON 输出一致，如 unsolvableProven）
    private static readonly JsonNamingPolicy Camel = JsonNamingPolicy.CamelCase;

    public SolveRunStore(AppDatabase db)
    {
        _db = db;
    }

    // 落一条运行记录（指纹来自注册表与关卡字节，不依赖是否有磁带），返回新行 id
    public long Insert(GameRegistration reg, string levelRef, string levelHash, PlatformSolve outcome)
    {
        var r = outcome.Result;
        var m = r.Metrics;
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO solve_runs (game_id, level_ref, level_hash, adapter_hash, kernel_ver, contract_ver,
              run_config_hash, status, optimality, graph_complete, termination_reason, metrics_scope,
              solution_length, states_explored, unique_states, frontier_peak, elapsed_ms, avg_branching,
              error_message, created_at)
            VALUES ($g, $ref, $lh, $ah, $kv, $cv, $rch, $st, $op, $gc, $tr, $ms,
              $sl, $se, $us, $fp, $em, $ab, $err, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", reg.GameId);
        cmd.Parameters.AddWithValue("$ref", levelRef);
        cmd.Parameters.AddWithValue("$lh", levelHash);
        cmd.Parameters.AddWithValue("$ah", reg.AdapterHash);
        cmd.Parameters.AddWithValue("$kv", reg.KernelVersion);
        cmd.Parameters.AddWithValue("$cv", reg.ContractVersion);
        cmd.Parameters.AddWithValue("$rch", (object?)outcome.RunConfigHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$st", Camel.ConvertName(r.Status.ToString()));
        cmd.Parameters.AddWithValue("$op", Camel.ConvertName(r.Optimality.ToString()));
        cmd.Parameters.AddWithValue("$gc", r.GraphComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$tr", Camel.ConvertName(r.TerminationReason.ToString()));
        cmd.Parameters.AddWithValue("$ms", Camel.ConvertName(r.MetricsScope.ToString()));
        cmd.Parameters.AddWithValue("$sl", r.Solution.Count);
        cmd.Parameters.AddWithValue("$se", m?.StatesExplored ?? 0);
        cmd.Parameters.AddWithValue("$us", m?.UniqueStates ?? 0);
        cmd.Parameters.AddWithValue("$fp", m?.FrontierPeak ?? 0);
        cmd.Parameters.AddWithValue("$em", m?.ElapsedMs ?? 0);
        cmd.Parameters.AddWithValue("$ab", m?.AvgBranchingFactor ?? 0);
        cmd.Parameters.AddWithValue("$err", (object?)r.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return (long)cmd.ExecuteScalar()!;
    }

    // 磁带落盘后回填索引
    public void AttachTape(long runId, long tapeId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE solve_runs SET tape_id = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$t", tapeId);
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.ExecuteNonQuery();
    }

    // 取单条运行；不存在返回 null
    public SolveRunRow? Get(long runId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, game_id, level_ref, level_hash, adapter_hash, kernel_ver, contract_ver, run_config_hash,
              status, optimality, graph_complete, termination_reason, metrics_scope, solution_length,
              states_explored, unique_states, frontier_peak, elapsed_ms, avg_branching, error_message,
              tape_id, created_at
            FROM solve_runs WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", runId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new SolveRunRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetInt64(10) != 0,
            reader.GetString(11), reader.GetString(12), reader.GetInt32(13),
            reader.GetInt64(14), reader.GetInt64(15), reader.GetInt64(16), reader.GetInt64(17),
            reader.GetDouble(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetInt64(20),
            reader.GetString(21));
    }
}
