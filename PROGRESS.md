# LevelForge 实现接续文档（PROGRESS）

> 用途：本文件供**新的 AI 会话/协作者**在本仓库上无缝接续实现工作。它承载"只存在于原对话中"的工作记忆：进度、计划、决策与约定。
> 配套阅读：上级目录 `../` 的 14 份设计文档（v0.2 修订版，方案书.md 为总纲）——**目录拷贝时必须连上级目录一起拷**（拷整个 `ai-glm` 文件夹，保持相对结构）。
> 最后更新：第 8 步完成后（阶段 B 进行中）。
> 范围决定（2026-10-31，用户拍板）：**本设备做到第 15 步为止**——第 16 步需滑动猪仓库/Unity 环境，当前设备没有；16~19 步待回到有猪项目的环境再继续。

## 一、项目一句话

LevelForge = "LLM 出题 + 确定性求解器验题"的关卡生产线 + 半天接入准入制度。实现依据是修订后 v0.2 的 14 份设计文档，**不改动任何设计文档**，按文档语义实现。

## 二、当前进度（19 步计划）

| 步 | 内容 | 状态 | commit |
|---|---|---|---|
| 1 | 仓库与工程基建 | ✅ | dfa6449 |
| 2 | 契约层与数据结构 | ✅ | 435e708 |
| 3 | BFS求解器+执行池+内核自测 | ✅ | b5f00a4 |
| 4 | 回放引擎与解法磁带 | ✅ | e607be9 |
| 5 | AdapterHosting五项合规检查 | ✅ | d49947e |
| 6 | 玩具推箱子适配层（含10关） | ✅ | a7653bd |
| 7 | 难度特征提取与固定权重模型 | ✅ | 3c92d41 |
| 8 | 平台服务骨架与SQLite数据模型 | ✅ | d469d04 |
| **9** | **生成流水线（LLM出题→校验→判卷→重生）** | **▶ 下一步** | |
| 10 | 质检模式+安全落盘+执行确认 | 待做 | |
| 11 | 三道门编排（门1/门2对拍/门3基准diff+指纹失效） | 待做 | |
| 12 | 前端骨架+SVG通用渲染器+顶栏玩法切换 | 待做 | |
| 13 | 生成工作台页面 | 待做 | |
| 14 | 接入向导（薄页面版）+设置页 | 待做 | |
| 15 | 接入套件skill九件套 | 待做 | |
| 16 | 滑动猪真实接入（适配层+rule-matrix+harness+三门） | 待做（需用户提供滑动猪仓库/Unity环境） | |
| 17 | 故障注入实验与演示证据 | 待做 | |
| 18 | 批量生产（P1） | 待做 | |
| 19 | 自包含打包与发布 | 待做 | |

验收口径：每步完成后 `bash build.sh`（或 `build.cmd`）构建+全部测试通过，然后单独 commit。当前基线：**133 测试全绿**（78 内核 + 31 玩具 + 24 平台）。

## 三、冻结的技术决策（不再讨论，直接执行）

| 决策 | 选择 |
|---|---|
| 后端+内核 | C# / .NET 10，ASP.NET Core minimal API 单进程（SDK 10.0.201，解决方案为 .slnx 新格式） |
| 前端 | TypeScript + Vite + React，渲染器 SVG（第12步初始化 web/） |
| 存储 | SQLite + 本地文件（磁带/关卡包/报告落文件）；数据目录 `data/` 已 gitignore |
| 适配器装载 | **静态注册、随平台构建编译**；"产物上传"= 文件落进 `src/Adapters/` + MANIFEST 登记 |
| 门2执行 | 子进程+文件交换（Unity batchmode），驱动配置声明命令，平台不装引擎；**不用 MCP** |
| 向导 UI | 薄页面版六步向导（登记表单/报告解析/产物验收+门1运行卡/简化审核清单/门2分诊/门3diff/上线注册），不做三栏代码对照 |
| 单人维护约束 | 零外部服务依赖（除 LLM API）、本地存储、无账号、单进程常驻+可按需启动子进程 |
| 仓库 | 本目录即 git 仓库；注意：**上级目录另有一个空的 .git**（误建），内层仓库优先，不要动上级的 |

## 四、代码地图

