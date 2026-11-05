# LevelForge 实现接续文档（PROGRESS）

> 用途：本文件供**新的 AI 会话/协作者**在本仓库上无缝接续实现工作。它承载"只存在于原对话中"的工作记忆：进度、计划、决策与约定。
> 配套阅读：上级目录的 14 份设计文档（v0.2 修订版，方案书.md 为总纲）——设计文档已定稿，一律不改，按文档语义实现。
> 最后更新：2026-11-04（19 步实现计划全部完成；优化轮 M0~M4 见第十节；**三轮·阶段 B 已落地**：项目导入 + 移除内置滑动猪，见 10.5）。
> 范围：第 0~19 步已全部完成 ✅（第 16 步的滑动猪真实接入在公司机完成并已转正 production；2026-11-04 晚已按三轮计划移出内置注册表，源码保留作 piggyfarm 适配层参考）。

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
| 9 | 生成流水线（LLM出题→校验→判卷→重生） | ✅ | 4da396f |
| 10 | 质检模式+安全落盘+执行确认 | ✅ | a7a605f |
| 11 | 三道门编排（门1/门2对拍/门3基准diff+指纹失效） | ✅ | d49677c |
| 12 | 前端骨架+SVG通用渲染器+顶栏玩法切换 | ✅ | （step12 提交） |
| 13 | 生成工作台页面 | ✅ | （step13 提交） |
| 14 | 接入向导（薄页面版）+设置页 | ✅ | （step14 提交） |
| 15 | 接入套件skill九件套 | ✅ | （step15 提交） |
| **16** | **滑动猪真实接入 ✅ 全部完成（2026-11-03 转正）**：适配层 8 类型+22 测试；门1 全绿；门2 真机对拍 **17/20 全绿**（1 关物理停格回退=已查明模型边界，规则矩阵附证据；2 关制带口径差异已对齐）；门3 基准建立（75 关：31 solved + 44 诚实未知，0 红 0 黄）；75 关导入；④人工审核通过（rule-matrix 15 条+5 口径无异议，2026-11-03）；skill reference 内部版回填；转正 production | **✅** | |
| 17 | 故障注入实验与演示证据 | ✅ | 4398d70 / 96db34a |
| 18 | 批量生产（P1，后端） | ✅ | 7731f46 |
| 19 | 自包含打包与发布 | ✅ | 570aa0e |

验收口径：每步完成后 `bash build.sh`（或 `build.cmd`）构建+全部测试通过（含前端 vitest），然后单独 commit。当前基线：**316 测试全绿**（98 内核 + 31 玩具 + 104 平台 + 26 滑动猪 + 57 前端）。

## 三、冻结的技术决策（不再讨论，直接执行）

| 决策 | 选择 |
|---|---|
| 后端+内核 | C# / .NET 10，ASP.NET Core minimal API 单进程（新设备 SDK 10.0.403 用户级安装；SDK 10.0.201 起均可） |
| 前端 | TypeScript + Vite + React，渲染器 SVG（第12步初始化 web/） |
| 存储 | SQLite + 本地文件（磁带/关卡包/报告落文件）；数据目录 `data/` 已 gitignore |
| 适配器装载 | **静态注册、随平台构建编译**；"产物上传"= 文件落进 `src/Adapters/` + MANIFEST 登记 |
| 门2执行 | 子进程+文件交换（Unity batchmode），驱动配置声明命令，平台不装引擎；**不用 MCP** |
| 向导 UI | 薄页面版六步向导（登记表单/报告解析/产物验收+门1运行卡/简化审核清单/门2分诊/门3diff/上线注册），不做三栏代码对照 |
| 单人维护约束 | 零外部服务依赖（除 LLM API）、本地存储、无账号、单进程常驻+可按需启动子进程 |
| 仓库 | 本目录即 git 仓库；仓库级 .git/config 已写身份（邓翔） |

## 四、代码地图

