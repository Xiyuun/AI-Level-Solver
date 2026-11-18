using System.Text.Json.Nodes;
using LevelForge.Adapters.PiggyFarm;
using LevelForge.Kernel.Contracts;
using LevelForge.Platform.Facades;

namespace LevelForge.Adapters.PiggyFarm.Tests;

// 合成器（去模板化）回归：见证解可回放、格式合法、质量门通过、同种子可复现、不同种子不塌缩
public sealed class PiggyFarmSynthesizerTests
{
    private static readonly PiggyFarmAdapter Adapter = new();

    private static PiggyFarmSynthesizer.Plan Plan(int band, int seed) => band switch
    {
        1 => new PiggyFarmSynthesizer.Plan(14, Slides: 1, TailCount: 1, LaneCount: 0, LongRatio: 0, RichRatio: 0, Seed: seed),
        2 => new PiggyFarmSynthesizer.Plan(92, Slides: 6, TailCount: 5, LaneCount: 1, LongRatio: 0.04, RichRatio: 0.01, Seed: seed),
        _ => new PiggyFarmSynthesizer.Plan(104, Slides: 10, TailCount: 5, LaneCount: 1, LongRatio: 0.04, RichRatio: 0.01, Seed: seed),
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 合成_见证解可被适配层原样回放且清场(int band)
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var result = PiggyFarmSynthesizer.Synthesize(Plan(band, seed));
            Assert.NotNull(result);
            var json = PiggyFarmSynthesizer.Serialize(result!.Pigs, 900 + seed, band, $"synth-{band}-{seed}");
            Assert.Empty(PiggyFarmLevels.Validate(json));

            var state = Adapter.LoadLevel(PiggyFarmLevels.Load(json));
            foreach (var id in result.Witness)
            {
                Assert.Contains(state.Pigs, p => p.Id == id);
                state = Adapter.ApplyMove(state, new PiggyFarmMove(id));
            }
            Assert.True(Adapter.IsGoal(state), $"band={band} seed={seed} 见证回放后仍有 {state.Pigs.Count} 只猪");
        }
    }

    [Fact]
    public void 合成_同种子两次生成完全相同()
    {
        var a = PiggyFarmSynthesizer.Synthesize(Plan(2, 12345));
        var b = PiggyFarmSynthesizer.Synthesize(Plan(2, 12345));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(
            PiggyFarmSynthesizer.Serialize(a!.Pigs, 500, 2, "x"),
            PiggyFarmSynthesizer.Serialize(b!.Pigs, 500, 2, "x"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 出题_32个种子得到32个不同骨架(int band)
    {
        // 验收线：同档 32 个种子必须得到 32 个不同的结构指纹（不再是"13 张模板 + 4 种对称"）
        var facade = new PiggyFarmFacade();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < 32; seed++)
        {
            var result = facade.Compose("""{"band":1}""", "900", new ComposeContext("900", band, null, seed * 104729 + band));
            Assert.True(result.LevelJson is not null, $"band={band} seed={seed}: " + string.Join("；", result.Errors));
            signatures.Add(SignatureOf(result.LevelJson!));
        }
        Assert.Equal(32, signatures.Count);
    }

    private static string SignatureOf(string levelJson)
    {
        var level = PiggyFarmLevels.Load(levelJson);
        var occupied = level.Pigs.SelectMany(p => p.Cells).ToHashSet();
        var parts = new List<string>();
        for (var by = 0; by < 5; by++)
        {
            for (var bx = 0; bx < 3; bx++)
            {
                var count = 0;
                for (var y = by * 4; y < Math.Min(19, by * 4 + 4); y++)
                {
                    for (var x = bx * 4; x < Math.Min(12, bx * 4 + 4); x++)
                    {
                        if (occupied.Contains(new GridCell(x, y))) count++;
                    }
                }
                parts.Add((count / 4).ToString());
            }
        }
        foreach (var facing in new[] { PigFacing.Up, PigFacing.Down, PigFacing.Left, PigFacing.Right })
        {
            parts.Add(level.Pigs.Count(p => p.Facing == facing).ToString());
        }
        return string.Join("|", parts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 出题_产物通过生成质量门(int band)
    {
        // 质量门是"生成候选"的门槛：合成器不保证每次都过（过不了就换种子重造），
        // 契约是"门面最终交出来的关卡一定过门"
        var facade = new PiggyFarmFacade();
        for (var seed = 0; seed < 5; seed++)
        {
            var result = facade.Compose("""{"band":1}""", "900",
                new ComposeContext("900", band, null, seed * 7919));
            Assert.True(result.LevelJson is not null, $"band={band} seed={seed}: " + string.Join("；", result.Errors));
            var errors = facade.QualityErrors(result.LevelJson!);
            Assert.True(errors.Count == 0, $"band={band} seed={seed}: {string.Join("；", errors)}");
        }
    }

    [Theory]
    [InlineData(2, 0.65, 0.99)]
    [InlineData(3, 0.75, 0.99)]
    public void 合成_占格密度落在真实关卡包络(int band, double min, double max)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var result = PiggyFarmSynthesizer.Synthesize(Plan(band, seed));
            Assert.NotNull(result);
            var cells = result!.Pigs.SelectMany(p => p.Cells).Distinct().Count();
            var density = cells / (double)(PiggyFarmSynthesizer.Width * PiggyFarmSynthesizer.Height);
            Assert.InRange(density, min, max);
        }
    }

    [Fact]
    public void 合成_单关构造耗时远低于预算()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var seed = 0; seed < 20; seed++)
        {
            Assert.NotNull(PiggyFarmSynthesizer.Synthesize(Plan(3, seed)));
        }
        var perLevel = sw.ElapsedMilliseconds / 20.0;
        Assert.True(perLevel < 200, $"单关构造平均 {perLevel:F1}ms，超出 200ms 预算");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void 合成_经门面出题命中目标档位且猪数达标(int band)
    {
        var facade = new PiggyFarmFacade();
        var level = facade.Compose("""{"band":1}""", "900", new ComposeContext("900", band, null, 424242));
        Assert.True(level.LevelJson is not null, "errors=" + string.Join("|", level.Errors));
        var loaded = PiggyFarmLevels.Load(level.LevelJson!);
        Assert.Equal(band, loaded.Difficulty);
        Assert.True(loaded.Pigs.Count >= band switch { 1 => 12, 2 => 88, _ => 100 }, $"band={band} 只有 {loaded.Pigs.Count} 只");
        var solve = new PiggyFarmSolver().Solve(loaded, TimeSpan.FromSeconds(10), 300_000, CancellationToken.None);
        Assert.Equal(SolveStatus.Solved, solve.Status);
        var weights = PiggyFarmContent.Weights;
        var score = weights.SolutionLengthWeight * solve.Solution.Count + weights.BranchingWeight * (solve.Metrics?.AvgBranchingFactor ?? 0);
        var actual = score < weights.Thresholds[0] ? 1 : score < weights.Thresholds[1] ? 2 : 3;
        Assert.Equal(band, actual);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void 出题_结构指标落在真实关卡包络(int band)
    {
        // 12 张原生真实关卡实测：密度 0.67~0.85、初始可离场 8~19%、受阻 69~88%、朝向 ≥3。
        // 生成关卡必须落在同一量级——这是"质量不降级"的硬线（封口脊机制就是把可走比例压下来的关键）。
        var facade = new PiggyFarmFacade();
        for (var seed = 0; seed < 8; seed++)
        {
            var result = facade.Compose("""{"band":1}""", "900", new ComposeContext("900", band, null, seed * 31337 + 5));
            Assert.True(result.LevelJson is not null, $"band={band} seed={seed}: " + string.Join("；", result.Errors));
            var level = PiggyFarmLevels.Load(result.LevelJson!);
            var state = Adapter.LoadLevel(level);
            var clickable = state.Pigs.Where(p => p.Type != "Duck").ToList();
            var exit = clickable.Count(p => PiggyFarmAdapter.CanExitNow(state, p)) / (double)clickable.Count;
            var blocked = clickable.Count(p => !PiggyFarmAdapter.CanAdvance(state, p)) / (double)clickable.Count;
            var density = state.Pigs.SelectMany(p => p.Cells).Distinct().Count() / (double)(PiggyFarmSynthesizer.Width * PiggyFarmSynthesizer.Height);
            Assert.InRange(exit, 0.0, 0.25);
            Assert.InRange(blocked, 0.60, 1.0);
            Assert.InRange(density, 0.65, 0.99);
            Assert.True(state.Pigs.Select(p => p.Facing).Distinct().Count() >= 3);
        }
    }

    [Fact]
    public void 出题_无类型需求时只出普通猪()
    {
        // 用户实测反馈：提示词只说"至少生成60只猪"，没提土豪猪，但每次生成中间都蹲着同样的两只金猪。
        // 根因是旧模板自带 2 只 RichPig（Level374 固定位置）；新合成器也必须遵守"需求即契约"：
        // 没有点名的特殊猪一只都不许出现。
        var facade = new PiggyFarmFacade();
        for (var seed = 0; seed < 10; seed++)
        {
            var result = facade.Compose("""{"band":2}""", "500",
                new ComposeContext("500", 2, "至少生成60只猪", seed * 7919 + 3));
            Assert.True(result.LevelJson is not null, $"seed={seed}: " + string.Join("；", result.Errors));
            var types = PiggyFarmLevels.Load(result.LevelJson!).Pigs.Select(p => p.Type).Distinct().ToArray();
            Assert.Equal(["Normal"], types);
        }
    }

    // 结构指纹：3×5 密度分档 + 朝向直方图（粗粒度——细节不同但"看起来一样"的关卡指纹相同）
    private static string Signature(PiggyFarmSynthesizer.Result result)
    {
        var occupied = new HashSet<GridCell>(result.Pigs.SelectMany(p => p.Cells));
        var parts = new List<string>();
        for (var by = 0; by < 5; by++)
        {
            for (var bx = 0; bx < 3; bx++)
            {
                var count = 0;
                for (var y = by * 4; y < Math.Min(19, by * 4 + 4); y++)
                {
                    for (var x = bx * 4; x < Math.Min(12, bx * 4 + 4); x++)
                    {
                        if (occupied.Contains(new GridCell(x, y))) count++;
                    }
                }
                parts.Add((count / 4).ToString());
            }
        }
        foreach (var facing in new[] { PigFacing.Up, PigFacing.Down, PigFacing.Left, PigFacing.Right })
        {
            parts.Add(result.Pigs.Count(p => p.Facing == facing).ToString());
        }
        return string.Join("|", parts);
    }
}

