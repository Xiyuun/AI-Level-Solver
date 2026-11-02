using LevelForge.Adapters.SlidePig;
using Xunit;

namespace LevelForge.Adapters.SlidePig.Tests;

// 规则引擎语义测试：每条规则矩阵行至少一个用例（网格模型口径）
public class SlidePigRulesTests
{
    private static readonly SlidePigAdapter Adapter = new();

    // 构造测试关卡的便捷方法（默认 6×6）
    private static SlidePigLevel Level(params (int Id, string Facing, string Type, string Info, (int X, int Y)[] Cells)[] pigs)
    {
        return new SlidePigLevel(1, "t", 6, 6,
            pigs.Select(p => new SlidePigLevelPig(p.Id, p.Facing, p.Type, p.Info, p.Cells)).ToList());
    }

    #region 滑行 / 阻挡 / 离场

    [Fact]
    public void 滑行_被挡停在贴靠格()
    {
        // A(0)朝右占 x=0..1 行1；B(1)竖占 x=4 行1..2：A 前移两格后前缘目标 (4,1) 被占
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Normal", "", [(0, 1), (1, 1)]),
            (1, "Up", "Normal", "", [(4, 1), (4, 2)])));

        var next = Adapter.ApplyMove(state, new SlidePigMove(0));

        var a = next.Find(0)!;
        Assert.Equal([(2, 1), (3, 1)], a.Cells);                 // 停在 B 贴靠格
        Assert.Equal(2, next.Pigs.Count);                        // 无离场
    }

    [Fact]
    public void 滑行_滑出边界整猪离场并胜利()
    {
        var state = Adapter.LoadLevel(Level((0, "Right", "Normal", "", [(3, 3), (4, 3)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));
        Assert.Empty(next.Pigs);                                 // 整猪离场
        Assert.True(Adapter.IsGoal(next));                       // 清场即胜
    }

    [Fact]
    public void 多格猪_整块平移()
    {
        // 长猪 1×3 朝右：三格同步前移
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Long", "", [(0, 2), (1, 2), (2, 2)]),
            (1, "Up", "Normal", "", [(4, 2), (4, 3)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));
        Assert.Equal([(1, 2), (2, 2), (3, 2)], next.Find(0)!.Cells);
    }

    [Fact]
    public void 非法操作_被挡与铁笼与未解锁均抛异常()
    {
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Normal", "", [(0, 1), (1, 1)]),
            (1, "Left", "Normal", "", [(2, 1), (3, 1)])));       // 0 的前缘目标格 (2,1) 被 1 直接占住
        Assert.Throws<InvalidOperationException>(() => Adapter.ApplyMove(state, new SlidePigMove(0)));

        var cageState = Adapter.LoadLevel(Level(
            (1, "Up", "Cage", "3", [(2, 0), (2, 1)]),
            (2, "Right", "Normal", "", [(0, 0), (0, 1)])));
        Assert.Throws<InvalidOperationException>(() => Adapter.ApplyMove(cageState, new SlidePigMove(1)));   // 铁笼不可点

        var lockState = Adapter.LoadLevel(Level(
            (0, "Right", "Lock", "2", [(2, 2), (1, 2)]),
            (1, "Up", "Normal", "", [(4, 4), (4, 5)])));
        Assert.Throws<InvalidOperationException>(() => Adapter.ApplyMove(lockState, new SlidePigMove(0)));   // 未解锁不可点
    }

    #endregion

    #region 爆炸猪

    [Fact]
    public void 爆炸猪_碰撞双方各扣自身计数()
    {
        // A(Boom,5) 撞 B(Boom,2)：双方各扣 1
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Boom", "5", [(0, 1), (1, 1)]),
            (1, "Up", "Boom", "2", [(3, 1), (3, 2)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));
        Assert.Equal(4, next.Find(0)!.Counter);
        Assert.Equal(1, next.Find(1)!.Counter);
        Assert.False(next.Failed);
    }

    [Fact]
    public void 爆炸猪_计数归零失败终态()
    {
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Boom", "1", [(0, 1), (1, 1)]),         // A 计数 1：撞一次即归零
            (1, "Up", "Normal", "", [(3, 1), (3, 2)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));
        Assert.True(next.Failed);                                // 失败终态
        Assert.Empty(Adapter.GetLegalMoves(next));               // 无后继
        Assert.False(Adapter.IsGoal(next));                      // 失败不算胜
    }

    #endregion

    #region 锁猪与铁笼

    [Fact]
    public void 锁猪_他猪离场扣数_归零可动并胜利()
    {
        // Lock(1) + 上路畅通的 Normal：B 先离场解锁 A，A 再右移离场
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Lock", "1", [(2, 2), (1, 2)]),
            (1, "Up", "Normal", "", [(4, 4), (4, 5)])));
        // 初始合法操作不含锁猪
        Assert.DoesNotContain(Adapter.GetLegalMoves(state), m => m.PigId == 0);

        var afterB = Adapter.ApplyMove(state, new SlidePigMove(1));
        Assert.Empty(afterB.Pigs.Where(p => p.Id == 1));         // B 离场
        Assert.Equal(0, afterB.Find(0)!.Counter);                // A 解锁
        Assert.Contains(Adapter.GetLegalMoves(afterB), m => m.PigId == 0);

        var afterA = Adapter.ApplyMove(afterB, new SlidePigMove(0));
        Assert.True(Adapter.IsGoal(afterA));                     // 两击清场
    }

    [Fact]
    public void 铁笼_离场扣数自删_且不级联扣其他铁笼()
    {
        // C1(1) 与 C2(2)：B 离场 → C1 归零自删、C2 → 1；若存在级联，C2 也会被删
        var state = Adapter.LoadLevel(Level(
            (1, "Up", "Cage", "1", [(2, 0), (2, 1)]),
            (2, "Up", "Cage", "2", [(3, 0), (3, 1)]),
            (0, "Up", "Normal", "", [(5, 4), (5, 5)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));

        Assert.Null(next.Find(1));                               // C1 自删
        var c2 = next.Find(2)!;
        Assert.Equal(1, c2.Counter);                             // C2 仅扣 1（无级联）
    }

    [Fact]
    public void 铁笼_自删后释放通路()
    {
        // 铁笼挡路：A 无法通过；B 离场解笼后 A 可滑至边界离场
        var state = Adapter.LoadLevel(Level(
            (1, "Up", "Cage", "1", [(2, 1), (2, 2)]),
            (2, "Up", "Normal", "", [(5, 4), (5, 5)]),
            (0, "Right", "Normal", "", [(0, 1), (0, 2)])));
        var unblocked = Adapter.ApplyMove(state, new SlidePigMove(2));   // B 离场，笼自删
        Assert.Null(unblocked.Find(1));
        // A 现在可以一路向右滑出（y=1..2 行畅通）
        var cleared = Adapter.ApplyMove(unblocked, new SlidePigMove(0));
        Assert.True(Adapter.IsGoal(cleared));
    }

    #endregion

    #region 弹簧猪

    [Fact]
    public void 弹簧猪_撞猪弹回原位_回调照常()
    {
        // Spring 撞 Boom(2)：Spring 回原位，Boom 扣 1
        var state = Adapter.LoadLevel(Level(
            (0, "Right", "Spring", "", [(0, 1), (1, 1)]),
            (1, "Left", "Boom", "2", [(3, 1), (4, 1)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));

        Assert.Equal([(0, 1), (1, 1)], next.Find(0)!.Cells);     // 弹回原位
        Assert.Equal(1, next.Find(1)!.Counter);                  // Boom 被撞扣 1
    }

    [Fact]
    public void 弹簧猪_无阻挡正常离场()
    {
        var state = Adapter.LoadLevel(Level((0, "Right", "Spring", "", [(3, 3), (4, 3)])));
        var next = Adapter.ApplyMove(state, new SlidePigMove(0));
        Assert.Empty(next.Pigs);
        Assert.True(Adapter.IsGoal(next));
    }

    #endregion

    #region 状态键与解析

    [Fact]
    public void 状态键_覆盖位置计数失败维度()
    {
        var baseState = Adapter.LoadLevel(Level(
            (0, "Right", "Boom", "5", [(0, 1), (1, 1)]),
            (1, "Up", "Normal", "", [(3, 3), (3, 4)])));
        // 位置维度：同猪换位
        var moved = baseState with { };
        var posChanged = Adapter.ApplyMove(baseState, new SlidePigMove(0));
        Assert.NotEqual(Adapter.StateKey(baseState), Adapter.StateKey(posChanged));
        // 计数维度：仅改 Boom 计数
        var counterChanged = baseState with
        {
            Pigs = baseState.Pigs.Select(p => p.Id == 0 ? p with { Counter = 4 } : p).ToList()
        };
        Assert.NotEqual(Adapter.StateKey(baseState), Adapter.StateKey(counterChanged));
        // 失败维度
        var failedState = baseState with { Failed = true };
        Assert.NotEqual(Adapter.StateKey(baseState), Adapter.StateKey(failedState));
    }

    [Fact]
    public void 计数解析_特殊值走默认()
    {
        // 空串 Boom→3、Lock→10；正整数照用
        Assert.Equal(3, SlidePigRules.ParseBoomCount(""));
        Assert.Equal(7, SlidePigRules.ParseBoomCount(" 7 "));
        Assert.Equal(10, SlidePigRules.ParseUnlockCount(""));
        Assert.Equal(0, SlidePigRules.ParseUnlockCount("0"));
        Assert.Equal(4, SlidePigRules.ParseUnlockCount("4"));
    }

    [Fact]
    public void 校验器_重叠越界与子集外类型报错()
    {
        var (l1, e1) = SlidePigValidator.ValidateJson("""{"levelId":1,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":1,"pigs":[{"id":0,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":0,"y":1},{"x":1,"y":1}]}]}""");
        Assert.Empty(e1);
        Assert.Equal(1, l1!.LevelId);

        // 脚印重叠
        var (_, e2) = SlidePigValidator.ValidateJson("""{"levelId":2,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":2,"pigs":[{"id":0,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":1,"y":1}]},{"id":1,"facing":"Up","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":1,"y":1}]}]}""");
        Assert.Contains(e2, e => e.Contains("重叠"));

        // 越界
        var (_, e3) = SlidePigValidator.ValidateJson("""{"levelId":3,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":1,"pigs":[{"id":0,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":9,"y":1},{"x":1,"y":1}]}]}""");
        Assert.Contains(e3, e => e.Contains("越界"));

        // v1 子集外类型（Duck）
        var (_, e4) = SlidePigValidator.ValidateJson("""{"levelId":4,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":1,"pigs":[{"id":0,"facing":"Up","pigType":"Duck","specialInfo":"","occupiedCells":[{"x":1,"y":1},{"x":1,"y":2}]}]}""");
        Assert.Contains(e4, e => e.Contains("不在 v1 支持子集"));
    }

    #endregion
}
