using System.Reflection;
using LevelForge.Kernel.Contracts;

namespace LevelForge.Kernel.Hosting;

// 适配托管检查的输入：全部样本来自适配层测试套件/规则矩阵，由调用方（平台或测试）提供
public sealed record HostingCheckInput<TLevel, TState, TMove>(
    AdapterBundle<TLevel, TState, TMove> Bundle,          // 适配层包
    Assembly? AdapterAssembly,                            // 适配层程序集；null 时跳过依赖扫描并记 Fail
    IReadOnlyList<StatePairSample<TState>> KeySamples,    // 状态键判别样本（维度清单来自规则矩阵）
    (TState State, TMove Move) PuritySample,              // 纯函数性样本（state 的一步合法操作）
    TLevel DeterminismLevel,                              // 确定性检查关卡（建议选可解代表关）
    byte[] LevelFileBytes);                               // 该关卡的文件字节（制带与 levelHash 用）

// 适配托管：门1五项合规检查的统一入口（求解内核 §2 AdapterHosting）
// 用法：平台在适配层验证时调用 RunAll；AllPassed 即门1机器部分结论
// 单项检查执行异常转为该项 Fail，不在门内分诊——报告说话，分诊在门外的失败回路
public sealed class AdapterHosting
{
    // 依次执行五项检查并汇总为总报告
    public ComplianceReport RunAll<TLevel, TState, TMove>(HostingCheckInput<TLevel, TState, TMove> input)
    {
        // 检查1 依赖扫描：无程序集信息 = 无法验证，明确 Fail 而非静默跳过
        var dependency = input.AdapterAssembly is { } asm
            ? SafeRun(ComplianceCheckKind.DependencyScan, () => DependencyScanner.Scan(asm))
            : ComplianceResult.Fail(ComplianceCheckKind.DependencyScan, ["未提供适配层程序集，依赖扫描未执行"]);

        // 检查2 状态键判别
        var discrimination = SafeRun(ComplianceCheckKind.StateKeyDiscrimination,
            () => ComplianceChecks.CheckStateKeyDiscrimination(input.Bundle.Game.StateKey, input.KeySamples));

        // 检查3 纯函数性
        var purity = SafeRun(ComplianceCheckKind.Purity,
            () => ComplianceChecks.CheckPurity(input.Bundle, input.PuritySample.State, input.PuritySample.Move));

        // 检查4 确定性
        var determinism = SafeRun(ComplianceCheckKind.Determinism,
            () => ComplianceChecks.CheckDeterminism(input.Bundle, input.DeterminismLevel, input.LevelFileBytes));

        // 检查5 内核自测
        var selfTest = SafeRun(ComplianceCheckKind.KernelSelfTest, KernelSelfTest.Run);

        return new ComplianceReport(dependency, discrimination, purity, determinism, selfTest);
    }

    #region 内部工具

    // 单项检查的防御包装：检查自身异常不中断其余检查，转为该项 Fail
    private static ComplianceResult SafeRun(ComplianceCheckKind kind, Func<ComplianceResult> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return ComplianceResult.Fail(kind, [$"检查执行异常：{ex.GetType().Name}：{ex.Message}"]);
        }
    }

    #endregion
}
