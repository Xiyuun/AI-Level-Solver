using System.Text.Json;
using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;

namespace LevelForge.Kernel.Hosting;

// 状态维度判别样本：来自适配层测试套件与规则矩阵，描述"仅该维度不同"的状态对
// 维度清单由资格报告草拟、开发者规则矩阵确认（接入套件skill详设 §5/§7）
public sealed record StatePairSample<TState>(
    string Dimension,      // 维度名（位置/朝向/剩余步数……），用于失败时点名
    TState A,              // 状态A
    TState B);             // 状态B：与A仅在该维度上不同

// 合规检查 2/3/4：状态键判别、纯函数性、确定性（求解内核 §9）
// 全部检查只产出结论不抛异常；检查执行异常由 AdapterHosting 统一转为该项 Fail
public static class ComplianceChecks
{
    #region 检查2 状态键判别

    // 对每个维度样本断言键不同：键设计漏维度（如漏掉朝向）在这里现形（求解内核 §9.1）
    // 空样本集本身就是问题——维度覆盖未经确认，不允许通过
    public static ComplianceResult CheckStateKeyDiscrimination<TState>(
        Func<TState, string> stateKey,
        IEnumerable<StatePairSample<TState>> samples)
    {
        var list = samples.ToList();
        if (list.Count == 0)
        {
            return ComplianceResult.Fail(ComplianceCheckKind.StateKeyDiscrimination,
                ["未提供任何维度判别样本——维度覆盖未经确认，空集不允许通过"]);
        }
        var issues = list
            .Where(s => string.Equals(stateKey(s.A), stateKey(s.B), StringComparison.Ordinal))
            .Select(s => $"维度[{s.Dimension}]：两状态键相同（\"{stateKey(s.A)}\"），键设计疑似漏该维度")
            .ToList();
        return issues.Count == 0
            ? ComplianceResult.Pass(ComplianceCheckKind.StateKeyDiscrimination, [$"已覆盖 {list.Count} 个维度样本"])
            : ComplianceResult.Fail(ComplianceCheckKind.StateKeyDiscrimination, issues);
    }

    #endregion

    #region 检查3 纯函数性

