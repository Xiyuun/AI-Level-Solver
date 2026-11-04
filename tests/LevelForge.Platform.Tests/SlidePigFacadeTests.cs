using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LevelForge.Adapters.SlidePig;
using LevelForge.Platform.Facades;
using Xunit;

namespace LevelForge.Platform.Tests;

// 滑动猪门面与注册集成测试：注册表出现 slide-pig、A* 求解、试玩步进、门1、导入关卡不可入仓库的边界
public class SlidePigFacadeTests
{
    // 门面直测：v1 子集外类型在格式校验即被拒
    [Fact]
    public void 格式校验_子集外类型明确拒绝()
    {
        var facade = new SlidePigFacade();
        var errors = facade.FormatErrors("""{"levelId":1,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":1,"pigs":[{"id":0,"facing":"Up","pigType":"Duck","specialInfo":"","occupiedCells":[{"x":1,"y":1},{"x":1,"y":2}]}]}""");
        Assert.Contains(errors, e => e.Contains("v1 支持子集"));
        // 关卡编号提取
        Assert.Equal("7", facade.TryGetLevelId("""{"levelId":7,"pigs":[]}"""));
    }

    // 门面直测：A* 策略求解简单关 + 生成端判卷第一步格式校验通过
    [Fact]
    public void 求解_简单关A星可解()
    {
        var facade = new SlidePigFacade();
        var json = """
            {"levelId":900,"levelName":"t","mapWidth":6,"mapHeight":6,"pigCount":2,"pigs":[
            {"id":0,"facing":"Right","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":3,"y":3},{"x":4,"y":3}]},
            {"id":1,"facing":"Up","pigType":"Normal","specialInfo":"","occupiedCells":[{"x":0,"y":4},{"x":0,"y":5}]}]}
            """;
        Assert.Empty(facade.FormatErrors(json));
        var solve = facade.Solve(new SolveCommand(json, System.Text.Encoding.UTF8.GetBytes(json), "900"));
        Assert.Equal(LevelForge.Kernel.Contracts.SolveStatus.Solved, solve.Result.Status);
        Assert.NotNull(solve.Tape);
        Assert.True(solve.Tape!.Steps.Count >= 2);
    }

    // 门面直测：门1五项全绿
    [Fact]
    public void 门1合规_五项全绿()
    {
        var report = new SlidePigFacade().RunGate1Checks();
        Assert.True(report.AllPassed, string.Join("；",
            report.DependencyScan.Issues.Concat(report.StateKeyDiscrimination.Issues)
            .Concat(report.Purity.Issues).Concat(report.Determinism.Issues)));
    }

    // 三轮移除守卫（2026-11-04）：slide-pig 已退出静态注册表——真实 PiggyFarm 改走「导入项目」流程，
    // 平台玩法列表里不应再出现内置滑动猪；门面源码保留作为 piggyfarm 适配层的参考实现
    [Fact]
    public async Task 注册表_滑动猪已移除()
    {
        using var factory = new PlatformFactory();
        using var client = factory.CreateClient();
        var games = await client.GetFromJsonAsync<JsonElement[]>("/api/games");
        Assert.DoesNotContain(games!, g => g.GetProperty("gameId").GetString() == "slide-pig");
    }
}
