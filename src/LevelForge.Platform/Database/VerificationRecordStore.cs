using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 验证记录行：verification_records 表整行（验证门 §2.1——该门不依赖的指纹列为 null）
public sealed record VerificationRecordRow(
    long Id, string GameId, string Gate, string Verdict,
    string? LevelHash, string? AdapterHash, string? KernelVer, string? ContractVer,
    string? TestSuiteHash, string? RuleMatrixHash, string? GameBuildFingerprint,
    string? HarnessHash, int? SemanticSchemaVer, string? RunConfigHash,
    string? SamplesJson, string? SummaryJson, string ExecutedAt);

// 验证记录存取：三道门结论的唯一登记处；失效判定由消费方按指纹字段比对（§2.1 矩阵）
public sealed class VerificationRecordStore
{
    private readonly AppDatabase _db;

    public VerificationRecordStore(AppDatabase db)
    {
        _db = db;
    }

    // 登记一条验证结论（指纹字段可空：按该门实际依赖保存）
    public long Insert(string gameId, string gate, string verdict,
        string? levelHash = null, string? adapterHash = null, string? kernelVer = null,
        string? contractVer = null, string? testSuiteHash = null, string? ruleMatrixHash = null,
        string? gameBuildFingerprint = null, string? harnessHash = null, int? semanticSchemaVer = null,
        string? runConfigHash = null, string? samplesJson = null, string? summaryJson = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO verification_records (game_id, gate, verdict, level_hash, adapter_hash, kernel_ver,
              contract_ver, test_suite_hash, rule_matrix_hash, game_build_fingerprint, harness_hash,
              semantic_schema_ver, run_config_hash, samples_json, summary_json, executed_at)
            VALUES ($g, $gate, $v, $lh, $ah, $kv, $cv, $ts, $rm, $gbf, $hh, $ssv, $rch, $sp, $sm, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$gate", gate);
        cmd.Parameters.AddWithValue("$v", verdict);
        cmd.Parameters.AddWithValue("$lh", (object?)levelHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ah", (object?)adapterHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kv", (object?)kernelVer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cv", (object?)contractVer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts", (object?)testSuiteHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rm", (object?)ruleMatrixHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gbf", (object?)gameBuildFingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hh", (object?)harnessHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ssv", (object?)semanticSchemaVer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rch", (object?)runConfigHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sp", (object?)samplesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sm", (object?)summaryJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return (long)cmd.ExecuteScalar()!;
    }

    // 玩法全部记录（新→旧）；可按门过滤
    public IReadOnlyList<VerificationRecordRow> List(string gameId, string? gate = null)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + (gate is null
            ? " WHERE game_id = $g ORDER BY id DESC"
            : " WHERE game_id = $g AND gate = $gate ORDER BY id DESC");
        cmd.Parameters.AddWithValue("$g", gameId);
        if (gate is not null) cmd.Parameters.AddWithValue("$gate", gate);
        using var reader = cmd.ExecuteReader();
        var rows = new List<VerificationRecordRow>();
        while (reader.Read()) rows.Add(Map(reader));
        return rows;
    }

    #region 内部工具

    // 统一列序（与 Map 对齐）
    private const string SelectSql = """
        SELECT id, game_id, gate, verdict, level_hash, adapter_hash, kernel_ver, contract_ver,
          test_suite_hash, rule_matrix_hash, game_build_fingerprint, harness_hash, semantic_schema_ver,
          run_config_hash, samples_json, summary_json, executed_at
        FROM verification_records
        """;

    // 行映射
    private static VerificationRecordRow Map(SqliteDataReader reader)
    {
        return new VerificationRecordRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            Nl(reader, 4), Nl(reader, 5), Nl(reader, 6), Nl(reader, 7),
            Nl(reader, 8), Nl(reader, 9), Nl(reader, 10), Nl(reader, 11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12),
            Nl(reader, 13), Nl(reader, 14), Nl(reader, 15),
            reader.GetString(16));
    }

    // 可空字符串列读取
    private static string? Nl(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    #endregion
}
