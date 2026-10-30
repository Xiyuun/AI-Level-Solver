using System.Text.Json.Nodes;

namespace LevelForge.Kernel.Contracts;

// 解法磁带：预览动画/单步/试玩校验/真机对拍共用的唯一中间格式（求解内核 §8、渲染规范 §5）
// v1 决策：存全量快照不存 diff；执行复现须从初态按结构化动作运行，不能从显示快照恢复规则状态
public sealed record SolutionTape(
    int Version,                                   // 磁带格式版本
    string GameId,                                 // 玩法标识
    string AdapterVer,                             // 适配层版本
    string? AdapterHash,                           // 适配层产物指纹
    string KernelVer,                              // 内核版本
    string LevelRef,                               // 关卡引用（编号或文件名）
    string LevelHash,                              // 关卡文件原始字节哈希
    string ContractVer,                            // 契约版本
    string? RunConfigHash,                         // 运行资源条件指纹（无则 null）
    int MoveSchemaVer,                             // 动作 schema 版本
    int SemanticSchemaVer,                         // 语义 schema 版本
    TapeInitial Initial,                           // 初始帧
    IReadOnlyList<TapeStep> Steps,                 // 逐步记录
    ExecutionOutcome Outcome,                      // 执行结局
    string TrajectoryHash,                         // 轨迹摘要（确定性检查用）
    string ArtifactHash);                          // 磁带文件完整性摘要

// 初始帧：加载后的语义投影与场景快照（磁带第 0 步）
public sealed record TapeInitial(
    JsonNode SemanticState,           // 初始语义投影
    string SemanticStateHash,         // 初始语义摘要
    SceneSnapshot Snapshot);          // 初始场景快照

// 单步记录：结构化动作 + 语义投影 + 场景快照三者并存，各有用途（执行/对拍/绘制）
public sealed record TapeStep(
    int Index,                        // 步序号，从 1 起
    JsonNode Move,                    // 结构化动作（执行协议）
    string MoveText,                  // 仅展示，禁止反向解析成执行命令
    JsonNode SemanticState,           // 该步结算后的语义投影
    string SemanticStateHash,         // 该步语义摘要
    SceneSnapshot Snapshot);          // 该步结算后的场景快照

// 磁带与契约的当前版本常量：所有磁带产出方统一引用，禁止散落字面量
public static class TapeConstants
{
    public const int TapeVersion = 1;              // 磁带格式版本
    public const string ContractVersion = "1";     // 契约版本
    public const string KernelVersion = "0.1.0";   // 内核版本，随发布更新
}
