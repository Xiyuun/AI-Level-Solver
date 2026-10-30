namespace LevelForge.Kernel.Hosting;

// 合规检查类别：验证门门1的五项构成（验证门 §3）
public enum ComplianceCheckKind
{
    DependencyScan,          // 依赖扫描
    StateKeyDiscrimination,  // 状态键判别
    Purity,                  // 纯函数性
    Determinism,             // 确定性
    KernelSelfTest           // 内核自测
}

// 单项检查结论：通过标准零豁免；Issues 面向分诊，Notes 面向人工复核
public sealed record ComplianceResult(
    ComplianceCheckKind Kind,            // 检查类别
    bool Passed,                         // 是否通过
    IReadOnlyList<string> Issues,        // 未通过的具体问题（维度名/引用名/步骤）
    IReadOnlyList<string> Notes)         // 备注信息（外部引用清单等，供人工复核）
{
    // 通过便捷构造
    public static ComplianceResult Pass(ComplianceCheckKind kind, IReadOnlyList<string>? notes = null)
    {
        return new(kind, true, Array.Empty<string>(), notes ?? Array.Empty<string>());
    }

    // 未通过便捷构造
    public static ComplianceResult Fail(ComplianceCheckKind kind, IReadOnlyList<string> issues, IReadOnlyList<string>? notes = null)
    {
        return new(kind, false, issues, notes ?? Array.Empty<string>());
    }
}

// 五项检查总报告：门1机器部分的完整结论
public sealed record ComplianceReport(
    ComplianceResult DependencyScan,           // 检查1 依赖扫描
    ComplianceResult StateKeyDiscrimination,   // 检查2 状态键判别
    ComplianceResult Purity,                   // 检查3 纯函数性
    ComplianceResult Determinism,              // 检查4 确定性
    ComplianceResult KernelSelfTest)           // 检查5 内核自测
{
    // 全绿才通过：门1的通过标准是零豁免（验证门 §3）
    public bool AllPassed =>
        DependencyScan.Passed && StateKeyDiscrimination.Passed &&
        Purity.Passed && Determinism.Passed && KernelSelfTest.Passed;
}
