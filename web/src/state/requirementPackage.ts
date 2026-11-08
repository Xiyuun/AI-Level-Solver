// 向导③「路径 B」需求包（给外部编程助手）：纯函数生成，便于单测与回归
// 目标：让一个没有平台上下文的助手也能产出"能被平台装载 + 能真正求解"的插件
// 内容分五块：工具仓库与必读材料 / 契约与内核职责 / 项目事实 / 关卡数据核验 / 导入与验收口径
import type { ProjectAnalysis, ProjectProfile } from '../api/client'

export interface RequirementPackageInput {
  gameId: string
  displayName: string
  engineType: string | null
  projectRoot: string | null
  codeRoot: string | null
  profile: ProjectProfile | null
  analysis: ProjectAnalysis | null
  toolRepoRoot: string | null // 平台启动时自动写入 settings.system.toolRepoRoot（发布版可能为空）
}

// 常见工具配置 JSON 的顶层键：命中即提示"这不是关卡数据"
const TOOL_CONFIG_KEYS = ['runtimeOptions', 'compilerOptions', 'recommendations', 'configurations', 'include']

function isToolConfigKeys(keys: string | null | undefined): boolean {
  if (!keys) return false
  const list = keys.split(',')
  return TOOL_CONFIG_KEYS.some((k) => list.includes(k))
}