```
src/LevelForge.Kernel/            内核（零游戏知识）
  Contracts/   IGameAdapter(七成员) / IExecutionProtocol(动作编码+语义投影) / AdapterBundle
               SolveEnums(五维度枚举) / SolveModels(SolveRequest/Result/Metrics/MoveRecord)
               SceneSnapshot / SolutionTape / TapeConstants
  Search/      ISearchStrategy / BfsSolver（早停+完整枚举，诚实结论：预算耗尽=ResourceLimit 绝不报无解）
  Replay/      ReplayEngine（录制磁带，结局由实际执行重判）/ TapeJson（camelCase+小写枚举）
  Executor/    SolveExecutor（单关+批量，固定并发窗口，单项异常不中断整批）
  Hosting/     DependencyScanner / ComplianceChecks(键判别+纯函数+确定性) / KernelSelfTest /
               AdapterHosting.RunAll → ComplianceReport（门1机器部分，AllPassed 零豁免）
  Analysis/    FeatureExtractor（ToolMoves 缺失=null 不填0）/ DifficultyModel（固定权重+阈值，结论带版本）
  Hashing/     CanonicalJson（键递归排序+实体按id排序+普通数组保序）/ TapeHashes（四类摘要独立用途）
src/Adapters/LevelForge.Adapters.SokobanToy/   玩具推箱子：SokobanToyAdapter(七成员+协议) /
               SokobanToyValidator(静态布局校验+ValidateJson 生成端入口，生成端复用) /
               SokobanToyContent(10关+五件接入产物+默认难度权重)
src/Adapters/LevelForge.Adapters.SlidePig/     空壳，第16步产出
src/LevelForge.Platform/            平台（ASP.NET Core minimal API + Microsoft.Data.Sqlite）
  Facades/    GameRegistry 静态注册表 / IGameFacade 泛型适配层的平台门面（FormatErrors 静态校验）/
              SokobanToyFacade（解析+求解+制带+难度一入口）/ AdapterFingerprint 程序集指纹
  Database/   AppDatabase（12张表 DDL+WAL+幂等建表；candidates.content_json 可空）/
              GameSeeder（权威列 upsert+内置关+默认设置+向导任务）/ GameStore / SolveRunStore（五维结论落库）/
              SettingsStore / PhraseStore / GenerationStore（运行+候选观测落库）
  Storage/    TapeStore（磁带落文件+索引，写盘失败回滚不留半截记录）
  Llm/        LlmClient（OpenAI 兼容 /chat/completions：测试连接+聊天补全，30s 超时，宽松编码）/
              LlmJson（从模型输出宽容提取 JSON 原文，不修复内容）
  Generation/ GenerationPipeline（出题→格式修复≤2→求解判卷→难度评估→分类回填重生；API失败与内容失败分开）/
              PromptBuilder（系统提示词=规则书+格式说明书；few-shot+参数+失败反馈；版本 v1）/
              FewShotSelector（按同难度档采样2~3个，进程内缓存）/ GenerationModels（失败分类+反馈文案：只给已验证事实）
  Adoption/   AdoptionService（落盘→幂等短路→模型验证→levels登记→旧确认失效→待确认登记；文件核对 ok/missing/hashChanged）
  Gates/      GateService（三门编排：门1五项检查运行卡 / 门2子进程对拍 / 门3基准diff）/
              Gate2Models（驱动配置占位符 {levelFile}{tapeFile}{outputFile}；HarnessReport 协议；
              Gate2Comparator 逐步差分：首分歧步/第0步/关卡错位/步数不一致）
  Storage/    LevelFileStore（安全落盘六条流程：冻结产物/限定目录/冲突旧指纹复查/临时文件原子发布/最终核对/
              同编号写串行化）+ ILevelFileIO 接缝（故障注入测试用）+ TapeStore（+FindByRun）
  Database/   ConfirmationStore（五态状态机：pending/running/passed/failed/invalidated + 人工降级/报告导入）/
              BaselineStore（版本维度唯一，幂等追加，不静默覆盖）/ VerificationRecordStore（三门结论+指纹字段）
  Api/        GameEndpoints（列表/详情/切换/关卡）/ SolveEndpoints（存量关+质检粘贴同一入口、磁带获取）/
              SettingsEndpoints（读写+LLM测试连接）/ PhraseEndpoints（短语 CRUD）/
              GenerationEndpoints（POST /api/generate 同步整轮 + 运行历史查询）/
              AdoptionEndpoints（adopt/确认列表/人工结论/报告导入/入基准/基准列表）/
              GateEndpoints（三门运行 + 验证记录查询）
tests/LevelForge.Kernel.Tests/      内核自测（含 TestDoubles：SyntheticGraphAdapter 无状态图适配器，
                                    SyntheticFailure 枚举注入故障；PurityAdapter 带三种纯度缺陷+分类器）
tests/LevelForge.Adapters.SokobanToy.Tests/
tests/LevelForge.Platform.Tests/    平台测试（PlatformFactory 临时数据目录隔离；stub LLM 脚本驱动生成流水线）
web/                                前端（Vite+React+TS，npmmirror 镜像）
  src/renderer/  model.ts(类型镜像) / project.ts(投影纯函数核心：快照×映射→画面项，灰兜底+告警) /
                 tween.ts(同id插值/淡入淡出/frameAt 进度接口) / BoardSvg.tsx(SVG薄层：4形状/图标白名单/
                 arrow·badge·tint props/明暗主题/实体点击回调) + vitest 24 测试
  src/api/client.ts  平台 API 薄封装（camelCase 类型与后端一致）
  src/App.tsx        骨架页：顶栏玩法切换 + 关卡预览纵向切片（求解→磁带→渲染→播放/单步/重置）
skills/onboarding-skill/            接入套件 skill 九件套（详设逐条蓝本：README 行为守则/资格四问/
                                    七步工作流/契约正文/对拍规范/13条清单/产物清单/测试骨架/参考指向）
harness-templates/                  对拍 harness 模板：unity-batchmode 骨架（REVIEW 标注点）+ 人工对拍清单
```

