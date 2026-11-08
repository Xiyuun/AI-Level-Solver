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

    // 是否需要原生 few-shot 样例：模板/坐标制 composer 往往不需要（而且采样要逐关求解，代价很高）。
    // 默认 true 保持旧行为；返回 false 时流水线跳过 FewShotSelector.Sample，避免生成前白等几分钟。
    bool WantsFewShot => true;

    ComposeResult Compose(string modelOutput, string? levelNumber);  // 拼装原生关卡 JSON；失败返回错误清单

    // 带上下文的重载（默认回退到两参版本，旧 composer 不用改）：
    // TargetBand/SpecialRequirements/Seed 让 composer 能"按请求变化"，而不是每次产出同一张图
    ComposeResult Compose(string modelOutput, string? levelNumber, ComposeContext context) =>
        Compose(modelOutput, levelNumber);

    // 生成前的参数可行性检查（可选）：例如"2 档模板最多 24 只猪，无法满足 ≥30 只"。
    // 返回非空 = 这个请求在调用 LLM 之前就不可满足，流水线直接失败并给出可操作原因，
    // 不再把"几何约束"当成"格式不合法"反复回喂模型。
    IReadOnlyList<string> ValidateContext(ComposeContext context) => [];

    // few-shot 示例转坐标制：示例必须与要求的输出格式一致，否则模型会照着示例写原生 grid
    // 无法转换返回 null（调用方跳过该示例）
    string? DescribeAsCoordinates(string levelJson);
}

// 拼装上下文：目标档位 + 特殊要求 + 每次生成的随机种子（由流水线注入，避免"重新生成得到同一关"）
public sealed record ComposeContext(
    string? LevelNumber,
    int TargetBand,
    string? SpecialRequirements,
    int Seed);

// 拼装失败类型：Format=模型输出格式问题（可用修复重试回喂）；Constraint=几何/需求不可满足（模型改不了，别重试）
public enum ComposeFailureKind { Format, Constraint }

// 拼装结论：LevelJson 非空即成功（此时 Errors 为空）；失败时 Errors 逐条说明原因
public sealed record ComposeResult(string? LevelJson, IReadOnlyList<string> Errors)
{
    public ComposeFailureKind Kind { get; init; } = ComposeFailureKind.Format;

    // 成功：得到可直接走原生格式校验的关卡 JSON
    public static ComposeResult Ok(string levelJson) => new(levelJson, []);

    // 失败：错误清单原样回喂（只给已验证事实，不猜模型意图）
    public static ComposeResult Fail(params string[] errors) => new(null, errors);

    // 失败：错误清单由调用方组装好传入
    public static ComposeResult Fail(IReadOnlyList<string> errors) => new(null, errors);

    // 约束失败：需求/几何不可满足，重试模型没有意义（流水线直接记 constraint_violation）
    public static ComposeResult Reject(params string[] errors) =>
        new(null, errors) { Kind = ComposeFailureKind.Constraint };

    public static ComposeResult Reject(IReadOnlyList<string> errors) =>
        new(null, errors) { Kind = ComposeFailureKind.Constraint };
}

// 生成质量门（可选能力）：格式合法 ≠ 设计可用。适配层可声明"这种布局不算有效关卡"，
// 生成流水线在求解前拦截，并把错误回喂模型重生——避免"28 只猪全部朝右"这种可解但无设计的图。
// 只作用于生成候选；导入的原生关卡不经过本门（导入只走 FormatErrors）。
public interface ILevelQualityGate
{
    // 返回空 = 通过；非空 = 逐条可回喂的设计问题（不要在这里做求解，保持轻量）
    IReadOnlyList<string> QualityErrors(string levelJson);
}
