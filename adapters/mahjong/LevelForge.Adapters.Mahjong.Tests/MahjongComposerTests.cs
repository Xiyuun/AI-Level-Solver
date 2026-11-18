using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Executor;
using LevelForge.Kernel.Search;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.Mahjong.Tests;

// 意图制出题（多米诺铺装）：意图拼装可解、确定性、原生直通、非法意图拒绝
public sealed class MahjongComposerTests
{
    private static readonly MahjongFacade Facade = new();
    private static readonly MahjongAdapter Adapter = new();

    private static string Compose(int width, int height, int icePairs, int seed, string? levelNumber = "501")
    {
        var intent = $$"""{"width":{{width}},"height":{{height}},"icePairs":{{icePairs}}}""";
        var result = Facade.Compose(intent, levelNumber, new ComposeContext(levelNumber, 2, null, seed));
        Assert.True(result.LevelJson is not null, "errors=" + string.Join("|", result.Errors));
        return result.LevelJson!;
    }

    [Fact]
    public void 意图拼装_多米诺铺装可解且全预设()
    {
        var json = Compose(6, 6, 1, 11);
        Assert.Empty(Facade.FormatErrors(json));   // 全预设校验（chesses=chessCount、无生成期参数）
        var level = MahjongLevels.Load(json);
        Assert.Equal(36, level.ChessCount);
        Assert.Equal(36, level.Chesses.Count);
        // 求解器在环已复核；此处再独立求解一次
        var outcome = Facade.Solve(new SolveCommand(json, System.Text.Encoding.UTF8.GetBytes(json), "501", TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        Assert.Equal(18, outcome.Result.Solution.Count);   // 36 只 = 18 对
        Assert.NotNull(outcome.Tape);
    }

    [Fact]
    public void 意图拼装_同种子同布局_异种子异布局()
    {
        var a = Compose(6, 6, 0, 42);
        var a2 = Compose(6, 6, 0, 42);
        Assert.Equal(a, a2);   // 同种子确定性
        var b = Compose(6, 6, 0, 43);
        Assert.NotEqual(a, b);   // 异种子换布局
    }

    [Fact]
    public void 意图拼装_冰冻对自动解冻可清盘()
    {
        var json = Compose(6, 6, 1, 21);
        var outcome = Facade.Solve(new SolveCommand(json, System.Text.Encoding.UTF8.GetBytes(json), "502", TimeSpan.FromSeconds(20)));
        Assert.Equal(SolveStatus.Solved, outcome.Result.Status);
        var level = MahjongLevels.Load(json);
        Assert.Equal(2, level.Chesses.Count(c => c.SpecialType == MahjongSpecial.Ice && c.SpecialInfo > 0));   // 一对冰冻
    }

    [Fact]
    public void 原生直通_合法接受_非法拒绝()
    {
        var native = MahjongContent.RealLevel1Json;
        var ok = Facade.Compose(native, "1", new ComposeContext("1", 2, null, 1));
        Assert.True(ok.LevelJson is not null);   // 原生直通：合法即接受原文
        Assert.Equal(native, ok.LevelJson);

        var bad = """{"level":1,"difficulty":1,"chessCount":2,"mapWidth":6,"mapHeight":6,"chesses":[{"id":1,"x":0,"y":0,"type":1}],"obstacles":[]}""";
        Assert.Contains(Facade.Compose(bad, "1", new ComposeContext("1", 2, null, 1)).Errors, e => e.Contains("v1 支持子集之外"));
    }

    [Fact]
    public void 意图非法_给可操作错误()
    {
        var tooBig = Facade.Compose("""{"width":13,"height":6}""", "511", new ComposeContext("511", 2, null, 1));
        Assert.Contains(tooBig.Errors, e => e.Contains("width"));
        var badIce = Facade.Compose("""{"width":6,"height":6,"icePairs":5}""", "512", new ComposeContext("512", 2, null, 1));
        Assert.Contains(badIce.Errors, e => e.Contains("icePairs"));
        var odd = Facade.Compose("""{"width":7,"height":7}""", "513", new ComposeContext("513", 2, null, 1));
        Assert.Contains(odd.Errors, e => e.Contains("奇数"));
    }
}