## 五、工作约定（新会话必须遵守）

1. **注释规范**：只用 `//`（禁 XML summary）；参数/字段注释写声明**同行末尾**；同模块用 `#region` 分组；函数与关键步骤有注释；中文注释。与仓库现有代码风格保持一致即可。
2. **每步一个 commit**，message 格式 `stepN: <内容摘要>（<测试数> tests）`；测试全绿才允许提交。
3. **故障注入反向验证习惯**：每建一道防线，写一个"故意注入对应缺陷→防线必须红"的测试（第5步的四个缺陷双、第9步的 schema NOT NULL 缺陷均由此抓出）。
4. **诚实语义铁律**：未知不计作无解（ResourceLimit≠UnsolvableProven）、抽样不宣称等价、三视角（投影/快照/键）不可见的变异要注明覆盖边界。
5. 设计争议一律**按设计文档现行文字实现**（用户已决定不改文档）；文档内部冲突时按本文件"冻结决策"表裁决。

## 六、新设备环境准备

1. .NET SDK 10：已装 **10.0.403 用户级** `%USERPROFILE%\.dotnet`；Node.js：已装 **24.22.2 用户级** `%USERPROFILE%\nodejs`——build.sh/build.cmd 已自动把它们加进 PATH，直接跑即可。
2. Git：仓库级 .git/config 已写身份（邓翔）；GitHub 凭据已缓存。
3. 仓库根执行 `bash build.sh`：227 测试全绿（78 内核 + 31 玩具 + 75 平台 + 43 前端）即环境就绪。
4. 前端开发：`cd web && npm run dev`（Vite 5173，代理 /api → localhost:5000）；平台 `dotnet run --project src/LevelForge.Platform --urls http://localhost:5000`。

## 七、第 16 步当前状态（2026-11-03，公司机推进）

