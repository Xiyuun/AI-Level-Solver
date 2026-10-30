using System.Text.Json.Nodes;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hashing;

namespace LevelForge.Kernel.Replay;

// 磁带元信息：来自调用方（平台）的产物指纹（求解内核 §8、渲染规范 §5）
public sealed record TapeMetadata(
    string LevelRef,            // 关卡引用（编号或文件名）
    string? AdapterHash,        // 适配层产物指纹
    string? RunConfigHash);     // 运行资源条件指纹

// 回放引擎：拿 LoadLevel + 操作序列，逐步执行结构化动作，
// 录制每步的语义状态投影与完整场景快照（求解内核 §8）
// v1 存全量快照；执行复须从初态按结构化动作运行，不能从显示快照恢复规则状态
// 磁带是"预览动画/单步/试玩校验/真机对拍"共用的唯一中间格式
public sealed class ReplayEngine
{
    // 从求解结果录制磁带（便捷重载：取结果中的操作序列）
    public SolutionTape RecordTape<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        TLevel level,
        byte[] levelFileBytes,
        SolveResult result,
        TapeMetadata metadata)
    {
        return RecordTape(bundle, level, levelFileBytes, result.Solution, metadata);
    }

    // 从操作序列录制磁带：动作 JSON 经解码后重新执行，结局由实际执行重判（不采信求解结论）
    public SolutionTape RecordTape<TLevel, TState, TMove>(
        AdapterBundle<TLevel, TState, TMove> bundle,
        TLevel level,
        byte[] levelFileBytes,
        IReadOnlyList<MoveRecord> solution,
        TapeMetadata metadata)
    {
        var game = bundle.Game;
        var execution = bundle.Execution;

        // 初始帧：加载 → 语义投影 + 快照 + 摘要
        var state = game.LoadLevel(level);
        var initialSemantic = execution.ProjectSemanticState(state);
        var initial = new TapeInitial(
            initialSemantic,
            TapeHashes.SemanticStateHash(execution.SemanticSchemaVersion, initialSemantic),
            game.Snapshot(state));

        // 逐步执行：解码 → 应用 → 投影 → 快照；非法编码/非法操作按契约抛异常，中断录制
        var stateSteps = new List<(JsonNode Move, JsonNode Semantic)>();
        var tapeSteps = new List<TapeStep>();
        var index = 0;
        foreach (var record in solution)
        {
            index++;
            var move = execution.DecodeMove(record.Move);
            state = game.ApplyMove(state, move);
            var semantic = execution.ProjectSemanticState(state);
            stateSteps.Add((record.Move, semantic));
            tapeSteps.Add(new TapeStep(
                index,
                record.Move,
                record.MoveText,
                semantic,
                TapeHashes.SemanticStateHash(execution.SemanticSchemaVersion, semantic),
                game.Snapshot(state)));
        }

        // 结局由实际执行重判：序列执行完到目标 = win，否则 incomplete（lose/error 由对拍/执行层产生）
        var outcome = game.IsGoal(state) ? ExecutionOutcome.Win : ExecutionOutcome.Incomplete;

        // 轨迹摘要：排除 moveText 与视觉表现（渲染规范 §5.1）
        var trajectory = TapeHashes.TrajectoryHash(
            execution.MoveSchemaVersion, execution.SemanticSchemaVersion,
            initialSemantic, stateSteps, outcome);

        // 组装磁带（artifactHash 先占位，序列化后回填）
        var tape = new SolutionTape(
            TapeConstants.TapeVersion,
            bundle.GameId, bundle.AdapterVersion, metadata.AdapterHash,
            TapeConstants.KernelVersion,
            metadata.LevelRef, TapeHashes.LevelHash(levelFileBytes),
            TapeConstants.ContractVersion, metadata.RunConfigHash,
            execution.MoveSchemaVersion, execution.SemanticSchemaVersion,
            initial, tapeSteps, outcome, trajectory, string.Empty);

        // 文件完整性摘要：规范化磁带内容（artifactHash 置 null 参与）后哈希，无自包含循环
        var artifact = TapeHashes.ArtifactHash((JsonObject)System.Text.Json.JsonSerializer.SerializeToNode(tape, TapeJson.Options)!);
        return tape with { ArtifactHash = artifact };
    }
}