    // 同一状态+同一操作重复应用，结果经【语义投影+场景快照+状态键】三视角深比较一致；
    // 入参变异检测：每次调用后重新投影原状态，投影变化即判定入参被修改（求解内核 §9.2）
    // move 必须来自该状态的 GetLegalMoves（非法操作按契约抛异常，转为该项 Fail）
    // 覆盖边界：三投影均不可见的字段变异无法检出，由规则矩阵与代码审核补充
    public static ComplianceResult CheckPurity<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        TState state,
        TMove move,
        int repeats = 3)
    {
        var game = bundle.Game;
        var exec = bundle.Execution;
        var issues = new List<string>();

        // 基线：原状态三视角（规范化后比较，键序差异不算差异）
        var baseKey = game.StateKey(state);
        var baseSemantic = CanonicalJson.Serialize(exec.ProjectSemanticState(state));
        var baseSnapshot = CanonicalJson.Serialize(ToJsonNode(game.Snapshot(state)));

        // 合法操作枚举稳定性：两次调用编码序列一致（顺序稳定契约）
        var legal1 = game.GetLegalMoves(state).Select(m => CanonicalJson.Serialize(exec.EncodeMove(m))).ToList();
        var legal2 = game.GetLegalMoves(state).Select(m => CanonicalJson.Serialize(exec.EncodeMove(m))).ToList();
        if (!legal1.SequenceEqual(legal2))
        {
            issues.Add("GetLegalMoves 两次调用顺序不一致（违反顺序稳定契约）");
        }

        // 重复应用：结果三视角必须逐次一致
        string? firstResult = null;
        for (var i = 1; i <= repeats; i++)
        {
            var result = game.ApplyMove(state, move);
            var view = string.Join('|',
                game.StateKey(result),
                CanonicalJson.Serialize(exec.ProjectSemanticState(result)),
                CanonicalJson.Serialize(ToJsonNode(game.Snapshot(result))));
            if (firstResult is null)
            {
                firstResult = view;
            }
            else if (view != firstResult)
            {
                issues.Add($"ApplyMove 第 {i} 次应用与首次结果不一致（隐藏状态或不稳定实现）");
            }

            // 入参变异检测：调用后再看原状态，任一视角变化即判定被修改
            if (game.StateKey(state) != baseKey
                || CanonicalJson.Serialize(exec.ProjectSemanticState(state)) != baseSemantic
                || CanonicalJson.Serialize(ToJsonNode(game.Snapshot(state))) != baseSnapshot)
            {
                issues.Add($"ApplyMove 第 {i} 次调用修改了入参状态（调用后原状态投影变化）");
            }
        }

        return issues.Count == 0
            ? ComplianceResult.Pass(ComplianceCheckKind.Purity, [$"重复应用 {repeats} 次，三视角一致且入参未变"])
            : ComplianceResult.Fail(ComplianceCheckKind.Purity, issues);
    }

    #endregion

    #region 检查4 确定性

    // 同输入、同版本、同配置双跑求解并制带：结论与轨迹摘要必须一致（求解内核 §9.3）
    // 行为判断以 trajectoryHash 为准；artifactHash 只查文件完整性，不跨版本替代行为判断
    public static ComplianceResult CheckDeterminism<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        TLevel level,
        byte[] levelFileBytes)
    {
        var issues = new List<string>();

        // 双跑求解：默认请求（无预算、无取消），两次独立实例避免任何共享状态侥幸
        var solver = new BfsSolver<TLevel, TState, TMove>();
        var r1 = solver.Solve(bundle, new SolveRequest<TLevel>(level));
        var r2 = new BfsSolver<TLevel, TState, TMove>().Solve(bundle, new SolveRequest<TLevel>(level));

        // 结论四元组一致
        if (r1.Status != r2.Status || r1.TerminationReason != r2.TerminationReason
            || r1.GraphComplete != r2.GraphComplete || r1.Optimality != r2.Optimality)
        {
            issues.Add($"两次求解结论不一致：({r1.Status}/{r1.TerminationReason}) vs ({r2.Status}/{r2.TerminationReason})");
        }

        // 操作序列一致（结构化动作与展示文本分别比对）
        var seq1 = r1.Solution.Select(s => CanonicalJson.Serialize(s.Move)).ToList();
        var seq2 = r2.Solution.Select(s => CanonicalJson.Serialize(s.Move)).ToList();
        if (!seq1.SequenceEqual(seq2))
        {
            issues.Add("两次求解的结构化动作序列不一致");
        }
        if (!r1.Solution.Select(s => s.MoveText).SequenceEqual(r2.Solution.Select(s => s.MoveText)))
        {
            issues.Add("两次求解的操作描述序列不一致");
        }

        // 制带双跑：轨迹摘要一致（语义状态序列的哈希级验证）
        var engine = new ReplayEngine();
        var meta = new TapeMetadata("determinism-check", null, null);
        var t1 = engine.RecordTape(bundle, level, levelFileBytes, r1, meta);
        var t2 = engine.RecordTape(bundle, level, levelFileBytes, r2, meta);
        if (t1.TrajectoryHash != t2.TrajectoryHash)
        {
            issues.Add("两次录制的轨迹摘要（trajectoryHash）不一致——语义状态序列非确定");
        }

        return issues.Count == 0
            ? ComplianceResult.Pass(ComplianceCheckKind.Determinism,
                [$"结论一致，轨迹摘要 {t1.TrajectoryHash[..16]}…"])
            : ComplianceResult.Fail(ComplianceCheckKind.Determinism, issues);
    }

    #endregion

    #region 内部工具

    // 记录 → JsonNode（复用磁带序列化选项：camelCase + 枚举小写）
    private static JsonNode ToJsonNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, TapeJson.Options)!;
    }

    #endregion
}
