using System.Reflection;
using System.Runtime.Loader;
using LevelForge.Platform.Database;
using LevelForge.Platform.Facades;

namespace LevelForge.Platform.Plugins;

// 插件装载结论
public sealed record PluginLoadOutcome(
    string GameId, string Loaded, string? Error,
    string? DisplayName, string? AdapterVersion, string? AdapterHash);

// 适配层插件装载器（三轮 D7 解冻）：
// 适配层 DLL 放在数据目录 plugins/{gameId}/ 下，本装载器负责
//   ① 平台启动时全量扫描装载（重启后插件依然生效）
//   ② 向导"检测适配层"时对单个玩法定向装载 + 绑定校验
// 契约类型同一性：Kernel / FacadeContracts / 平台主程序集一律回退解析到平台已加载副本（防止类型分裂）
public sealed class PluginLoader(AppDatabase db, GameRegistry registry)
{
    public string PluginsRoot { get; } = Path.GetFullPath(Path.Combine(db.DataDir, "plugins"));   // 绝对路径：AssemblyDependencyResolver/LoadFromAssemblyPath 要求

    // 门面契约与内核的程序集简单名：这些必须与平台共享同一份，绝不从插件目录解析
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "LevelForge.Kernel", "LevelForge.FacadeContracts", "LevelForge.Platform",
    };

    // 启动全量扫描：每个子目录视为一个玩法插件；单个失败不影响其余（错误收集返回）
    public List<PluginLoadOutcome> LoadAll()
    {
        var outcomes = new List<PluginLoadOutcome>();
        if (!Directory.Exists(PluginsRoot)) return outcomes;
        foreach (var dir in Directory.EnumerateDirectories(PluginsRoot).OrderBy(d => d))
        {
            var gameId = Path.GetFileName(dir);
            var outcome = LoadForGame(gameId, allowOverwrite: true);
            outcomes.Add(outcome);
            if (outcome.Loaded == "ok")
            {
                // 权威元数据对齐进 games 行（status 保留接入流程状态；行不存在（未导入）则跳过）
                if (registry.TryGet(gameId, out var facade))
                {
                    GameSeeder.UpsertGame(db, facade.Registration);
                }
            }
        }
        return outcomes;
    }

    // 向导"检测适配层"：装载 plugins/{gameId}/ 并做绑定校验；成功后注册 + 权威列对齐
    public PluginLoadOutcome LoadForGame(string gameId, bool allowOverwrite = false)
    {
        var dir = Path.Combine(PluginsRoot, gameId);
        if (!Directory.Exists(dir))
        {
            return new PluginLoadOutcome(gameId, "missing",
                $"未找到插件目录：{dir}——请把 AI 助手产出的适配层 DLL 放入该目录（或在本页上传）", null, null, null);
        }
        if (!allowOverwrite && registry.TryGet(gameId, out _))
        {
            return new PluginLoadOutcome(gameId, "already", null, null, null, null);
        }
        // 目录内的本地 DLL 清单：加载上下文优先从插件目录解析这些程序集（防止被默认上下文从别处抢先加载）
        var localDlls = Directory.EnumerateFiles(dir, "*.dll")
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => f, StringComparer.OrdinalIgnoreCase);
        foreach (var dll in localDlls.Values.OrderBy(f => f))
        {
            try
            {
                var context = new PluginLoadContext(dll, localDlls);
                var assembly = context.LoadFromAssemblyPath(dll);
                var facadeType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IGameFacade).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
                if (facadeType is null) continue;   // 该 DLL 不是门面（可能是依赖库），继续找下一个
                var facade = (IGameFacade)Activator.CreateInstance(facadeType)!;
                var reg = facade.Registration;
                if (!string.Equals(reg.GameId, gameId, StringComparison.Ordinal))
                {
                    return new PluginLoadOutcome(gameId, "bindingError",
                        $"插件声明的 gameId 是「{reg.GameId}」，与本项目「{gameId}」不一致——请让 AI 助手修正 Registration 后重新产出 DLL",
                        reg.DisplayName, reg.AdapterVersion, reg.AdapterHash);
                }
                registry.RegisterDynamic(facade);
                GameSeeder.UpsertGame(db, reg);   // 权威元数据对齐（version/hash/产物），status 不动
                return new PluginLoadOutcome(gameId, "ok", null, reg.DisplayName, reg.AdapterVersion, reg.AdapterHash);
            }
            catch (Exception ex)
            {
                // 入口 DLL 失败如实报告（依赖库失败继续尝试其余文件）
                return new PluginLoadOutcome(gameId, "error",
                    $"插件加载失败：{Path.GetFileName(dll)} → {ex.Message}", null, null, null);
            }
        }
        return new PluginLoadOutcome(gameId, "missing",
            $"插件目录存在，但其中没有实现 IGameFacade 的门面程序集——请确认 AI 助手产出的是完整适配层 DLL", null, null, null);
    }

    // 隔离加载上下文：
    //  1) 插件目录内的本地 DLL → 优先从插件目录加载（入口/依赖都自洽）
    //  2) 契约程序集（Kernel/FacadeContracts/平台）→ 回退到平台已加载副本（类型同一性）
    //  3) 其余 → AssemblyDependencyResolver 按插件 deps 解析
    private sealed class PluginLoadContext(string dllPath, Dictionary<string, string> localDlls)
        : AssemblyLoadContext(name: dllPath, isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(dllPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var simple = assemblyName.Name ?? "";
            if (SharedAssemblies.Contains(simple))
            {
                return null;   // 回退默认上下文：与平台共享同一份契约类型（类型同一性）
            }
            if (localDlls.TryGetValue(simple, out var localPath))
            {
                return LoadFromAssemblyPath(localPath);   // 插件自带的程序集从插件目录加载
            }
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is not null ? LoadFromAssemblyPath(path) : null;
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
        }
    }
}