**★ 门2 冒烟对拍全绿（2026-11-02）**：PiggyFarm 1 号关在 Unity batchmode 下完成首次真机对拍——
初始+全部 6 步语义哈希逐步一致、结局 win（游戏自身判定）。适配层网格模型 ↔ 真实游戏规则等价性经真机裁决通过。
harness 三处同步：`harness-templates/unity-batchmode/PairingHarness.cs`（模板，含实战记录）、
`piggyfarm/Assets/Editor/LevelForgeHarness/`（原项目）、`_gate2copy`（测试副本，因原项目常开编辑器）。

**★ 门2 批量对拍：17/20 全绿（2026-11-03）**：20 关抽样（覆盖全部机制与规模梯度），初始+逐步语义哈希
全一致且真实 win 的 17 关；2 关制带口径差异（DumpTape 阶梯与普查不一致，对齐即可）；1 关（64）为
**已查明的模型边界**：游戏"贴靠格被占向后回退"兜底（PigManager.TryFindFreeStopFootprint，物理停格与
占用表粒度不一致时触发，游戏日志直接抓到"猪 69 向后调整 4 格"）无法在纯网格模型忠实复现——
规则矩阵假设③已据此修订并附证据。诊断过程沉淀了单步/二分/确定性三件实验方法与 DiffStep/DiffAdjacent
等勘查脚本（probe/）。harness 落定等待已补"铁笼回收完毕"（56/62/110 三关由此转绿）。

**★ 求解覆盖大幅提升**：贪心 DFS 加"机动性平级排序+腾挪预算阶梯(8/16/32)"后，普查 110 关：31/75 可解
（改进前 1/75）、44 诚实未知、35 子集外。91 号关 11 秒出 62 步解；91 猪的 110 号关 20 秒 122 步解——
卡点是结构陷阱而非规模。门面已接线（稀疏 A*/稠密阶梯，按 40 实体分界）。

**已完成清单**：资格评估（v1 子集 75/110 关）、适配层全套+22 测试、A*+贪心DFS 双策略+内核测试组、
SlidePigFacade（试用态）、关卡导入器+API、探针五件（Probe/Bench/Survey/Greedy/Tune/DumpTape/CompareTape）、
harness 实战化（含 SessionState 域重载续跑、规范化语义哈希对齐）、全套 264 测试绿。

**剩余工作（下一步开工清单）**：
1. **门2 批量对拍**：从 31 个可解关抽 20 关 → DumpTape 批量出磁带 → _gate2copy 逐关 batchmode → CompareTape 汇总。
   注意：对拍跑 _gate2copy（原项目被编辑器占用）；磁带求解用门面口径（稠密关阶梯）。
2. 门3 基准：可解关求解建基准（未知关记"未知"观察值）；导入 110 关入平台库（import-levels API）。
3. 用户④人工审核 13 条清单（rule-matrix 确认人栏；无道具可解已确认✅）→ 转正（trial→production）。
4. 回填 skill reference 内部版一节（SlidePig 兼任标准答案，harness 实战记录引用）。
- 之后：17 故障注入实验与演示证据（方案书 §10）→ 18 批量 P1 → 19 打包发布。

## 八、已知坑与经验（原对话过程记录，避免新会话重踩）

