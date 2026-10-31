using System.Text;
using LevelForge.Kernel.Hashing;
using LevelForge.Platform.Facades;
using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 种子注册：启动时把静态注册表落库（幂等）
// 权威列（版本/指纹/接入产物）每次启动对齐注册表；用户可编辑列（status 等）冲突时不覆盖
public static class GameSeeder
{
    // 六步向导固定步名（接入向导：登记→资格评估→适配层生成→人工审核→对拍与回归→上线注册）
    private static readonly (int Step, string Title)[] WizardSteps =
    {
        (1, "登记玩法"), (2, "资格评估"), (3, "适配层生成"),
        (4, "人工审核"), (5, "对拍与回归"), (6, "上线注册")
    };

    // 全量种子：玩法行 + 内置关 + 默认设置 + 接入任务
    public static void Seed(AppDatabase db, GameRegistry registry)
    {
        var defaultGame = registry.Facades.First().Registration.GameId;   // 首个注册玩法作默认活跃玩法
        foreach (var facade in registry.Facades)
        {
            UpsertGame(db, facade.Registration);
            SeedLevels(db, facade);
            SeedOnboardingTasks(db, facade.Registration);
        }
        SeedDefaultSettings(db, defaultGame);
    }

    #region 玩法行

    // 注册表权威列 upsert；status 仅首插写入（接入流程可改它：onboarding→trial→production）
    private static void UpsertGame(AppDatabase db, GameRegistration r)
    {
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO games (id, display_name, status, adapter_ver, adapter_hash, kernel_ver, contract_ver,
              levels_root, gate2_applicable, visual_mapping_json, mechanics_json, game_brief, format_spec,
              rule_matrix, difficulty_weights_json, registered_at, updated_at)
            VALUES ($id, $name, $status, $av, $ah, $kv, $cv, $root, $g2, $vm, $mx, $brief, $fmt, $rm, $dw, $now, $now)
            ON CONFLICT(id) DO UPDATE SET
              display_name = excluded.display_name, adapter_ver = excluded.adapter_ver,
              adapter_hash = excluded.adapter_hash, kernel_ver = excluded.kernel_ver,
              contract_ver = excluded.contract_ver, levels_root = excluded.levels_root,
              gate2_applicable = excluded.gate2_applicable, visual_mapping_json = excluded.visual_mapping_json,
              mechanics_json = excluded.mechanics_json, game_brief = excluded.game_brief,
              format_spec = excluded.format_spec, rule_matrix = excluded.rule_matrix,
              difficulty_weights_json = excluded.difficulty_weights_json, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$id", r.GameId);
        cmd.Parameters.AddWithValue("$name", r.DisplayName);
        cmd.Parameters.AddWithValue("$status", r.Status);
        cmd.Parameters.AddWithValue("$av", r.AdapterVersion);
        cmd.Parameters.AddWithValue("$ah", r.AdapterHash);
        cmd.Parameters.AddWithValue("$kv", r.KernelVersion);
        cmd.Parameters.AddWithValue("$cv", r.ContractVersion);
        cmd.Parameters.AddWithValue("$root", r.LevelsRootDir);
        cmd.Parameters.AddWithValue("$g2", r.Gate2Applicable ? 1 : 0);
        cmd.Parameters.AddWithValue("$vm", r.VisualMappingJson);
        cmd.Parameters.AddWithValue("$mx", r.MechanicsJson);
        cmd.Parameters.AddWithValue("$brief", r.GameBrief);
        cmd.Parameters.AddWithValue("$fmt", r.FormatSpec);
        cmd.Parameters.AddWithValue("$rm", r.RuleMatrix);
        cmd.Parameters.AddWithValue("$dw", r.DifficultyWeightsJson);
        cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region 内置关卡

    // 内置关 upsert：编号相同覆盖内容与哈希（内置关内容以注册表为准）；来源恒为 builtin
    private static void SeedLevels(AppDatabase db, IGameFacade facade)
    {
        var gameId = facade.Registration.GameId;
        foreach (var json in facade.BuiltinLevels)
        {
            var levelId = facade.TryGetLevelId(json);
            if (levelId is null) continue;   // 取不到编号的内置关跳过（适配层内容缺陷，由其自身测试暴露）
            var hash = TapeHashes.LevelHash(Encoding.UTF8.GetBytes(json));
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO levels (game_id, level_id, level_hash, content_json, source, created_at)
                VALUES ($g, $l, $h, $c, 'builtin', $now)
                ON CONFLICT(game_id, level_id) DO UPDATE SET
                  level_hash = excluded.level_hash, content_json = excluded.content_json
                """;
            cmd.Parameters.AddWithValue("$g", gameId);
            cmd.Parameters.AddWithValue("$l", levelId);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$c", json);
            cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
            cmd.ExecuteNonQuery();
        }
    }

    #endregion

    #region 默认设置与接入任务

    // 默认设置：仅缺失时写入（用户改过的值不动）；值以 JSON 字面量存储
    private static void SeedDefaultSettings(AppDatabase db, string defaultGame)
    {
        var defaults = new (string Key, string ValueJson)[]
        {
            ("llm.baseUrl", "\"\""),                  // LLM API 地址（设置页配置，实例自带）
            ("llm.apiKey", "\"\""),                   // key 明文本地存储（冻结决策：本地单机无账号）
            ("llm.model", "\"\""),
            ("gen.candidateLimit", "3"),              // 重生上限默认 3（生成端出题分析 §3.3）
            ("gen.solveTimeoutMs", "30000"),          // 单候选求解超时默认 30s
            ("ui.activeGame", $"\"{defaultGame}\""),
            ("ui.theme", "\"light\"")
        };
        foreach (var (key, valueJson) in defaults)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO settings (key, value_json, updated_at) VALUES ($k, $v, $now)";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", valueJson);
            cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
            cmd.ExecuteNonQuery();
        }
    }

    // 接入任务：六步行 INSERT OR IGNORE；已投产玩法直接种子为已完成
    private static void SeedOnboardingTasks(AppDatabase db, GameRegistration r)
    {
        var status = r.Status == GameStatus.Production ? "done" : "todo";
        var note = r.Status == GameStatus.Production ? "\"内置玩法：随平台发布，向导已完成\"" : "null";
        foreach (var (step, title) in WizardSteps)
        {
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO onboarding_tasks (game_id, step, title, status, detail_json, updated_at) VALUES ($g, $s, $t, $st, $d, $now)";
            cmd.Parameters.AddWithValue("$g", r.GameId);
            cmd.Parameters.AddWithValue("$s", step);
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$d", status == "done" ? note : (object?)DBNull.Value);
            cmd.Parameters.AddWithValue("$now", AppDatabase.UtcNowIso);
            cmd.ExecuteNonQuery();
        }
    }

    #endregion
}
