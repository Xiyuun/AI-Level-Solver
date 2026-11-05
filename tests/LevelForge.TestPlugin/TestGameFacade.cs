using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Platform.Facades;

namespace LevelForge.TestPlugin;

// 测试插件（三轮 D7）：最小 IGameFacade 实现——只用于装载器/检测链路的自动化验证。
// Solve/Play 不做真实实现（测试不触达）；RunGate1Checks 返回合成全绿报告。
public sealed class TestGameFacade : IGameFacade
{
    public const string GameIdValue = "test-plugin";

    public GameRegistration Registration => new(
        GameIdValue,
        "测试插件玩法",
        GameStatus.Trial,
        "1.0.0",
        AdapterFingerprint.Of(typeof(TestGameFacade).Assembly),
        TapeConstants.KernelVersion,
        TapeConstants.ContractVersion,
        "levels/test-plugin",
        Gate2Applicable: false,
        VisualMappingJson: "{}",
        MechanicsJson: "{}",
        GameBrief: "测试插件玩法（仅用于装载链路验证）",
        FormatSpec: "{}",
        RuleMatrix: "",
        DifficultyWeightsJson: "{}");

    public IReadOnlyList<string> BuiltinLevels => Array.Empty<string>();

    public string? TryGetLevelId(string levelJson)
    {
        try
        {
            return JsonNode.Parse(levelJson)?["id"]?.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> FormatErrors(string levelJson)
    {
        return Array.Empty<string>();   // 测试插件不做真实校验
    }

    public PlatformSolve Solve(SolveCommand command)
    {
        var result = new SolveResult(
            SolveStatus.Invalid, Optimality.NotApplicable, false,
            TerminationReason.InvalidInput, MetricsScope.ExpandedPrefix,
            [], null, "测试插件不实现真实求解");
        var difficulty = new DifficultyAssessment(0, 0, "test-plugin", [], []);
        return new PlatformSolve(result, null, difficulty, null);
    }

    public ComplianceReport RunGate1Checks()
    {
        return new ComplianceReport(
            ComplianceResult.Pass(ComplianceCheckKind.DependencyScan, ["测试插件：合成报告"]),
            ComplianceResult.Pass(ComplianceCheckKind.StateKeyDiscrimination),
            ComplianceResult.Pass(ComplianceCheckKind.Purity),
            ComplianceResult.Pass(ComplianceCheckKind.Determinism),
            ComplianceResult.Pass(ComplianceCheckKind.KernelSelfTest));
    }

    public PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)
    {
        throw new PlayException("测试插件不实现试玩");
    }
}