export function buildRequirementPackage(input: RequirementPackageInput): string {
  const { gameId, displayName, engineType, projectRoot, codeRoot, profile, analysis, toolRepoRoot } = input
  const lines: string[] = []
  const repo = toolRepoRoot?.trim() ?? ''

  lines.push(`请为 LevelForge 平台制作「${displayName}」的求解器适配层。`)
  lines.push('')

  // ---- 工具仓库与必读材料 ----
  lines.push('# 工具仓库（先读材料，再动手）')
  if (repo.length > 0) {
    lines.push(`- 仓库绝对路径：${repo}`)
    lines.push(`- 必读：${repo}/skills/onboarding-skill/ 下 README.md（行为守则）、onboarding.md（接入总纲）、contract.md（契约正文）、deliverables.md（产物清单）、qualification.md（资格评估格式）、checklist.md（审核清单）`)
    lines.push(`- 契约与内核源码：${repo}/src/LevelForge.FacadeContracts/、${repo}/src/LevelForge.Kernel/Contracts/、${repo}/src/LevelForge.Kernel/Replay/`)
    lines.push(`- 参考门面：${repo}/src/LevelForge.Platform/Facades/SokobanToyFacade.cs（最小示例）、SlidePigFacade.cs（实战）`)
  } else {
    lines.push('- **未配置工具仓库路径**：请让用户在平台启动源码版后重试（平台会自动写入 settings.system.toolRepoRoot）。没有仓库路径无法完成第 4 步的 ProjectReference，无法编译插件。')
  }
  lines.push('')

  // ---- 契约与内核职责 ----
  lines.push('# 契约与内核职责（必须使用平台类型，不要重写契约）')
  lines.push('平台消费的是 `IGameFacade`（命名空间 LevelForge.Platform.Facades），必须实现：')
  lines.push('- `GameRegistration Registration`、`IReadOnlyList<string> BuiltinLevels`、`string? TryGetLevelId(string levelJson)`、`IReadOnlyList<string> FormatErrors(string levelJson)`；')
  lines.push('- `PlatformSolve Solve(SolveCommand command)`；')
  lines.push('- `ComplianceReport RunGate1Checks()`；')
  lines.push('- `PlayState Play(string levelJson, IReadOnlyList<JsonNode> moves)`。')
  lines.push('')
  lines.push('Solve 的返回与内核职责（不是"可选参考"，是平台工作流依赖）：')
  lines.push('- `PlatformSolve = SolveResult + SolutionTape? + DifficultyAssessment + runConfigHash`；')
  lines.push('- **磁带**用 `ReplayEngine.RecordTape(bundle, level, levelFileBytes, result, metadata)` 由同一份适配层录制——预览/试玩/门2对拍共用；无解/未知不要造磁带；')
  lines.push('- **门1** 的五项合规检查用 `AdapterHosting.RunAll(input)`：门面负责组装样本（状态键维度对、纯函数样本、确定性代表关+字节、适配层程序集）；')
  lines.push('- **难度**用 `FeatureExtractor.Extract` + `DifficultyModel.Evaluate`（固定权重），结论必须带模型版本；')
  lines.push('- **搜索**可以自带（不强制内核 BFS），但结论必须按内核五维度表达：status / optimality / graphComplete / terminationReason / metricsScope；预算耗尽=ResourceLimit（未知），不得冒充无解；')
  lines.push('- 适配器七成员用 `IGameAdapter` + `IExecutionProtocol` 组 `AdapterBundle`，参考 SokobanToyFacade 的 25 行装配。')
  lines.push('')

  // ---- 项目事实 ----
  lines.push('# 项目信息')
  lines.push(`- 引擎类型：${engineType ?? '未知'}`)
  lines.push(`- 项目根目录：${projectRoot ?? '（未登记，请向用户确认）'}`)
  lines.push(`- 注册标识（gameId，必须一字不差）：${gameId}`)
  lines.push(`- 工具仓库：${repo.length > 0 ? repo : 'LevelForge_glm'}`)
  lines.push('')

  if (profile) {
    lines.push('# 项目扫描事实（平台已扫描，可直接采信；如与实际不符请重新扫描）')
    for (const o of profile.observations) lines.push(`- ${o}`)
    if (profile.levelDirCandidates.length > 0) {
      lines.push('- 关卡目录候选（按"像关卡"程度排序）：' + profile.levelDirCandidates.map((c) => `${c.directory}/(${c.jsonCount}个,${c.namingHint})`).join('、'))
    }
    for (const s of profile.sampleFiles.filter((x) => x.parsed)) {
      lines.push(`- 抽样文件 ${s.path}（${s.bytes}B）顶层键：{${s.topLevelKeys}}`)
    }
    const previewSample = profile.sampleFiles.find((s) => s.parsed && s.preview)
    if (previewSample?.preview) {
      lines.push(`- 抽样关卡原文（${previewSample.path}，最多 2000 字符）：`)
      lines.push('```json')
      lines.push(previewSample.preview)
      lines.push('```')
      lines.push('- 原文只用于确认字段形态；规则语义仍以玩法代码为准，不要只凭样例臆断全部规则。')
    }
    lines.push('')
    lines.push('# 关卡数据核验（写解析器前必须先做）')
    lines.push('- 候选目录只是候选：先打开抽样文件确认它是**关卡**（含关卡字段，如 levelId/mapWidth/pigs/grid/tiles 等）；')
    if (isToolConfigKeys(profile.sampleFiles.find((s) => s.parsed)?.topLevelKeys)) {
      lines.push('- **警告：当前排第一的候选抽样是工具配置（runtimeOptions/compilerOptions 一类），不是关卡数据。** 继续在项目内寻找关卡源（常见位置：LevelEditor/Luban~/Datas/Levels、Content/Levels、Assets/**/Levels、配置表导出目录），不要拿工具配置当关卡。')
    }
    lines.push('- 确认后写"格式说明书 + 静态校验器"，并让平台按该目录导入 *.json；若关卡是二进制/表格（Luban ByteBuf 等），说明源数据从哪来、平台导入用哪份形态。')
    lines.push('')
  }

  if (codeRoot || (profile?.codeDirCandidates?.length ?? 0) > 0) {
    lines.push('# 玩法代码（规则承载代码——求解器语义的唯一权威来源，请全量阅读后再动手；插件/三方库一律忽略）')
    if (codeRoot) {
      lines.push(`- 玩法代码根目录（用户已确认）：${codeRoot}`)
    } else {
      lines.push('- 玩法代码根目录：用户尚未确认——结合代码结构向用户确认后全量阅读，不要凭抽样臆断规则')
    }
    for (const c of profile?.codeDirCandidates ?? []) {
      if (c.directory !== codeRoot) {
        lines.push(`- 候选目录：${c.directory}/（${c.sourceFileCount} 个源码文件）`)
      }
    }
    lines.push('')
  }

  if (analysis) {
    lines.push('# 深度分析结论（平台内模型初稿，已经用户审核环节；仍请核验）')
    for (const q of analysis.fourQuestions) {
      lines.push(`- 四问｜${q.question}：${q.answer}（证据：${q.evidence}）`)
    }
    for (const m of analysis.mechanics) lines.push(`- 机制｜${m}`)
    for (const r of analysis.risks) lines.push(`- 风险｜${r}`)
    if (analysis.summary) lines.push(`- 总结｜${analysis.summary}`)
    lines.push('')
  }

  // ---- 导入与验收口径 ----
  lines.push('# 导入与验收口径（平台按此判定，不按自述）')
  lines.push('- **导入**：平台读关卡目录下 `*.json`，逐文件调用 `TryGetLevelId` + `FormatErrors`；')
  lines.push('  - 错误信息含 `v1 支持子集` → 计入「子集外跳过」（这是范围声明）；其他错误 → 计入「校验失败」（数据/实现问题）；')
  lines.push('  - 因此：v1 可以只支持一个规则子集（例如只支持部分猪类型/无地形），但**子集外必须用该前缀如实拒绝**，禁止静默简化或假装可解。')
  lines.push('- **门1**：`RunGate1Checks` 必须返回五项检查（依赖扫描/状态键判别/纯函数/确定性/内核自测）的完整报告；状态键维度对要覆盖规则矩阵声明的全部维度；确定性用可解代表关+原文字节；全绿才算过（零豁免）。')
  lines.push('- **测试**：交付 xUnit 测试（解析/规则/非法数据/状态键/纯函数），并至少跑通 1 个真实关卡的端到端求解（有磁带、outcome=win）。')
  lines.push('- **交付**：插件工程放工具仓库之外，`ProjectReference` 引用仓库内 `src/LevelForge.FacadeContracts` 与 `src/LevelForge.Kernel`（绝对路径）；只交付插件 DLL 与非契约依赖，**不要复制** LevelForge.Kernel.dll / LevelForge.FacadeContracts.dll（类型分裂）；`Registration.GameId` 必须为 `' + gameId + '`。')
  lines.push('- **验证闭环**：上传 DLL → 平台检测（装载+绑定+门1）→ 未过按门1 逐项修复 → 重新编译上传，直到全绿；不要修改平台源码、不要重新构建平台。')
  lines.push('- **诚实边界**：门2 未跑前不得宣称"真机一致"；预算耗尽≠无解；分析初稿≠已确认。')
  lines.push('')

  // ---- 任务步骤 ----
  lines.push('# 任务步骤')
  lines.push('1. 阅读上面「工具仓库」里的 skills/onboarding-skill/（先 README 再 contract/deliverables）与 2 个参考门面；')
  lines.push('2. 核验关卡数据形态（见「关卡数据核验」），产出资格评估（四问 + 证据），交由用户粘贴回向导②存档；')
  lines.push('3. 按 contract.md 实现七成员 + 执行协议 + 场景快照，并产出一套接入产物：规则矩阵、格式说明书、机制表、视觉映射、玩法规则书、难度权重；')
  lines.push('4. 新建 net10.0 类库工程（仓库外），ProjectReference 绝对路径引用 FacadeContracts + Kernel；门面实现 IGameFacade；按「契约与内核职责」接好 Solve/Tape/门1/难度；')
  lines.push(`5. 编译产出插件 DLL（连同非契约依赖）交付用户，在向导③上传检测；门1 未过则按报告修复重传。`)
  lines.push('')
  lines.push('# 约束')
  lines.push('- 严格遵循接入契约与诚实语义：非法数据必须抛异常，禁止静默修复；未知照实标注，不冒充结论。')
  lines.push('- 先声明支持子集与覆盖边界，再实现；覆盖不到的机制在校验阶段拒绝，不静默降级。')

  return lines.join('\n')
}
