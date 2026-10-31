using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 短语行：phrases 表行
public sealed record PhraseRow(long Id, string GameId, string Text, int SortOrder, string CreatedAt);

// 短语库存取：特殊要求快捷短语，按玩法分组（生成工作台设计决定 #1：无预置、用户自建、本地存储）
public sealed class PhraseStore
{
    private readonly AppDatabase _db;

    public PhraseStore(AppDatabase db)
    {
        _db = db;
    }

    // 某玩法的短语列表（按 sort_order、创建序）
    public IReadOnlyList<PhraseRow> List(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, game_id, text, sort_order, created_at FROM phrases WHERE game_id = $g ORDER BY sort_order, id";
        cmd.Parameters.AddWithValue("$g", gameId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<PhraseRow>();
        while (reader.Read())
        {
            rows.Add(new PhraseRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4)));
        }
        return rows;
    }

    // 新增短语（排在末尾），返回 (新行 id, 排序值)
    public (long Id, int SortOrder) Add(string gameId, string text)
    {
        using var conn = _db.Open();
        using var count = conn.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM phrases WHERE game_id = $g";
        count.Parameters.AddWithValue("$g", gameId);
        var sortOrder = (int)(long)count.ExecuteScalar()!;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO phrases (game_id, text, sort_order, created_at) VALUES ($g, $t, $s, $now);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$s", sortOrder);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        return ((long)cmd.ExecuteScalar()!, sortOrder);
    }

    // 删除；不存在返回 false
    public bool Delete(long id)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM phrases WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }
}
