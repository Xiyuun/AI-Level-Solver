namespace LevelForge.Platform.Facades;

// 玩法注册元数据：games 表与注册表共用的描述（冻结决策：适配器静态注册、随平台构建编译）
public sealed record GameRegistration(
    string GameId,                             // 玩法标识（磁带 gameId，如 sokoban-toy）
    string DisplayName,                        // 显示名
    string Status,                             // GameStatus 常量：生产中/试用/接入中
    string AdapterVersion,                     // 适配层版本（磁带 adapterVer）
    string AdapterHash,                        // 适配层产物指纹（程序集字节 SHA-256）
    string KernelVersion,                      // 内核版本
    string ContractVersion,                    // 契约版本
    string LevelsRootDir,                      // 关卡根目录（安全落盘的限定根，表现层_生成工作台 §4.1）
    bool Gate2Applicable,                      // 门2真机对拍是否适用（玩具无游戏本体=false）
    string VisualMappingJson,                  // 视觉映射声明（渲染规范 §3，纯数据零代码）
    string MechanicsJson,                      // 机制解锁表（编号→可用机制，生成端用）
    string GameBrief,                          // 玩法规则描述（生成端提示词的规则书，接入产物 #5）
    string FormatSpec,                         // 关卡格式说明书（接入产物 #2 文档部分）
    string RuleMatrix,                         // 规则矩阵（开发者确认，门1组成部分）
    string DifficultyWeightsJson);             // 难度权重配置 JSON（固定权重 MVP 形态）

// 玩法状态取值（games.status；分级接入：试用档先行、转正档补验）
public static class GameStatus
{
    public const string Production = "production";     // 生产中
    public const string Trial = "trial";               // 试用（未经真机对拍，结论仅供参考）
    public const string Onboarding = "onboarding";     // 接入中（向导未走完）

    // 状态中文名（展示层用）
    public static string DisplayNameOf(string status)
    {
        return status switch
        {
            Production => "生产中",
            Trial => "试用",
            Onboarding => "接入中",
            _ => status
        };
    }
}

// 关卡来源取值（levels.source；平台无标准关卡格式，原生内容直通存储）
public static class LevelSource
{
    public const string Builtin = "builtin";           // 内置（随适配层注册导入）
    public const string Imported = "imported";         // 导入（历史关卡接入时导入）
    public const string Generated = "generated";       // 生成采纳（生成流水线落盘采纳，第10步启用）
}
