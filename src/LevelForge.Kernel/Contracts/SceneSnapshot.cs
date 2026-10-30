using System.Text.Json.Nodes;

namespace LevelForge.Kernel.Contracts;

// 场景快照：某一时刻的可渲染投影（渲染规范 §2）
// 不是关卡数据、也不是完整规则状态；语义对拍另用 IExecutionProtocol 的语义投影
public sealed record SceneSnapshot(
    BoardSize Board,                                        // 棋盘尺寸，v1 仅矩形网格
    IReadOnlyList<Cell> Cells,                              // 格子层（无 id）
    IReadOnlyList<Entity> Entities,                         // 实体层（id 稳定唯一）
    IReadOnlyDictionary<string, JsonNode?> Globals);        // 全局量（剩余步数/库存等，信息栏显示）

// 矩形棋盘尺寸：坐标约定 x=列、y=行、原点左上
public sealed record BoardSize(int Width, int Height);

// 格子层单元：类型名是视觉映射的键
public sealed record Cell(
    int X,                    // 列号
    int Y,                    // 行号
    string Type);             // 格子类型（wall/goal/...，玩法自定义）

// 实体层单元：影响玩家决策的局部量放 type/props 中
public sealed record Entity(
    string Id,                                        // 实体 id，一次关卡执行内稳定且唯一
    string Type,                                      // 实体类型（视觉映射的键）
    int X,                                            // 列号
    int Y,                                            // 行号
    IReadOnlyDictionary<string, JsonNode?>? Props);   // 自定义属性（朝向等，渲染规则声明显示方式）
