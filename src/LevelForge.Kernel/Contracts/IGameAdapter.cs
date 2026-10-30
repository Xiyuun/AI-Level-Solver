namespace LevelForge.Kernel.Contracts;

// 求解内核的最小契约：内核零游戏知识，所有游戏知识一律通过本接口注入（求解内核 §3）
// TLevel：项目原生关卡数据类型；TState：规则状态；TMove：结构化操作
// 换玩法 = 换适配层实现，内核与契约不动
public interface IGameAdapter<TLevel, TState, TMove>
{
    // 项目原生关卡数据 → 初始状态（直通解析，适配层不做任何 IO；非法数据必须抛异常，禁止静默修复）
    TState LoadLevel(TLevel level);

    // 枚举当前局面全部合法操作；同一状态多次调用结果一致且顺序稳定；空列表 = 无合法操作
    IReadOnlyList<TMove> GetLegalMoves(TState state);

    // 纯函数：返回新状态，严禁修改入参，不读写任何外部状态；move 必须来自本状态的 GetLegalMoves，非法操作抛异常
    TState ApplyMove(TState state, TMove move);

    // 胜利判定；须与游戏本体判定条件逐字一致
    bool IsGoal(TState state);

    // 规范化状态键：逻辑相等 ⇔ 键相等；必须覆盖状态维度枚举的全部维度（朝向、剩余步数、冷却……）
    // visited 集合由内核持有，适配层只提供键
    string StateKey(TState state);

    // 人类可读操作描述（流水线日志/磁带/失败回填提示词用）；不作为执行协议，禁止反向解析
    string DescribeMove(TState state, TMove move);

    // 可渲染场景快照（渲染规范 §2 格式）：纯函数，实体 id 全局稳定，须满足状态可视覆盖
    SceneSnapshot Snapshot(TState state);
}