1. **NuGet 直连不稳**：nuget.org 从本网络下载会 "EOF from transport stream"，仓库 nuget.config 已配华为云镜像优先；测试类包首次还原慢（分钟级），成功后进全局缓存就快了。
2. **本机 Git 是 2.21（2019 年）**：不支持 `git init -b`；credential manager 老，GitHub 推送用 PAT（本设备已由用户手动 push 完成凭据缓存）。
3. **上级目录有个误建的空 .git**（旧布局）：内层仓库优先，不要动它。
4. **ZCode 的 bash 偶发把命令和 shell 钩子串线报 exit 127**：原样重跑一遍命令即可，不是仓库问题。**新设备变体（必躲）**：命令行里含中文字符（路径/名字/grep 中文模式）时 exit 127 **必现且重跑无效**——用通配符路径、Read/Edit 文件方式绕开，绝不在 bash 命令里直接写中文。
4b. **.cmd/.bat 批处理文件必须纯 ASCII + CRLF**（run.cmd 踩过）：cmd.exe 按 ANSI(GBK) 解析批处理，编辑工具写出的 UTF-8 中文会被当乱码命令逐行执行；且 Write 工具默认 LF 行尾，批处理要求 CRLF——中文 + LF 叠加会让 `echo`/`rem` 行被当成命令跑。修法：英文注释/提示 + 转换 CRLF（PowerShell 脚本 ReadAllText/WriteAllText ASCII）。
5. **改文件用编辑工具整体重写，别用 sed 改代码文件**（曾把测试文件改坏过一次）。PROGRESS.md 若 Edit 匹配失败（CRLF 差异），直接 Write 整体重写。
6. **SDK 10 的 JsonNode 没有 DeepCopy 扩展**：用 CanonicalJson.Canonicalize 代替（它本来就产出独立副本）。
7. **曾由测试抓出的 bug**（保持这种"测试先红"的敏感度）：校验器在非矩形网格上越界（已加 ragged 守卫）；toy-005 关卡数据笔误目标数少 1；generation_candidates.content_json 误设 NOT NULL（api_error/无JSON 候选没有内容——第9步抓出）。
8. **用户协作偏好**：授权全权实现、逐步推进，不需事事确认；设计文档一律不改；时间由用户灵活安排（额度可能随时中断——所以每步必须停在可提交边界）。
9. **PROGRESS.md 维护规则**：每完成一步 commit 时，同步更新第二节进度表与第七节"下一步要点"，然后 push。
10. **用户级旧 SDK 遮蔽系统 SDK**（公司机踩过）：`~/.dotnet` 若装有旧版 SDK（如 8.0.403），build/run 脚本无条件把它提到 PATH 最前会导致 MSB4068（8.0 不识别 .slnx/net10.0）。四个脚本已改为**仅当用户级 dotnet 存在 10.x SDK 时才优先**，否则回落系统 dotnet——两台设备（家：用户级 10.0.403；公司：系统 10.0.201 + 用户级 8.0.403）均验证通过。
11. **C# 多行原始字符串**：起始 `"""` 后必须**直接换行**，内容另起一行（`"""{...` 同行跟内容=语法错 CS1513 连锁报一大片）；单行内容才能同行闭合。踩过两次。
12. **游戏项目侧事实**（PiggyFarm）：关卡 JSON 是 Luban 配置（`Luban~/Datas/Levels/`，110 个）；铁笼可与猪**叠放**（笼内猪被锁）；游戏死局会走复活/道具流程（无道具可解性已由开发者确认保证✅）。
13. **Unity Editor batchmode + PlayMode 陷阱**（harness 实战三坑）：①进 Play 触发 domain reload，静态字段与事件订阅全部重置——参数/状态必须存 `SessionState`（跨 reload 存活），用 `[InitializeOnLoadMethod]` 自动续跑；②Unity 2022 是 netstandard2.1 档——`Convert.ToHexString`/`SHA256.HashData`/`File.*Async` 部分 .NET5+ API 不可用，用 BitConverter+ComputeHash 替代；③对拍双方哈希必须同一规范化口径（平台 CanonicalJson 键字母序+schemaVer 载荷），harness 直接按规范化形态输出 JSON 再哈希。
14. **原项目被编辑器占用时 batchmode 打不开**（HandleProjectAlreadyOpenInAnotherInstance）：复制测试副本（robocopy 排除 Library/Temp/Logs；注意 Git Bash 下 robocopy 参数要 `MSYS_NO_PATHCONV=1` 防路径转换，含中文目录名的 /XD 项会触发坑 4b 直接去掉）；对拍统一跑副本。
15. **`dotnet run file.cs` 文件式应用禁用反射序列化**（JsonSerializerIsReflectionDisabled）：`#:property` 与 AppContext.SetSwitch 均无效——需制带/序列化的探针一律建正式 console 工程（probe/DumpTape 模式）。

