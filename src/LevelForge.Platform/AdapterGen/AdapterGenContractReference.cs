using System.Reflection;
using System.Text;
using LevelForge.Kernel.Analysis;
using LevelForge.Kernel.Contracts;
using LevelForge.Kernel.Hosting;
using LevelForge.Kernel.Replay;
using LevelForge.Kernel.Search;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.AdapterGen;

// 契约速查：从平台已加载程序集反射生成"枚举成员/构造器参数/接口成员/静态方法"清单
// 动机：路径 A 的模型看不到源码，只能在提示词里猜——实测 4 轮编译错误集中在
//   Optimality.Optimal / TerminationReason.GoalReached / MetricsScope.Search 等不存在的枚举成员，
//   以及 SceneSnapshot/PlayState 少传参数、适配器没实现 IExecutionProtocol。
// 反射生成保证与真实契约同步，不会随版本漂移；输出一次缓存。
public static class AdapterGenContractReference
{
    private static readonly Lazy<string> Cached = new(BuildCore, isThreadSafe: true);

    public static string Build() => Cached.Value;

    #region 生成

    private static string BuildCore()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 契约速查（由平台程序集反射生成，成员名/参数顺序以本清单为准）");
        sb.AppendLine();
        sb.AppendLine("## 枚举（只能使用下列成员，拼错即 CS0117）");
        AppendEnum<SolveStatus>(sb);
        AppendEnum<Optimality>(sb);
        AppendEnum<TerminationReason>(sb);
        AppendEnum<MetricsScope>(sb);
        AppendEnum<ComplianceCheckKind>(sb);
        AppendEnum<MoveCategory>(sb);
        sb.AppendLine();

        sb.AppendLine("## 记录/类构造器（参数按序；可选参数可省略）");
        AppendCtor<GameRegistration>(sb);
        AppendCtor<SolveCommand>(sb);
        AppendCtor<PlatformSolve>(sb);
        AppendCtor<SolveResult>(sb);
        AppendCtor<SearchMetrics>(sb);
        AppendCtor<MoveRecord>(sb);
        AppendCtor<SceneSnapshot>(sb);
        AppendCtor<BoardSize>(sb);
        AppendCtor<Cell>(sb);
        AppendCtor<Entity>(sb);
        AppendCtor<PlayState>(sb);
        AppendCtor<PlayMove>(sb);
        AppendCtor<ComplianceReport>(sb);
        AppendCtor<ComplianceResult>(sb);
        AppendCtor<DifficultyWeights>(sb);
        AppendCtor<DifficultyAssessment>(sb);
        AppendGenericCtor(typeof(AdapterBundle<,,>), "AdapterBundle<TLevel,TState,TMove>", sb);
        AppendGenericCtor(typeof(SolveRequest<>), "SolveRequest<TLevel>", sb);
        AppendGenericCtor(typeof(HostingCheckInput<,,>), "HostingCheckInput<TLevel,TState,TMove>", sb);
        AppendGenericCtor(typeof(StatePairSample<>), "StatePairSample<TState>", sb);
        sb.AppendLine();

        sb.AppendLine("## 接口成员（签名逐字）");
        AppendInterface(typeof(IGameFacade), "IGameFacade", sb);
        AppendGenericInterface(typeof(IGameAdapter<,,>), "IGameAdapter<TLevel,TState,TMove>", sb);
        AppendGenericInterface(typeof(IExecutionProtocol<,>), "IExecutionProtocol<TState,TMove>", sb);
        sb.AppendLine();

        sb.AppendLine("## 静态方法");
        AppendStaticMethod(typeof(ReplayEngine), "RecordTape", sb, "SolutionTape");
        AppendStaticMethod(typeof(AdapterHosting), "RunAll", sb, "ComplianceReport");
        AppendStaticMethod(typeof(BfsSolver<,,>), "Solve", sb, "SolveResult");
        AppendStaticMethod(typeof(RunConfig), "HashOf", sb, "string?");
        AppendStaticMethod(typeof(DifficultyModel), "Evaluate", sb, "DifficultyAssessment");
        AppendStaticMethod(typeof(FeatureExtractor), "Extract", sb, "DifficultyFeatures");
        return sb.ToString();
    }

    private static void AppendEnum<TEnum>(StringBuilder sb) where TEnum : struct, Enum
    {
        sb.AppendLine($"{typeof(TEnum).Name}: {string.Join(", ", Enum.GetNames<TEnum>())}");
    }

    private static void AppendCtor<T>(StringBuilder sb)
    {
        var type = typeof(T);
        sb.AppendLine($"{type.Name}{FormatCtor(type)}");
    }

    private static void AppendGenericCtor(Type definition, string display, StringBuilder sb)
    {
        sb.AppendLine($"{display}{FormatCtor(definition)}");
    }

    private static string FormatCtor(Type type)
    {
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null) return "()";
        var parameters = ctor.GetParameters().Select(p => $"{Friendly(p.ParameterType)} {p.Name}");
        return "(" + string.Join(", ", parameters) + ")";
    }

    private static void AppendInterface(Type type, string display, StringBuilder sb)
    {
        sb.AppendLine($"{display}:");
        foreach (var method in type.GetMethods().Where(m => !m.IsSpecialName))
        {
            sb.AppendLine($"  {Friendly(method.ReturnType)} {method.Name}({FormatParameters(method.GetParameters())})");
        }
        foreach (var property in type.GetProperties())
        {
            sb.AppendLine($"  {Friendly(property.PropertyType)} {property.Name} {{ get; }}");
        }
    }

    private static void AppendGenericInterface(Type definition, string display, StringBuilder sb)
    {
        AppendInterface(definition, display, sb);
    }

    private static void AppendStaticMethod(Type type, string name, StringBuilder sb, string returnDisplay)
    {
        var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name)
            .OrderByDescending(m => m.GetParameters().Length)
            .FirstOrDefault();
        if (method is null)
        {
            sb.AppendLine($"{type.Name}.{name}: （未找到）");
            return;
        }
        var generic = method.IsGenericMethodDefinition
            ? "<" + string.Join(",", method.GetGenericArguments().Select(a => a.Name)) + ">"
            : "";
        sb.AppendLine($"{type.Name}{generic}.{method.Name}({FormatParameters(method.GetParameters())}) → {returnDisplay}");
    }

    private static string FormatParameters(ParameterInfo[] parameters)
    {
        return string.Join(", ", parameters.Select(p =>
            $"{Friendly(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = …" : "")}"));
    }

    // 类型友好名：去掉命名空间；泛型显示为 Name<Arg1,Arg2>；可空/数组原样
    private static string Friendly(Type type)
    {
        if (type.IsGenericParameter) return type.Name;
        if (type.IsArray) return Friendly(type.GetElementType()!) + "[]";
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return Friendly(nullable) + "?";
        if (type.IsGenericType)
        {
            var name = type.Name.Split('`')[0];
            var args = type.GetGenericArguments().Select(Friendly);
            return $"{name}<{string.Join(",", args)}>";
        }
        return type.Name;
    }

    #endregion
}
