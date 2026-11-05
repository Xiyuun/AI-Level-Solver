// 【插件契约】本程序集是平台与适配层插件共享的门面契约（三轮 D7 解冻）：
// 插件引用本程序集 + Kernel 编译，运行时由平台加载器保证与平台共享同一份类型定义。
// 命名空间保留 LevelForge.Platform.Facades 以零改动迁移——不是误放。
namespace LevelForge.Platform.Facades;

// 生成端契约补充（前后端全流程优化 §8）：玩法可声明的两项生成能力
// 二者都是可选能力——门面不实现时流水线退回"模型直接输出原生 JSON"的旧路径

// token 预算提示：稠密玩法单关 JSON 远超缺省上限（滑动猪 90 头猪 ≈ 3600 token），
// 按玩法声明单次出题所需的最大输出 token，流水线据此设置 max_tokens
public interface ITokenBudgetHint
{
    int EstimateLevelTokens(string? levelNumber);   // 单次出题所需的最大输出 token（含修复余量）
}

// 结构化出题（坐标制）：模型只输出"设计意图"，关卡格式由代码拼装保证
// 动机：矩形等长/边界墙/箱子数=目标数属于精确计数任务，小模型失败率极高（实测 88% 候选死在这里）；
// 把格式正确性从模型可靠性中剥离后，剩下只有"内容是否合要求"这类真该模型负责的失败
public interface ILevelComposer
{
    string CompositionSpec { get; }                                  // 追加到系统提示词的"输出什么"说明

    ComposeResult Compose(string modelOutput, string? levelNumber);  // 拼装原生关卡 JSON；失败返回错误清单

    // few-shot 示例转坐标制：示例必须与要求的输出格式一致，否则模型会照着示例写原生 grid
    // 无法转换返回 null（调用方跳过该示例）
    string? DescribeAsCoordinates(string levelJson);
}

// 拼装结论：LevelJson 非空即成功（此时 Errors 为空）；失败时 Errors 逐条可回喂模型
public sealed record ComposeResult(string? LevelJson, IReadOnlyList<string> Errors)
{
    // 成功：得到可直接走原生格式校验的关卡 JSON
    public static ComposeResult Ok(string levelJson) => new(levelJson, []);

    // 失败：错误清单原样回喂（只给已验证事实，不猜模型意图）
    public static ComposeResult Fail(params string[] errors) => new(null, errors);

    // 失败：错误清单由调用方组装好传入
    public static ComposeResult Fail(IReadOnlyList<string> errors) => new(null, errors);
}
