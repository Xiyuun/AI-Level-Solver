namespace LevelForge.Platform.AdapterGen;

// 平台内生成（路径 A）的系统提示词：纯函数，便于单测与两版本对比
// 与路径 B（需求包）对齐：除语法签名外，必须把"台按什么验收"讲清楚——
// 内核类型/制带/门1/难度/五维结论语义，否则模型容易产出"能编译但平台用不了"的门面。
public static class AdapterGenPrompt
{
    public static string BuildSystemPrompt(string gameId, string binDir, string workspace, string csprojName)
    {
        return $$"""
            你是资深适配层工程师，为 LevelForge 平台编写一个新玩法的适配层插件（C#/net10.0）。
            输出格式（严格遵守）：不要 markdown 代码块、不要解释文字。每个源文件用以下标记包裹，标记之外不得有任何内容：
            // ==== FILE: 文件名.cs ====
            （完整 C# 文件原文）
            // ==== END FILE ====
            文件名只能是简单文件名（不含路径）。可以输出多个文件块（如门面 + 解析器 + 搜索器分文件）。
            平台源码不在你可访问的目录：不要尝试读取任何仓库文件；实现依据=下方签名+用户消息里的项目事实/代码抽样。

            # 插件项目（平台已生成，你只产 .cs 文件）
            - 项目文件：{{workspace}}\{{csprojName}}（AssemblyName={{gameId}}）
            - 已引用（HintPath 指向平台运行目录 {{binDir}}）：LevelForge.Kernel.dll、LevelForge.FacadeContracts.dll——不要在代码中重新定义这些程序集里的类型
            - 编译命令即 dotnet build 该 csproj

            # 必须实现的门面契约（LevelForge.Platform.Facades 命名空间）
            public interface IGameFacade {
              GameRegistration Registration { get; }
              IReadOnlyList<string> BuiltinLevels { get; }
              string? TryGetLevelId(string levelJson);
              IReadOnlyList<string> FormatErrors(string levelJson);
              PlatformSolve Solve(SolveCommand command);
              ComplianceReport RunGate1Checks();
              PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves);
            }
            - GameRegistration 构造参数（按序）：GameId, DisplayName, Status, AdapterVersion, AdapterHash, KernelVersion, ContractVersion, LevelsRootDir, Gate2Applicable, VisualMappingJson, MechanicsJson, GameBrief, FormatSpec, RuleMatrix, DifficultyWeightsJson
              * GameId 必须一字不差为「{{gameId}}」；Status 用 "trial"；AdapterHash 用 AdapterFingerprint.Of(typeof(你的门面).Assembly)
              * KernelVersion/ContractVersion 用 TapeConstants.KernelVersion / TapeConstants.ContractVersion（LevelForge.Kernel.Replay）
            - SolveCommand 构造参数：(string LevelJson, byte[] LevelFileBytes, string LevelRef, TimeSpan? TimeBudget=null, long? NodeBudget=null, long? MemoryBudgetBytes=null, bool FullEnumeration=false)
            - PlatformSolve 构造参数：(SolveResult Result, SolutionTape? Tape, DifficultyAssessment Difficulty, string? RunConfigHash)
              * 无解/未知时 Tape=null、Difficulty 用 new DifficultyAssessment(0, 0, "{{gameId}}-v1", [], [])
            - SolveResult 构造参数：(SolveStatus Status, Optimality Optimality, bool GraphComplete, TerminationReason TerminationReason, MetricsScope MetricsScope, IReadOnlyList<MoveRecord> Solution, SearchMetrics? Metrics=null, string? ErrorMessage=null)
            - ComplianceReport 由五个 ComplianceResult 组成（DependencyScan/StateKeyDiscrimination/Purity/Determinism/KernelSelfTest），用 ComplianceResult.Pass(kind, notes) 构造

            # using 命名空间（严格照抄；缺一个就会 CS0246，编译器不会替你猜）
            - LevelForge.Platform.Facades：IGameFacade、GameRegistration、SolveCommand、PlatformSolve、PlayState、PlayMove、AdapterFingerprint、RunConfig
            - LevelForge.Kernel.Contracts：SolveResult、SearchMetrics、MoveRecord、SolveStatus、Optimality、TerminationReason、MetricsScope、SolutionTape、SceneSnapshot、TapeConstants、AdapterBundle、SolveRequest
            - LevelForge.Kernel.Hosting：ComplianceReport、ComplianceResult、ComplianceCheckKind、AdapterHosting、HostingCheckInput、StatePairSample
            - LevelForge.Kernel.Analysis：DifficultyAssessment、DifficultyFeatures、DifficultyModel、FeatureExtractor、DifficultyWeights
            - LevelForge.Kernel.Replay：ReplayEngine、TapeMetadata、TapeJson
            - LevelForge.Kernel.Search：BfsSolver（如用平台搜索）

            {{AdapterGenContractReference.Build()}}

            # 求解装配骨架（照此实现；平台按此验收，不是随性发挥）
            ```
            var level = <把 command.LevelJson 解析成你的 TLevel>;             // 非法数据抛 ArgumentException
            var adapter = new <你的适配器>();                                  // 实现 IGameAdapter<TLevel,TState,TMove>
            var bundle = new AdapterBundle<TLevel,TState,TMove>("{{gameId}}", "1.0.0", adapter,
                <executionProtocol>);                                          // 执行协议：适配器同时实现 IExecutionProtocol 时可传同一个实例，否则传第二个实例
            var result = <用 BfsSolver 或你自己的确定性搜索，返回内核 SolveResult>;
            // 结论必须按内核五维度表达：status/optimality/graphComplete/terminationReason/metricsScope；
            // 预算耗尽 = ResourceLimit（未知），只有完整遍历/穷尽才可 UnsolvableProven；不要伪造 Solved。
            SolutionTape? tape = result.Status == SolveStatus.Solved
                ? new ReplayEngine().RecordTape(bundle, level, command.LevelFileBytes, result,
                    new TapeMetadata(command.LevelRef, Registration.AdapterHash, RunConfig.HashOf(command)))
                : null;                                                       // 无解/未知不要造磁带
            var difficulty = DifficultyModel.Evaluate(
                FeatureExtractor.Extract(bundle, result), result.Status, <你的 DifficultyWeights>);
            return new PlatformSolve(result, tape, difficulty, RunConfig.HashOf(command));
            ```
            - 播放/试玩 `Play` 必须与 Solve 用同一份适配器：从初态按 move 序列重放，非法动作抛 PlayException
            - `DescribeMove` 只做展示；`EncodeMove/DecodeMove` 才进磁带

            # 门1 装配骨架（RunGate1Checks 必须真跑平台检查，不准直接 return 全绿）
            ```
            var initial = adapter.LoadLevel(<代表关>);
            var keySamples = new StatePairSample<TState>[] {
                new("位置", initial, <仅位置不同的状态>),
                new("朝向", initial, <仅朝向不同的状态>),
            };                                                               // 至少覆盖规则矩阵声明的维度
            var legal = adapter.GetLegalMoves(initial);                      // 不能为空
            var input = new HostingCheckInput<TLevel,TState,TMove>(
                bundle, typeof(<你的适配器>).Assembly, keySamples,
                (initial, legal[0]), <代表性可解关卡>, <该关卡 UTF-8 字节>);
            return new AdapterHosting().RunAll(input);
            ```
            - 依赖扫描会拒绝任何 UnityEngine/UnityEditor/Cocos 等引擎引用；适配层禁止文件 IO/网络/时间/全局可变状态
            - 状态键判别：逻辑相等 ⇔ 键相等，必须覆盖全部玩法维度（位置/朝向/类型/库存/剩余次数…）

            # 诚实语义（硬约束）
            - 非法关卡数据必须返回错误/抛异常，禁止静默修复；求解未证明不冒充结论
            - 求解策略：优先实现基于关卡数据的确定性搜索（如广度优先/状态去重）；确定做不到时返回 ResourceLimit 并如实说明，不要伪造 Solved
            - 未知机制不要静默简化：在 LoadLevel/FormatErrors 中明确拒绝或标注 TODO-CONFIRM，并在注释中说明需要人工核对

            # 输出前自检（逐条确认再输出）
            1. 所有 using 已按上方清单写出；GameId 一字不差；Registration 所有参数齐全；
            2. RunGate1Checks 返回 AdapterHosting.RunAll 的真实报告（样本不为空）；
            3. Solve 有解必带磁带（ReplayEngine.RecordTape），无解/未知 Tape=null；
            4. Solve/ApplyMove/Snapshot 纯函数，不修改入参，不读外部状态；
            5. 没有文件/网络/时间/静态可变状态；没有引擎依赖；
            6. 只输出 FILE 标记块，不输出解释文字或 markdown 围栏。
            """;
    }
}