## 九、新会话开场白（复制即用）

> 读仓库根 PROGRESS.md（重点二、三、五、七、八、十节），再读上级目录的《方案书.md》。
> 19 步实现计划已完成；当前处于**优化轮**，总纲是上级目录的《前后端全流程优化.md》（进度看板 + 变更记录 + 待拍板决策 D1~D4）。
> 遵守第五节工作约定：设计文档不改、每步测试全绿再 commit、诚实语义六条不破；完成后同步更新 PROGRESS.md 与该文档的进度看板。

## 十、优化轮次（2026-11-04 起）

19 步实现计划完成后，用户反馈"生成几乎不可用、界面简陋、求解能力不足"，于是开了优化轮。
**总纲文档在上级目录：`../前后端全流程优化.md`**（进度看板与变更记录在那里维护，本文件不重复）。

### 10.1 五个里程碑与结果

| 里程碑 | 内容 | 结果 |
|---|---|---|
| M0 止血 | 生成链路：坐标制出题 + token 预算自适应 + JSON 模式 + 异步任务/SSE 进度 + 修指标造假 | 玩具关生成成功率 **11% → 80%**，耗时 45s → 11.3s，首帧反馈 225ms |
| M1 求解能力 | 新增 `BeamSearchSolver` + 门面三级阶梯（窄束→宽束→贪心，按剩余预算分配） | 滑动猪可解 **31 → 58/75**（77.3%，端到端实测） |
| M2 前端 | 设计令牌 + 亮暗主题、棋盘 viewBox 自适应、播放器倍速/重播、空态与骨架 | 修掉三个真 bug：主题失效、大棋盘撑爆 1152×1536、播放器缺控制 |
| M3 全链路 | 导入逐关三栏 + CSV、门3 四区 + 转正门禁（后端 409）、门1 五项卡片、LLM 供应商预设 | 未做：向导 stepper（收益低）、批量页（决策 D4 后置） |
| M4 收口 | 全量回归 + 端到端复测 + 打包 | 见 10.3 |

### 10.2 三条被数据证伪的假设（不要再走一遍）

1. **"逐猪最小点击数"启发式不可采纳**：一次点击让猪滑行任意多格（不是一格），"离边界几格"不是点击数下界。
   现有"剩余非铁笼实体数"已是该问题最紧的可采纳下界——**瓶颈在搜索组织方式，不在启发式精度**。
2. **加宽束搜索收益递减**：W=256 相对 W=64 只多解 1 关，却把每次尝试从 2s 拉到 8s。
3. **随机种子重启无效**：6 个种子跑 21 个硬关，0/21。

### 10.3 M4 端到端复测（2026-11-04）

不走探针，直接打真实 HTTP API（`POST /api/solve`，30s 预算，75 关全部导入）：

- 可解 **58/75（77.3%）**：比 M1 探针估的 54 更好（探针按 8s 掐断，门面在 30s 下把宽束档也跑满）→ 三级阶梯在真实链路生效
- 解出耗时中位数 **1497ms**；58 个解出关里 **47 个 ≤2s（81%）**；总耗时 10.9 分钟
- 剩余 21 关：30s 内探索 15~40 万状态仍无解；需要结构性新方法（依赖顺序规划/逆向搜索），不是调参

### 10.4 当前已知技术债

| 债务 | 现状 | 建议 |
|---|---|---|
| 21 个硬关不可解 | 宽束/重启/加预算均无效 | 结构性新搜索（依赖图 + 逆向规划） |
| 滑动猪生成仍不可用 | 90 头猪让 LLM 手写 JSON 本身不合理；token 上限已放宽到 8192 | 决策 D1：程序化兜底生成器 |
| 批量页缺失 | 后端 `BatchService` 完整，前端一行没有 | 决策 D4 拍板后 1~2 天 |
| 磁带体积 | 滑动猪单条 700KB~1.2MB，全量快照存储 | 分页接口或 diff 磁带（设计文档已列后置） |
| 门2 基准需重跑 | M1 换了搜索策略，旧基准的 trajectoryHash 会变 | 按验证门 §5.3 重跑门3 确认 |

