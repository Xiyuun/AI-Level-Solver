using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 玩法行：games 表整行 + 关卡数（列表页一次查询带回）
public sealed record GameRow(
    string Id, string DisplayName, string Status,
    string AdapterVer, string AdapterHash, string KernelVer, string ContractVer,
    string LevelsRoot, bool Gate2Applicable,
    string VisualMappingJson, string MechanicsJson, string GameBrief, string FormatSpec,
    string RuleMatrix, string DifficultyWeightsJson,
    string RegisteredAt, string UpdatedAt, long LevelCount);

// 关卡行：levels 表整行
public sealed record LevelRow(
    long Id, string GameId, string LevelId, string LevelHash, string ContentJson,
    string Source, string CreatedAt);

// 玩法与关卡存取：games/levels 两张表的查询侧（写入侧在种子注册与安全落盘流程）
public sealed class GameStore
{
    private readonly AppDatabase _db;

    public GameStore(AppDatabase db)
    {
        _db = db;
    }

    // 玩法列表（含关卡数）
    public IReadOnlyList<GameRow> ListGames()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.*, COUNT(l.id) AS level_count
            FROM games g LEFT JOIN levels l ON l.game_id = g.id
            GROUP BY g.id ORDER BY g.registered_at, g.id
            """;
        return ReadRows(cmd);
    }

    // 单个玩法；不存在返回 null
    public GameRow? FindGame(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.*, COUNT(l.id) AS level_count
            FROM games g LEFT JOIN levels l ON l.game_id = g.id
            WHERE g.id = $id GROUP BY g.id
            """;
        cmd.Parameters.AddWithValue("$id", gameId);
        return ReadRows(cmd).FirstOrDefault();
    }

    // 玩法下全部关卡
    public IReadOnlyList<LevelRow> ListLevels(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, game_id, level_id, level_hash, content_json, source, created_at FROM levels WHERE game_id = $g ORDER BY level_id";
        cmd.Parameters.AddWithValue("$g", gameId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<LevelRow>();
        while (reader.Read())
        {
            rows.Add(new LevelRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        }
        return rows;
    }

    // 按编号取关卡内容；不存在返回 null
    public LevelRow? FindLevel(string gameId, string levelId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, game_id, level_id, level_hash, content_json, source, created_at FROM levels WHERE game_id = $g AND level_id = $l";
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new LevelRow(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6));
    }

    // 关卡登记 upsert：安全落盘成功后调用（同编号覆盖内容与哈希；来源随调用方）
    public void UpsertLevel(string gameId, string levelId, string levelHash, string contentJson, string source)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO levels (game_id, level_id, level_hash, content_json, source, created_at)
            VALUES ($g, $l, $h, $c, $s, $now)
            ON CONFLICT(game_id, level_id) DO UPDATE SET
              level_hash = excluded.level_hash, content_json = excluded.content_json, source = excluded.source
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$l", levelId);
        cmd.Parameters.AddWithValue("$h", levelHash);
        cmd.Parameters.AddWithValue("$c", contentJson);
        cmd.Parameters.AddWithValue("$s", source);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.ExecuteNonQuery();
    }

    #region 内部工具

    // 组装玩法行（列序与 SELECT g.* 一致）
    private static List<GameRow> ReadRows(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var rows = new List<GameRow>();
        while (reader.Read())
        {
            rows.Add(new GameRow(
                reader.GetString(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("display_name")),
                reader.GetString(reader.GetOrdinal("status")),
                reader.GetString(reader.GetOrdinal("adapter_ver")),
                reader.GetString(reader.GetOrdinal("adapter_hash")),
                reader.GetString(reader.GetOrdinal("kernel_ver")),
                reader.GetString(reader.GetOrdinal("contract_ver")),
                reader.GetString(reader.GetOrdinal("levels_root")),
                reader.GetInt64(reader.GetOrdinal("gate2_applicable")) != 0,
                reader.GetString(reader.GetOrdinal("visual_mapping_json")),
                reader.GetString(reader.GetOrdinal("mechanics_json")),
                reader.GetString(reader.GetOrdinal("game_brief")),
                reader.GetString(reader.GetOrdinal("format_spec")),
                reader.GetString(reader.GetOrdinal("rule_matrix")),
                reader.GetString(reader.GetOrdinal("difficulty_weights_json")),
                reader.GetString(reader.GetOrdinal("registered_at")),
                reader.GetString(reader.GetOrdinal("updated_at")),
                reader.GetInt64(reader.GetOrdinal("level_count"))));
        }
        return rows;
    }

    #endregion
}
