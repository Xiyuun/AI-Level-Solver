# LevelForge 实现接续文档（PROGRESS）

> 用途：本文件供**新的 AI 会话/协作者**在本仓库上无缝接续实现工作。它承载"只存在于原对话中"的工作记忆：进度、计划、决策与约定。
> 配套阅读：上级目录 `../` 的 14 份设计文档（v0.2 修订版，方案书.md 为总纲）——**目录拷贝时必须连上级目录一起拷**（拷整个 `ai-glm` 文件夹，保持相对结构）。
> 最后更新：第 7 步完成后（阶段 A 收口）。

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
| **8** | **平台服务骨架与SQLite数据模型** | **▶ 下一步** | |
| 9 | 生成流水线（LLM出题→校验→判卷→重生） | 待做 | |
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

验收口径：每步完成后 `bash build.sh`（或 `build.cmd`）构建+全部测试通过，然后单独 commit。当前基线：**109 测试全绿**（78 内核 + 31 玩具）。

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
src/LevelForge.Platform/            空模板（dotnet new web），第8步起建
tests/LevelForge.Kernel.Tests/      内核自测（含 TestDoubles：SyntheticGraphAdapter 无状态图适配器，
                                    SyntheticFailure 枚举注入故障；PurityAdapter 带三种纯度缺陷+分类器）
tests/LevelForge.Adapters.SokobanToy.Tests/
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

## 七、第 8 步设计要点（下一步开工即用）

Platform（ASP.NET Core minimal API + Microsoft.Data.Sqlite）：
- **数据模型（SQLite 表）**：games 玩法注册表（id/显示名/状态[生产中|试用|接入中]/适配层版本×内核版本/关卡根目录/视觉映射JSON/机制表）；levels（gameId/levelId/levelHash/原生内容/来源[内置|导入|生成采纳]）；solve_runs（levelHash/adapterHash/kernelVer/contractVer/runConfigHash/五维结论+metrics/时间）；tapes 落文件+索引（trajectoryHash/artifactHash）；verification_records（门类型/结论/全部指纹字段/样本清单/时间——按《验证门》§2.1 失效矩阵）；baselines（LevelBaseline 全字段含 executionConfirmationRef/approvedBy/approvedAt）；generation_runs+候选记录（提示词版本/失败分类/耗时）；execution_confirmations（状态机 未执行/执行中/通过/失败/已失效）；settings（LLM地址/key明文本地/默认值）；phrases（按玩法分组）；onboarding_tasks。
- **API 骨架**：玩法列表/切换、求解+质检入口、设置读写+测试连接、磁带获取。门1/2/3 编排放第11步。
- **注册玩具**：启动时种子注册 sokoban-toy（内置参考状态、门2不适用标记）+ 导入 10 关。
- JSON 序列化沿用 TapeJson.Options 风格（camelCase+小写枚举）。
- 注意：SQLite 包需经 nuget.config 镜像还原；表结构首次启动自动建。

第 9~11 步要点分别在《生成端出题分析》《表现层_生成工作台§4.1/§4.2》《验证门》有逐条依据，开工时对照实现即可。
