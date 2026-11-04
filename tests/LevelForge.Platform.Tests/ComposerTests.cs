using LevelForge.Platform.Facades;
using Xunit;

namespace LevelForge.Platform.Tests;

// 坐标制出题（生成端 §8.1）：模型只给设计意图，格式由代码拼装
// 防线要点：拼装必须产出能过原生校验器的关卡；错误必须精确可回喂；原生合法输出必须直通不误杀
public sealed class ComposerTests
{
    private static readonly SokobanToyFacade Facade = new();

    [Fact]
    public void 坐标制_拼装出通过原生校验的网格()
    {
        var coord = """{"width":6,"height":4,"player":{"x":1,"y":1},"boxes":[{"x":2,"y":1}],"goals":[{"x":4,"y":1}]}""";

        var result = Facade.Compose(coord, "3");

        Assert.Empty(result.Errors);
        Assert.NotNull(result.LevelJson);
        // 拼装产物必须过原生校验器——否则等于中间层自说自话
        Assert.Empty(Facade.FormatErrors(result.LevelJson!));
        var level = System.Text.Json.Nodes.JsonNode.Parse(result.LevelJson!)!;
        var grid = level["grid"]!.AsArray().Select(g => g!.GetValue<string>()).ToArray();
        Assert.Equal(4, grid.Length);
        Assert.All(grid, row => Assert.Equal(6, row.Length));      // 矩形由代码保证
        Assert.All(grid, row => Assert.Equal('#', row[0]));        // 左边界是墙
        Assert.Equal('#', grid[0][0]);
        Assert.Equal('#', grid[3][5]);
        Assert.Equal("toy-003", level["id"]!.GetValue<string>());  // 编号按内置命名补零
    }

    [Fact]
    public void 箱子与目标数不一致_报精确错误而非笼统失败()
    {
        var coord = """{"width":6,"height":4,"player":{"x":1,"y":1},"boxes":[{"x":2,"y":1}],"goals":[{"x":3,"y":1},{"x":4,"y":1}]}""";

        var result = Facade.Compose(coord, null);

        Assert.Null(result.LevelJson);
        Assert.Contains(result.Errors, e => e.Contains("箱子数 1") && e.Contains("目标数 2"));
    }

    [Fact]
    public void 坐标越界_给出可用范围()
    {
        var coord = """{"width":5,"height":5,"player":{"x":0,"y":1},"boxes":[{"x":2,"y":1}],"goals":[{"x":3,"y":1}]}""";

        var result = Facade.Compose(coord, null);

        Assert.Null(result.LevelJson);
        // 最外圈留给墙：x=0 不可用，应提示 1..3 范围
        Assert.Contains(result.Errors, e => e.Contains("player") && e.Contains("越界"));
    }

    [Fact]
    public void 原生合法grid_直通接受不改写()
    {
        // 模型照 few-shot 学出原生格式时不该被误杀（合法输入原样接受）
        var native = """{"id":"gen-101","grid":["######","#@$ .#","#    #","######"]}""";

        var result = Facade.Compose(native, null);

        Assert.Empty(result.Errors);
        Assert.Equal(native, result.LevelJson);
    }

    [Fact]
    public void 原生非矩形grid_照实报错不静默修补()
    {
        // 这是历史真实失败样本：模型数不对列宽。必须报错，不能"帮它补齐"
        var ragged = """{"id":"toy-011","grid":["#########","#@$ .#","#    #","#########"]}""";

        var result = Facade.Compose(ragged, null);

        Assert.Null(result.LevelJson);
        Assert.Contains(result.Errors, e => e.Contains("矩形"));
    }

    [Fact]
    public void 示例转坐标制_可往返拼装()
    {
        var native = """{"id":"gen-101","grid":["######","#@$ .#","#    #","######"]}""";

        var coord = Facade.DescribeAsCoordinates(native);

        Assert.NotNull(coord);
        var rebuilt = Facade.Compose(coord!, "101");
        Assert.Empty(rebuilt.Errors);
        var grid = System.Text.Json.Nodes.JsonNode.Parse(rebuilt.LevelJson!)!["grid"]!.AsArray()
            .Select(g => g!.GetValue<string>()).ToArray();
        // 往返不丢实体：玩家/箱子/目标位置一致
        Assert.Equal("#@$ .#", grid[1]);
    }

    [Fact]
    public void token预算_各玩法都不低于安全下限()
    {
        // 三轮 N2 后滑动猪改意图级出题（输出变小，预算与玩具趋同）；此测试退守下限：
        // 任何玩法的 token 预算都必须 ≥2048（JSON 结构 + 一次修复重写的余量），防止调参时把预算调穿底
        var pig = new SlidePigFacade();
        Assert.True(pig.EstimateLevelTokens("500") >= 2048, "滑动猪 token 预算不应低于 2048");
        Assert.True(Facade.EstimateLevelTokens("11") >= 2048, "玩具推箱子 token 预算不应低于 2048");
    }
}