### 10.5 三轮·阶段 B：项目导入 + 移除内置滑动猪（2026-11-04 晚，`0941c3b`）

用户期望主线定稿（①配置模型→②导入项目→③接入向导 AI 产适配层→④生成/求解），阶段 B 落地：

- **项目导入**（N10）：`POST /api/projects/import`——绝对路径 → Unity/Cocos 引擎识别（识别不了列出目录内容帮助对照）→ 目录名即项目名（slug 化 gameId）→ games 行 status=onboarding + 六步向导任务；同目录重复导入幂等、撞名 409。前端顶栏「＋导入项目」弹窗（路径输入 + 最近导入回填），导入成功直达该玩法接入向导。
- **facadeless 语义**（N11）：games v2 迁移（project_root/engine_type）；生成/求解/采纳/批量/试玩/关卡导入/门1 七类端点统一护栏——已导入未接入给 409 可操作提示，真未知仍 404；工作台对未接入玩法整页换成引导卡；向导③内置"适配层需求包"（可复制提示词，指向 skills/onboarding-skill + GameRegistry 登记指引）。
- **移除内置滑动猪**：GameRegistry 反注册 + 清库（75 关/78 求解/7 生成/33 磁带/2 短语）；`SlidePigFacade`+`Adapters.SlidePig` 源码保留——**piggyfarm 适配层的参考实现**（含 `77f6cf7` WIP：另一会话已实现 ILevelComposer 脚印推导，1 万头真猪验证，token 预算随之 8192→2048；该 WIP 曾使 ComposerTests 旧断言失败，本轮改为下限守卫修复）。
- 首轮全流程测试的适配层路径走**重编译+重启**（GameRegistry 冻结决策）；插件热加载（D7=A）见三轮 §十三 阶段 C 后续。
- 测试：后端 261→268（+7 导入/护栏用例）+ 前端 73，全绿；HTTP 全链路冒烟 8 项通过（导入/护栏/任务种子/404 口径）。

### 10.6 三轮·N12 完整版：接入向导项目化（2026-11-04 深夜，`4734535`）

用户实测判定向导为"死页面"（导入后内容瞬出、产物假绿、13 条清单写死、门2文案误导）。根因三层：冻结的"薄页面"设计（平台只存档不分析）+ 为已注册玩法写的验收逻辑套在导入玩法上 + slide-pig 时代文案焊死成前端常量。落地：

- **ProjectScanner**（真实扫描，不猜语义）：JSON 全量收集（跳引擎/构建目录、限深限量）→ 关卡目录候选按聚簇排序（numbered/levelPrefixed/mixed）→ 每候选抽最小 2 个文件解析顶层键 → 机器观察只述事实。真实 mahjong 项目验证：扫出 `assets/resources/LevelData/` 43 关（numbered）+ 真实关卡 schema（chessCount/mapWidth/chesses/obstacles/countdown…）。
- **v3 迁移** games.project_profile_json；导入即扫描；`POST /api/games/{id}/rescan` 重扫；`PUT /api/games/{id}/levels-root` 真回填；门2"不适用"可无驱动命令登记（导入默认未评估 → 用户显式确认）。
- **向导五步全部画像驱动**：①候选预填+真回填+适用性显式选择 ②机器事实表格+AI 判断存档（平台不冒充判断）③产物假绿整块隐藏（适配层产出后才验收）+需求包内嵌扫描事实 ④清单=模板+画像派生条目（◆标记）、可增删、按玩法持久化在 step4 detail_json。
- 测试：后端 268→271 + 前端 73→75 全绿。生成/求解/门1~3/采纳/批量经核对均为真实后端逻辑（有测试与库证据），向导是唯一静态区——本轮已消除。

