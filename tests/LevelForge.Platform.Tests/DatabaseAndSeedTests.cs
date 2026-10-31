using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;
using Microsoft.Data.Sqlite;

namespace LevelForge.Platform.Tests;

// 数据库与种子：建表幂等、种子注册、适配层指纹、资源条件哈希（数据库层直测，不走 HTTP）
public sealed class DatabaseAndSeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lf-db-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void 建表_幂等且版本标记()
    {
        var db = new AppDatabase(_dir);
        db.Initialize();
        db.Initialize();   // 重复初始化不抛（CREATE IF NOT EXISTS 幂等）
        Assert.Equal(12, db.TableCount());
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void 种子_注册玩具玩法与内置关()
    {
        var db = new AppDatabase(_dir);
        db.Initialize();
        GameSeeder.Seed(db, GameRegistry.Instance);

        var games = new GameStore(db).ListGames();
        var toy = Assert.Single(games);
        Assert.Equal("sokoban-toy", toy.Id);
        Assert.Equal(GameStatus.Production, toy.Status);       // 内置参考玩法：生产中
        Assert.False(toy.Gate2Applicable);                     // 无游戏本体：门2不适用（≠未通过）
        Assert.Equal(10, toy.LevelCount);
        Assert.StartsWith("sha256:", toy.AdapterHash);         // 适配层指纹已登记

        var levels = new GameStore(db).ListLevels("sokoban-toy");
        Assert.Equal(10, levels.Count);
        Assert.Equal("toy-001", levels[0].LevelId);
        Assert.Equal("toy-010", levels[^1].LevelId);
        Assert.All(levels, l => Assert.Equal(LevelSource.Builtin, l.Source));
        Assert.All(levels, l => Assert.StartsWith("sha256:", l.LevelHash));

        // 接入任务：内置玩法六步全部已完成
        using var conn = db.Open();
        using var tasks = conn.CreateCommand();
        tasks.CommandText = "SELECT COUNT(*) FROM onboarding_tasks WHERE game_id='sokoban-toy' AND status='done'";
        Assert.Equal(6L, (long)tasks.ExecuteScalar()!);
    }

    [Fact]
    public void 种子_幂等不重复()
    {
        var db = new AppDatabase(_dir);
        db.Initialize();
        GameSeeder.Seed(db, GameRegistry.Instance);
        GameSeeder.Seed(db, GameRegistry.Instance);   // 重跑种子：不产生重复行
        var games = new GameStore(db).ListGames();
        Assert.Single(games);
        Assert.Equal(10, games[0].LevelCount);
    }

    [Fact]
    public void 适配层指纹_格式稳定且区分程序集()
    {
        var adapterAsm = typeof(LevelForge.Adapters.SokobanToy.SokobanToyAdapter).Assembly;
        var kernelAsm = typeof(LevelForge.Kernel.Search.BfsSolver<,,>).Assembly;
        var first = AdapterFingerprint.Of(adapterAsm);
        var second = AdapterFingerprint.Of(adapterAsm);
        var kernel = AdapterFingerprint.Of(kernelAsm);
        Assert.StartsWith("sha256:", first);
        Assert.Equal(first, second);          // 同一程序集稳定
        Assert.NotEqual(first, kernel);       // 不同产物指纹不同（失效判定的基础）
    }

    [Fact]
    public void 资源条件哈希_全默认为空_声明预算则有值且区分()
    {
        var plain = new SolveCommand("{}", [], "x");
        Assert.Null(RunConfig.HashOf(plain));                       // 全无限额 = 无资源条件声明
        var nodes100 = RunConfig.HashOf(plain with { NodeBudget = 100 });
        var nodes200 = RunConfig.HashOf(plain with { NodeBudget = 200 });
        var fullEnum = RunConfig.HashOf(plain with { FullEnumeration = true });
        Assert.NotNull(nodes100);
        Assert.StartsWith("sha256:", nodes100);
        Assert.NotEqual(nodes100, nodes200);                        // 条件不同指纹不同
        Assert.NotNull(fullEnum);                                   // 完整枚举也是资源条件
    }
}