```
src/LevelForge.Kernel/            内核（零游戏知识）
  Contracts/   IGameAdapter(七成员) / IExecutionProtocol(动作编码+语义投影) / AdapterBundle
               SolveEnums(五维度枚举) / SolveModels(SolveRequest/Result/Metrics/MoveRecord)
               SceneSnapshot / SolutionTape / TapeConstants
  Search/      ISearchStrategy / BfsSolver（早停+完整枚举，诚实结论：预算耗尽=ResourceLimit 绝不报无解；
               完整枚举中已得解被预算打断保留 Solved）
  Replay/      ReplayEngine（录制磁带，结局由实际执行重判）/ TapeJson（camelCase+小写枚举）
  Executor/    SolveExecutor（单关+批量，固定并发窗口，单项异常不中断整批）
  Hosting/     DependencyScanner / ComplianceChecks(键判别+纯函数+确定性) / KernelSelfTest /
               AdapterHosting.RunAll → ComplianceReport（门1机器部分，AllPassed 零豁免）
  Analysis/    FeatureExtractor（ToolMoves 缺失=null 不填0）/ DifficultyModel（固定权重+阈值，结论带版本；
               无解/未知/超时不评分）
  Hashing/     CanonicalJson（键递归排序+实体按id排序+普通数组保序）/ TapeHashes（四类摘要独立用途）
src/Adapters/LevelForge.Adapters.SokobanToy/   玩具推箱子：SokobanToyAdapter(七成员+协议) /
               SokobanToyValidator(静态布局校验，生成端复用) / SokobanToyContent(10关+五件接入产物+默认难度权重)
src/Adapters/LevelForge.Adapters.SlidePig/     空壳，第16步产出
src/LevelForge.Platform/            平台（ASP.NET Core minimal API + Microsoft.Data.Sqlite）
  Facades/    GameRegistry 静态注册表 / IGameFacade 泛型适配层的平台门面 /
              SokobanToyFacade（解析+求解+制带+难度一入口）/ AdapterFingerprint 程序集指纹
  Database/   AppDatabase（12张表 DDL+WAL+幂等建表）/ GameSeeder（权威列 upsert+内置关+默认设置+向导任务）/
              GameStore / SolveRunStore（五维结论落库）/ SettingsStore / PhraseStore
  Storage/    TapeStore（磁带落文件+索引，写盘失败回滚不留半截记录）
  Llm/        LlmClient（OpenAI 兼容 /chat/completions；目前仅测试连接，第9步扩展生成调用）
  Api/        GameEndpoints（列表/详情/切换/关卡）/ SolveEndpoints（存量关+质检粘贴同一入口、磁带获取）/
              SettingsEndpoints（读写+LLM测试连接）/ PhraseEndpoints（短语 CRUD）
tests/LevelForge.Kernel.Tests/      内核自测（含 TestDoubles：SyntheticGraphAdapter 无状态图适配器，
                                    SyntheticFailure 枚举注入故障；PurityAdapter 带三种纯度缺陷+分类器）
tests/LevelForge.Adapters.SokobanToy.Tests/
tests/LevelForge.Platform.Tests/    平台测试（PlatformFactory 临时数据目录隔离；stub 网络层测 LLM 连接）
web/ skills/ harness-templates/     占位，第12/15步建设
```

## 五、工作约定（新会话必须遵守）

1. **注释规范**：只用 `//`（禁 XML summary）；参数/字段注释写声明**同行末尾**；同模块用 `#region` 分组；函数与关键步骤有注释；中文注释。与仓库现有代码风格保持一致即可。
2. **每步一个 commit**，message 格式 `stepN: <内容摘要>（<测试数> tests）`；测试全绿才允许提交。
3. **故障注入反向验证习惯**：每建一道防线，写一个"故意注入对应缺陷→防线必须红"的测试（第5步的四个缺陷双即此模式）。
4. **诚实语义铁律**：未知不计作无解（ResourceLimit≠UnsolvableProven）、抽样不宣称等价、三视角（投影/快照/键）不可见的变异要注明覆盖边界。
5. 设计争议一律**按设计文档现行文字实现**（用户已决定不改文档）；文档内部冲突时按本文件"冻结决策"表裁决。

## 六、新设备环境准备

1. 安装 .NET SDK 10（10.0.201 或更新）、Node.js 24+（第12步起才需要）、Git。
2. 拷贝整个 `ai-glm` 文件夹（含 LevelForge_glm 与 14 份设计文档，保持相对结构）。
3. 仓库根执行 `bash build.sh`（Git Bash）或 `build.cmd`：首次还原走 nuget.config 里的华为云镜像；109 测试全绿即环境就绪。
4. 新 AI 会话开场白建议："读 LevelForge_glm/PROGRESS.md 与上级目录设计文档（可先只读方案书.md），从第 8 步继续"。

