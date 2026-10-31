using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Database;

// 平台数据库：SQLite 文件 + 首次启动自动建表（单人维护约束：本地存储、无账号、零外部服务）
// 连接策略：每次操作独立开连接（WAL + busy_timeout + 外键），单进程本地工具足够；写并发由 SQLite 串行化
public sealed class AppDatabase
{
    public string DataDir { get; }             // 数据根目录：db 文件、tapes/、后续关卡包与报告都落这里
    public string DbPath { get; }              // SQLite 文件路径

    public AppDatabase(string dataDir)
    {
        DataDir = dataDir;
        DbPath = Path.Combine(dataDir, "levelforge.db");
        Directory.CreateDirectory(dataDir);
    }

    // 统一时间戳：ISO-8601 UTC（落库列全用这个口径）
    public static string UtcNowIso => DateTime.UtcNow.ToString("o");

    // 打开连接并设 PRAGMA；调用方 using 释放
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        // WAL 提高读写并发；busy_timeout 避免偶发锁冲突直接抛错；外键约束开启
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            PRAGMA foreign_keys=ON;
            """;
        cmd.ExecuteNonQuery();
        return conn;
    }

    // 建表（幂等）+ 版本标记；首次启动自动完成，无需手工迁移
    public void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = string.Join("\n", SchemaDdl);
        cmd.ExecuteNonQuery();
        using var version = conn.CreateCommand();
        // user_version=1：当前结构版本；后续演进在此分支迁移
        version.CommandText = "PRAGMA user_version=1";
        version.ExecuteNonQuery();
    }

    // 表数量（测试用：全部表已建立）
    public long TableCount()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        return (long)cmd.ExecuteScalar()!;
    }

    #region 表结构（13 张表，验证门 §2.1 失效矩阵 + 验证门 §5.1 LevelBaseline 字段）

    private static readonly string[] SchemaDdl =
    [
        // 玩法注册表：一行一个玩法（静态注册的运行时映射）
        """
        CREATE TABLE IF NOT EXISTS games (
          id TEXT PRIMARY KEY,
          display_name TEXT NOT NULL,
          status TEXT NOT NULL,
          adapter_ver TEXT NOT NULL,
          adapter_hash TEXT NOT NULL,
          kernel_ver TEXT NOT NULL,
          contract_ver TEXT NOT NULL,
          levels_root TEXT NOT NULL,
          gate2_applicable INTEGER NOT NULL,
          visual_mapping_json TEXT NOT NULL,
          mechanics_json TEXT NOT NULL,
          game_brief TEXT NOT NULL,
          format_spec TEXT NOT NULL,
          rule_matrix TEXT NOT NULL,
          difficulty_weights_json TEXT NOT NULL,
          gate2_driver_json TEXT,
          registered_at TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );
        """,
        // 关卡表：原生内容直通存储（零转换），levelHash 绑定具体字节
        """
        CREATE TABLE IF NOT EXISTS levels (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          level_id TEXT NOT NULL,
          level_hash TEXT NOT NULL,
          content_json TEXT NOT NULL,
          source TEXT NOT NULL,
          created_at TEXT NOT NULL,
          UNIQUE(game_id, level_id)
        );
        CREATE INDEX IF NOT EXISTS ix_levels_hash ON levels(game_id, level_hash);
        """,
        // 求解运行：五维结论 + metrics + 指纹（tape_id 无外键：solve_runs 先落、tapes 后建，避免循环依赖）
        """
        CREATE TABLE IF NOT EXISTS solve_runs (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          level_ref TEXT NOT NULL,
          level_hash TEXT NOT NULL,
          adapter_hash TEXT NOT NULL,
          kernel_ver TEXT NOT NULL,
          contract_ver TEXT NOT NULL,
          run_config_hash TEXT,
          status TEXT NOT NULL,
          optimality TEXT NOT NULL,
          graph_complete INTEGER NOT NULL,
          termination_reason TEXT NOT NULL,
          metrics_scope TEXT NOT NULL,
          solution_length INTEGER NOT NULL,
          states_explored INTEGER NOT NULL,
          unique_states INTEGER NOT NULL,
          frontier_peak INTEGER NOT NULL,
          elapsed_ms INTEGER NOT NULL,
          avg_branching REAL NOT NULL,
          error_message TEXT,
          tape_id INTEGER,
          created_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_solve_runs_level ON solve_runs(game_id, level_hash);
        """,
        // 磁带索引：文件是唯一持久化形态（data/tapes/），表只存索引与摘要
        """
        CREATE TABLE IF NOT EXISTS tapes (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          run_id INTEGER NOT NULL REFERENCES solve_runs(id),
          file_path TEXT NOT NULL,
          trajectory_hash TEXT NOT NULL,
          artifact_hash TEXT NOT NULL,
          created_at TEXT NOT NULL
        );
        """,
        // 验证记录：按产物指纹失效（验证门 §2.1）——该门不依赖的指纹列存 NULL
        """
        CREATE TABLE IF NOT EXISTS verification_records (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          gate TEXT NOT NULL,
          verdict TEXT NOT NULL,
          level_hash TEXT,
          adapter_hash TEXT,
          kernel_ver TEXT,
          contract_ver TEXT,
          test_suite_hash TEXT,
          rule_matrix_hash TEXT,
          game_build_fingerprint TEXT,
          harness_hash TEXT,
          semantic_schema_ver INTEGER,
          run_config_hash TEXT,
          samples_json TEXT,
          summary_json TEXT,
          executed_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_verif_game_gate ON verification_records(game_id, gate);
        """,
        // 基准：LevelBaseline 全字段（验证门 §5.1）；按版本追加保留旧记录，不静默覆盖
        """
        CREATE TABLE IF NOT EXISTS baselines (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          level_id TEXT NOT NULL,
          level_hash TEXT NOT NULL,
          adapter_ver TEXT NOT NULL,
          adapter_hash TEXT NOT NULL,
          kernel_ver TEXT NOT NULL,
          contract_ver TEXT NOT NULL,
          game_build_fingerprint TEXT,
          harness_hash TEXT,
          run_config_hash TEXT,
          status TEXT NOT NULL,
          optimality TEXT NOT NULL,
          graph_complete INTEGER NOT NULL,
          termination_reason TEXT NOT NULL,
          solution_length INTEGER NOT NULL,
          states_explored INTEGER NOT NULL,
          trajectory_hash TEXT NOT NULL,
          artifact_hash TEXT,
          execution_confirmation_ref TEXT,
          approved_by TEXT,
          approved_at TEXT,
          created_at TEXT NOT NULL,
          UNIQUE(game_id, level_id, adapter_hash, kernel_ver)
        );
        """,
        // 生成运行：一次"出题→判卷→重生"循环的容器（生成端出题分析 §3）
        """
        CREATE TABLE IF NOT EXISTS generation_runs (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          prompt_version TEXT NOT NULL,
          target_difficulty INTEGER NOT NULL,
          level_number TEXT,
          special_requirements TEXT,
          candidate_limit INTEGER NOT NULL,
          solve_timeout_ms INTEGER NOT NULL,
          status TEXT NOT NULL,
          failure_summary TEXT,
          adopted_candidate_id INTEGER,
          started_at TEXT NOT NULL,
          finished_at TEXT
        );
        """,
        // 生成候选：逐次尝试的原生内容 + 失败分类（观测落库，供失败原因 Top 榜）
        // content_json 可空：api_error（调用失败无内容）与"输出中无 JSON"的 format_invalid 候选没有内容
        """
        CREATE TABLE IF NOT EXISTS generation_candidates (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          run_id INTEGER NOT NULL REFERENCES generation_runs(id),
          attempt_index INTEGER NOT NULL,
          content_json TEXT,
          failure_class TEXT NOT NULL,
          failure_detail TEXT,
          difficulty_band INTEGER,
          difficulty_score REAL,
          solve_run_id INTEGER REFERENCES solve_runs(id),
          elapsed_ms INTEGER NOT NULL,
          created_at TEXT NOT NULL
        );
        """,
        // 执行确认：当前文件在游戏实际执行的状态机（表现层_生成工作台 §4.2）
        // 五态：未执行 pending / 执行中 running / 通过 passed / 失败 failed / 已失效 invalidated
        """
        CREATE TABLE IF NOT EXISTS execution_confirmations (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL REFERENCES games(id),
          level_id TEXT NOT NULL,
          level_hash TEXT NOT NULL,
          state TEXT NOT NULL,
          mode TEXT NOT NULL,
          game_build_fingerprint TEXT,
          report_ref TEXT,
          outcome TEXT,
          confirmed_by TEXT,
          confirmed_at TEXT,
          note TEXT,
          solve_run_id INTEGER REFERENCES solve_runs(id),
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_confirm_level ON execution_confirmations(game_id, level_id);
        """,
        // 设置：键值表，值 JSON 存储（LLM 地址/key 明文本地、生成默认值、界面状态）
        """
        CREATE TABLE IF NOT EXISTS settings (
          key TEXT PRIMARY KEY,
          value_json TEXT NOT NULL,
          updated_at TEXT NOT NULL
        );
        """,
        // 短语库：特殊要求快捷短语，按玩法分组（生成工作台设计决定 #1：无预置、用户自建）
        """
        CREATE TABLE IF NOT EXISTS phrases (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL,
          text TEXT NOT NULL,
          sort_order INTEGER NOT NULL DEFAULT 0,
          created_at TEXT NOT NULL
        );
        """,
        // 接入任务：六步向导的每步状态（第14步向导页消费；内置玩法直接种子为已完成）
        """
        CREATE TABLE IF NOT EXISTS onboarding_tasks (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          game_id TEXT NOT NULL,
          step INTEGER NOT NULL,
          title TEXT NOT NULL,
          status TEXT NOT NULL,
          detail_json TEXT,
          updated_at TEXT NOT NULL,
          UNIQUE(game_id, step)
        );
        """
    ];

    #endregion
}
