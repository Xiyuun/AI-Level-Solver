using System.Reflection;

namespace LevelForge.Kernel.Hosting;

// 依赖扫描：适配层程序集引用引擎命名空间直接拒绝加载（求解内核 §9.4、铁律"零引擎依赖"）
// 覆盖边界：只做引用级检查，能发现显式引擎引用；
// "无 IO / 无网络 / 无时间调用 / 无全局可变状态"需代码审核与测试补充，不以机器检查冒充完整证明
public static class DependencyScanner
{
    // 引擎相关程序集名前缀（命中即拒绝；大小写不敏感）
    private static readonly string[] EnginePrefixes =
    {
        "UnityEngine", "UnityEditor", "Unity",
        "Cocos", "cocos",
        "Godot", "MonoGame", "Stride"
    };

    // 判断引用名是否属引擎命名空间（公开以便测试与平台复用）
    public static bool IsEngineReference(string assemblyName)
    {
        return EnginePrefixes.Any(p => assemblyName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // 扫描适配层程序集：引擎引用 = 拒绝项；非 BCL 外部引用 = 备注项（供人工复核是否必要）
    public static ComplianceResult Scan(params Assembly[] adapterAssemblies)
    {
        var engineRefs = new List<string>();
        var externalRefs = new List<string>();
        foreach (var asm in adapterAssemblies)
        {
            foreach (var name in asm.GetReferencedAssemblies().Select(r => r.Name).Where(n => n is not null).Select(n => n!))
            {
                if (IsEngineReference(name))
                {
                    engineRefs.Add($"{asm.GetName().Name} → {name}");
                }
                else if (!IsBclReference(name))
                {
                    externalRefs.Add($"{asm.GetName().Name} → {name}");
                }
            }
        }
        // 引擎引用存在即拒绝，外部引用仅提示复核
        return engineRefs.Count > 0
            ? ComplianceResult.Fail(ComplianceCheckKind.DependencyScan, engineRefs, externalRefs)
            : ComplianceResult.Pass(ComplianceCheckKind.DependencyScan, externalRefs);
    }

    // BCL 判定：System.* / Microsoft.* / netstandard 视为运行时基础设施
    private static bool IsBclReference(string name)
    {
        return name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name == "netstandard";
    }
}
