using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 接入任务行：onboarding_tasks 表整行（六步向导每步的状态与存档）
public sealed record OnboardingTaskRow(
    long Id, string GameId, int Step, string Title, string Status, string? DetailJson, string UpdatedAt);

// 接入任务存取：六步向导状态机（todo/doing/done/blocked；detail 存报告/清单等文本存档）
public sealed class OnboardingStore
{
    private readonly AppDatabase _db;

    public OnboardingStore(AppDatabase db)
    {
        _db = db;
    }

    // 玩法的六步任务（按步序）
    public IReadOnlyList<OnboardingTaskRow> List(string gameId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, game_id, step, title, status, detail_json, updated_at
            FROM onboarding_tasks WHERE game_id = $g ORDER BY step
            """;
        cmd.Parameters.AddWithValue("$g", gameId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<OnboardingTaskRow>();
        while (reader.Read())
        {
            rows.Add(new OnboardingTaskRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6)));
        }
        return rows;
    }

    // 更新某步状态与存档；步号不存在返回 false
    public bool UpdateStep(string gameId, int step, string status, string? detail)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE onboarding_tasks
            SET status = $s, detail_json = COALESCE($d, detail_json), updated_at = $now
            WHERE game_id = $g AND step = $step
            """;
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$d", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$g", gameId);
        cmd.Parameters.AddWithValue("$step", step);
        return cmd.ExecuteNonQuery() > 0;
    }

    // 更新玩法状态（onboarding→trial→production；games.status）
    public bool UpdateGameStatus(string gameId, string status)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE games SET status = $s, updated_at = $now WHERE id = $g";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$g", gameId);
        return cmd.ExecuteNonQuery() > 0;
    }

    // 写入门2驱动配置（JSON）与适用标记
    public bool UpdateGate2Driver(string gameId, string driverJson, bool applicable)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE games SET gate2_driver_json = $d, gate2_applicable = $a, updated_at = $now
            WHERE id = $g
            """;
        cmd.Parameters.AddWithValue("$d", driverJson);
        cmd.Parameters.AddWithValue("$a", applicable ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.Parameters.AddWithValue("$g", gameId);
        return cmd.ExecuteNonQuery() > 0;
    }
}
