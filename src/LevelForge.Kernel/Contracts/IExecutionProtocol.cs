using System.Text.Json.Nodes;

namespace LevelForge.Kernel.Contracts;

// 项目执行协议：与七成员配套的序列化/投影约定（接入套件skill详设 §3.1）
// 磁带制作（ReplayEngine）、后端步进试玩、对拍比对共同依赖本协议
// 独立性要求：语义投影由适配侧从 TState 提取、harness 侧从游戏真实状态独立提取；
// 双方可共享字段规范与规范化方法，禁止用适配器转移函数伪造游戏实报
public interface IExecutionProtocol<TState, TMove>
{
    // 结构化动作 schema 版本（moveSchemaVer）
    int MoveSchemaVersion { get; }

    // 结构化动作 → JSON：写入磁带 move 字段，作为执行器的机器输入；须能无歧义定位目标与参数
    JsonNode EncodeMove(TMove move);

    // JSON → 动作：后端从磁带恢复执行用；非法编码抛异常
    TMove DecodeMove(JsonNode encoded);

    // 语义投影 schema 版本（semanticSchemaVer）
    int SemanticSchemaVersion { get; }

    // 规则状态 → 规范化语义投影：对拍比较的唯一来源；须覆盖规则矩阵中全部玩法相关字段
    JsonNode ProjectSemanticState(TState state);
}
