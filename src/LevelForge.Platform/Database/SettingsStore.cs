using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 设置存取：settings 键值表的类型化封装（表现层_设置；值一律 JSON 存储）
// 键空间约定：llm.baseUrl / llm.apiKey / llm.model、gen.candidateLimit / gen.solveTimeoutMs、ui.activeGame / ui.theme
public sealed class SettingsStore
{
    private readonly AppDatabase _db;

    // 序列化口径：与平台 API 一致（camelCase 不影响原始值类型，枚举小写字符串）
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public SettingsStore(AppDatabase db)
    {
        _db = db;
    }

    // 读取：缺失或损坏返回 fallback（本地工具容错优先，一条坏记录不挂掉页面）
    public T Get<T>(string key, T fallback)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_json FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        var raw = cmd.ExecuteScalar() as string;
        if (raw is null) return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOpts) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    // 写入：值序列化为 JSON 存储（upsert）
    public void Set(string key, object? value)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value_json, updated_at) VALUES ($k, $v, $now)
            ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", JsonSerializer.Serialize(value, JsonOpts));
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.ExecuteNonQuery();
    }

    // 全量：key → 解析后的 JSON 值（设置页整表回显）；坏值原样跳过不抛
    public IReadOnlyDictionary<string, JsonNode?> All()
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value_json FROM settings ORDER BY key";
        using var reader = cmd.ExecuteReader();
        var map = new Dictionary<string, JsonNode?>();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            try
            {
                map[key] = JsonNode.Parse(reader.GetString(1));
            }
            catch (JsonException)
            {
                map[key] = null;   // 坏值如实暴露为 null，不吞键
            }
        }
        return map;
    }
}