**当前设备（2026-10-31 起用）备注**：仓库位于 `D:\temp\AI Level Solver\AI-Level-Solver`，设计文档在同级的 `ai求解器报告_10.30` 目录；SDK 10.0.403 装**用户级** `%USERPROFILE%\.dotnet`（未动系统），bash 里跑 dotnet 需前缀 `PATH="$HOME/.dotnet:$PATH"`；Node 未装（第12步前装）；git 身份已写入仓库 .git/config（邓翔）；GitHub 凭据本机未配，push 前需走 PAT。

## 七、第 9 步设计要点（下一步开工即用）

生成流水线（《生成端出题分析》逐条依据）：
- **形态铁律**：单次 LLM API 调用出候选（非 agent），循环由流水线代码控制流驱动；LlmClient 扩展 ChatCompletion 方法（OpenAI 兼容 /chat/completions，30s 超时，API 失败与内容失败分开计数）。
- **提示词组装**：系统提示词（GameBrief 玩法规则书 + FormatSpec 格式要求）+ few-shot（同难度历史关 2~3 个随机抽样，避免同质化）+ 本次参数（目标难度/关卡编号+机制范围 MechanicsTable/特殊要求）+ 失败反馈（仅重试时）。提示词模板版本化管理（prompt_version 落库）。
- **判卷链**：格式校验（SokobanToyValidator，校验失败带错误自动修复重试上限 2）→ 求解（预算=单候选超时，ResourceLimit 诚实分类）→ 难度评估（固定权重，不达标回填偏差）→ 约束检查（玩具无可机检约束时空实现并记录"无约束"，语义约束由策划预览判断——不虚构机检能力）。
- **失败分类回填**（generation_candidates.failure_class）：format_invalid / unsolvable / resource_limit / difficulty_miss / constraint_violation → 转自然语言修正指令重生；重生上限默认 3（settings gen.candidateLimit）；候选耗尽=运行失败（不阻塞其他关）。
- **落库**：generation_runs（参数快照/状态/失败汇总/耗时）+ generation_candidates（逐候选内容/分类/难度/solve_run_id 引用）——第8步表已建好，直接用。
- **API**：POST /api/generate 同步跑完整轮返回全部候选记录 + GET /api/generation/runs/{id}；流水线实时日志推送给前端留第13步（SSE/轮询届时定）。
- 测试：stub Llm 应答（好候选/坏JSON/无解/难度不达标四种脚本）驱动整条重生循环，断言分类与重试次数、落库完整；无解反馈只给已验证事实。

## 八、已知坑与经验（原对话过程记录，避免新会话重踩）

1. **NuGet 直连不稳**：nuget.org 从本网络下载会 "EOF from transport stream"，仓库 nuget.config 已配华为云镜像优先；测试类包首次还原慢（分钟级），成功后进全局缓存就快了。
2. **本机 Git 是 2.21（2019 年）**：不支持 `git init -b`；credential manager 老，GitHub 推送用 PAT（用户已走 PAT 流程，凭据已缓存）。
3. **上级目录有个误建的空 .git**：LevelForge_glm 内层仓库优先级更高，不影响操作，别去动它。
4. **ZCode 的 bash 偶发把命令和 shell 钩子串线报 exit 127**：原样重跑一遍命令即可，不是仓库问题。**新设备变体（必躲）**：命令行里含中文字符（路径/名字）时 exit 127 **必现且重跑无效**——用通配符路径、Write/Edit 文件方式绕开，绝不在 bash 命令里直接写中文。
5. **改文件用编辑工具整体重写，别用 sed 改代码文件**（曾把测试文件改坏过一次）。
6. **SDK 10 的 JsonNode 没有 DeepCopy 扩展**：用 CanonicalJson.Canonicalize 代替（它本来就产出独立副本）。
7. **曾由测试抓出的 bug**（保持这种"测试先红"的敏感度）：校验器在非矩形网格上越界（已加 ragged 守卫）；toy-005 关卡数据笔误目标数少 1。
8. **用户协作偏好**：授权全权实现、逐步推进，不需事事确认；设计文档一律不改；时间由用户灵活安排（额度可能随时中断——所以每步必须停在可提交边界）。
9. **PROGRESS.md 维护规则**：每完成一步 commit 时，同步更新第二节进度表与第七节"下一步要点"，然后 push。

## 九、新会话开场白（复制即用）

> 读 D:\...\LevelForge_glm\PROGRESS.md（重点是二、三、五、七、八节），再读上级目录的《方案书.md》。当前阶段 B 第 8 步起。遵守第五节工作约定，从第 8 步继续，每步测试全绿后 commit 并更新 PROGRESS.md。