### 10.7 三轮·D7 解冻落地：适配层插件热加载 + 面向策划向导（2026-11-05，`9a2782c`）

用户拍板解冻 D7（工具面向策划，接入流程不允许构建命令）。落地：

- **契约下沉**：新建 `LevelForge.FacadeContracts` 程序集（IGameFacade/GameRegistration/SolveCommand/坐标制接口迁入，命名空间零改动），插件引用它 + Kernel 编译，平台与插件共享同一份类型定义（类型同一性）。
- **PluginLoader**：隔离 AssemblyLoadContext（本地 DLL 优先解析、契约回退平台副本）、启动全量扫描 plugins/{gameId}/、定向 LoadForGame（绑定校验：插件声明 gameId 必须与导入玩法一致，否则拒绝注册）。
- **检测/上传端点**：POST plugins/upload（面向策划：页面上传 DLL，不要求会操作目录）、POST plugins/detect（装载 → 注册 → 权威列 upsert → 自动跑门1）；detect 后 adapterReady 即翻转，生成/求解/关卡导入全部就绪。
- **向导③三步式**：复制需求包 → 上传适配层文件 → 检测并验收（自动门1 卡片内显）；需求包模板改为「产出插件 DLL + 上传」模型（外部 AI 不改平台源码）。
- 测试：后端 274→277（装载/绑定拒绝/上传检测端点）+ 前端 75 全绿；浏览器端到端实测通过（导入→上传→检测→门1 绿→adapterReady 翻转）。
- 修复：插件根相对路径导致 AssemblyDependencyResolver 抛错（规范化绝对路径）；persistChecklist 闭包旧值覆盖新列表。
- D7=B（重编译+重启）保留为兜底说明；热卸载后置（升级 DLL 需重启一次，约 3 秒）。

### 10.8 三轮·D9 解冻落地：平台内适配层生成（双入口）（2026-11-05，`c1289e3`）

用户拍板解冻"平台内直接生成适配层代码"（双入口设计：路径 A 平台内自动 = 策划零依赖；路径 B 需求包+外部编程助手 = 质量上限）。落地：

- **AdapterGenRunner**：平台调设置页模型生成适配层 C# 源文件（FILE 标记切分格式，零转义膨胀——JSON 包代码在推理模型下被 8K 思考+截断卡死，已弃用）→ 平台 dotnet 编译 → 编译错误逐条回喂重生成（≤4 轮）→ 成功即拷入 plugins/ 热装载 → 自动跑门1；门1 未过也回喂。超时 180s 下限、调用超时计入轮次而非硬失败。
- **AdapterGenJobService**：后台任务 + 轮询快照（与生成任务同形态）；20 分钟硬上限。
- **端点**：POST /api/games/{id}/adapter-gen（已接入玩法 409 拒绝）、GET /api/adapter-gen/{jobId}。
- **向导③双入口**：路径 A 平台内自动（策划一个按钮闭环，含轮次/阶段进度）+ 路径 B 需求包→外部编程助手；两路产物过同一套验收（检测→门1→④⑤⑥）。
- **真实 deepseek 冒烟**：全链路（生成→编译→回喂→装载→门1→adapterReady）真实验证；deepseek-flash 4 轮均未过编译——**推理模型的输出预算被思考耗尽（实测思考 38K 字符）是关键瓶颈**，8192 token 预算下正文为空；提升到 16384 后能产出正文但完整适配层的首过编译仍超其能力。链路本身有 ScriptedLlm 全绿测试兜底（编译→回喂→门1→adapterReady 全流程可复现）。
- **诚实结论**：平台内路径的适配层质量当前受 deepseek-flash 能力上限约束——这正是保留双入口的理由（外部编程助手路径保上限）。随模型能力提升，平台内路径的效果会自动改善（同一套链路零改动）。
- 测试：后端 274→281（schema 聚合/分析端点/插件装载/适配层生成编译回喂）+ 前端 75 全绿。
- 测试数据已清理：库里只剩推箱子 + 用户 mahjong 项目。

