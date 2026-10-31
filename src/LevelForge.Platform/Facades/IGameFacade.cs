using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using LevelForge.Kernel.Hosting;

namespace LevelForge.Platform.Facades;

// 平台侧玩法门面：把泛型适配层包成统一入口，平台服务不接触 TLevel/TState/TMove
// 存量关求解与质检粘贴共用同一 Solve；换玩法 = 注册表加一项，平台代码不动
public interface IGameFacade
{
    GameRegistration Registration { get; }                       // 注册元数据

    IReadOnlyList<string> BuiltinLevels { get; }                 // 内置关卡（原生 JSON 原文，随注册导入）

    string? TryGetLevelId(string levelJson);                     // 从原生 JSON 提取关卡编号；无该字段返回 null

    IReadOnlyList<string> FormatErrors(string levelJson);        // 静态格式校验（接入产物校验器；生成端判卷第一步）

    PlatformSolve Solve(SolveCommand command);                   // 求解 + 制带 + 难度评估

    ComplianceReport RunGate1Checks();                           // 门1五项合规检查（样本组装属玩法知识，收在门面内）

    PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves);   // 试玩步进：从初态执行动作序列（渲染规范 §6）
}

// 求解命令：原生关卡 JSON 原文 + 原始字节（levelHash 来源）+ 运行资源条件
public sealed record SolveCommand(
    string LevelJson,
    byte[] LevelFileBytes,
    string LevelRef,                        // 关卡引用（编号或"质检:xxx"）
    TimeSpan? TimeBudget = null,
    long? NodeBudget = null,
    long? MemoryBudgetBytes = null,
    bool FullEnumeration = false);

// 求解产物：五维结果 + 磁带（仅 Solved 时录制）+ 难度评估 + 资源条件指纹
public sealed record PlatformSolve(
    SolveResult Result,
    SolutionTape? Tape,
    DifficultyAssessment Difficulty,
    string? RunConfigHash);

// 试玩一步的可选项：结构化动作 + 展示文本（前端按钮/实体点击触发）
public sealed record PlayMove(JsonNode Move, string MoveText);

// 试玩局面：当前快照 + 合法动作 + 胜负判定（后端步进 API 的载荷；试玩与求解执行同一份适配层）
public sealed record PlayState(SceneSnapshot Snapshot, IReadOnlyList<PlayMove> LegalMoves, bool IsGoal, int MovesExecuted);

// 试玩执行失败（非法动作/非法关卡）：端点转 400
public sealed class PlayException(string message) : Exception(message);

// 运行资源条件指纹：任何预算/枚举模式被声明即对参数规范化哈希；全默认 = null（验证门 §2.1 runConfigHash）
public static class RunConfig
{
    // 计算：sha256(规范化 JSON)；全无限额且非完整枚举 = 无资源条件声明，返回 null
    public static string? HashOf(SolveCommand command)
    {
        if (command.TimeBudget is null && command.NodeBudget is null
            && command.MemoryBudgetBytes is null && !command.FullEnumeration)
        {
            return null;
        }
        var payload = new JsonObject
        {
            ["timeBudgetMs"] = command.TimeBudget is { } t ? (JsonNode?)(long)t.TotalMilliseconds : null,
            ["nodeBudget"] = command.NodeBudget,
            ["memoryBudgetBytes"] = command.MemoryBudgetBytes,
            ["fullEnumeration"] = command.FullEnumeration
        };
        var canonical = CanonicalJson.Serialize(payload);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
