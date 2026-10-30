namespace LevelForge.Kernel.Contracts;

// 适配层包：游戏适配器 + 执行协议 + 元信息的统一挂载单元
// 求解、回放、合规检查均以包为单位消费，避免接口在签名里成对传递
public sealed record AdapterBundle<TLevel, TState, TMove>(
    string GameId,                                        // 玩法标识（磁带 gameId）
    string AdapterVersion,                                // 适配层版本（磁带 adapterVer）
    IGameAdapter<TLevel, TState, TMove> Game,             // 七成员游戏适配器
    IExecutionProtocol<TState, TMove> Execution);         // 执行协议（动作编码/语义投影）
