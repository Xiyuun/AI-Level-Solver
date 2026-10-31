using System.Text.Json;
using System.Text.Json.Serialization;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Platform.Gates;

// 门2驱动配置（对拍驱动，接入产物⑦）：声明"怎么启动/怎么传输入/去哪读结果"
// 平台执行器只认配置，每个项目对平台就是一条可执行命令（验证门 §4.5）
public sealed record Gate2DriverConfig(
    string Command,                       // 可执行命令（如 Unity.exe 或 cmd）
    IReadOnlyList<string> Args,           // 参数模板；占位符 {levelFile} {tapeFile} {outputFile}
    int TimeoutSeconds = 180)             // 单关执行超时；超时 kill 标 timeout
{
    // 解析驱动配置 JSON；非法配置抛 JsonException（配置是登记时的审核物）
    public static Gate2DriverConfig Parse(string json)
    {
        return JsonSerializer.Deserialize<Gate2DriverConfig>(json, JsonOpts) ?? throw new JsonException("驱动配置为空");
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

// harness 报告（磁带协议的对侧）：游戏本体独立执行的实报（验证门 §4.2）
// 独立性要求：这些值由 harness 从游戏真实状态提取，禁止由适配器代算
public sealed record HarnessReport(
    string LevelHash,                     // 实际加载文件的指纹（关卡数据错位防线）
    string InitialSemanticHash,           // 初始语义状态摘要
    IReadOnlyList<string> StepSemanticHashes,   // 逐步语义状态摘要（顺序对应磁带步骤）
    string Outcome,                       // win|lose|incomplete|error
    string? GameBuildFingerprint,         // 游戏版本指纹（结论绑定版本）
    string? HarnessHash)                  // harness 产物指纹
{
    // 解析 harness 报告 JSON
    public static HarnessReport Parse(string json)
    {
        return JsonSerializer.Deserialize<HarnessReport>(json, JsonOpts)
               ?? throw new JsonException("harness 报告为空");
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

// 对拍比对结论：首分歧定位（0=初始即不一致）+ 结局核对（验证门 §4.2）
public sealed record Gate2Verdict(
    bool Passed,
    int? FirstDivergenceStep,             // null=无分歧；0=初始；k=第k步
    string? DivergenceDetail,             // 分歧说明（差异字段描述由 harness 侧补充）
    bool OutcomeMatch,                    // 游戏胜负信号与磁带执行结局是否一致
    string TapeOutcome,                   // 磁带结局（适配侧执行重判）
    string HarnessOutcome)                // harness 实报结局
{
    // 通过便捷构造
    public static Gate2Verdict Ok(string outcome) =>
        new(true, null, null, true, outcome, outcome);
}

// 对拍比对器：磁带（适配侧）× harness 报告（游戏侧）的逐步语义差分
// 逐步状态自动比较与首分歧定位属于基础对拍能力，不后置（验证门 §8）
public static class Gate2Comparator
{
    // 比对：初始摘要 → 步数 → 逐步摘要 → 结局；任一环不一致即 fail 并给出首分歧位置
    public static Gate2Verdict Compare(SolutionTape tape, HarnessReport report)
    {
        // 加载指纹先核对：加载错位在这里现形（§4.3 分诊第3类）
        if (!string.Equals(report.LevelHash, tape.LevelHash, StringComparison.OrdinalIgnoreCase))
        {
            return new Gate2Verdict(false, 0,
                $"harness 加载的关卡指纹与磁带不一致（报告 {report.LevelHash}，磁带 {tape.LevelHash}）——疑似关卡数据错位",
                false, tape.Outcome.ToString().ToLowerInvariant(), report.Outcome);
        }
        // 初始语义（第0步）
        if (!string.Equals(report.InitialSemanticHash, tape.Initial.SemanticStateHash, StringComparison.OrdinalIgnoreCase))
        {
            return new Gate2Verdict(false, 0, "初始语义状态不一致（第 0 步）", false,
                tape.Outcome.ToString().ToLowerInvariant(), report.Outcome);
        }
        // 步数：报告步数多于磁带 = 游戏侧多执行；少于 = 提前停止，都算分歧
        var tapeHashes = tape.Steps.Select(s => s.SemanticStateHash).ToList();
        if (report.StepSemanticHashes.Count != tapeHashes.Count)
        {
            return new Gate2Verdict(false, Math.Min(report.StepSemanticHashes.Count, tapeHashes.Count) + 1,
                $"步数不一致：harness {report.StepSemanticHashes.Count} 步，磁带 {tapeHashes.Count} 步", false,
                tape.Outcome.ToString().ToLowerInvariant(), report.Outcome);
        }
        // 逐步语义：首个不一致的步号（1 起）
        for (var i = 0; i < tapeHashes.Count; i++)
        {
            if (!string.Equals(report.StepSemanticHashes[i], tapeHashes[i], StringComparison.OrdinalIgnoreCase))
            {
                return new Gate2Verdict(false, i + 1,
                    $"第 {i + 1} 步语义状态不一致：harness {report.StepSemanticHashes[i]}，磁带 {tapeHashes[i]}",
                    true, tape.Outcome.ToString().ToLowerInvariant(), report.Outcome);
            }
        }
        // 结局核对（游戏自身胜负判定 vs 磁带执行重判）
        var tapeOutcome = tape.Outcome.ToString().ToLowerInvariant();
        var outcomeMatch = string.Equals(report.Outcome, tapeOutcome, StringComparison.OrdinalIgnoreCase);
        return new Gate2Verdict(true, null, null, outcomeMatch, tapeOutcome, report.Outcome);
    }
}
