// 接入向导纯逻辑：审核清单（skill checklist.md 模板 + 项目裁剪）、六步完成度、上线条件
import type { OnboardingTask, ProjectAnalysis, ProjectProfile } from '../api/client'

// 审核清单条目语义（解决"勾选框含义不清"的问题）：
// - confirm：可签字的断言——人核对后要么「确认通过」，要么「不适用 + 理由」，不能留待确认
// - task：待办动作（AI 草稿常见的「验证…/检查…」），先把事做完再确认；不应与断言混为一谈
// - fact：机器事实核对（扫描口径），确认「页面显示与项目实际一致」
export type ChecklistKind = 'confirm' | 'task' | 'fact'

// 三态：待确认 / 已确认 / 不适用（不适用必须写理由，否则不算完成）
export type ChecklistState = 'pending' | 'confirmed' | 'na'

// 审核清单条目：text 写"谁与谁一致/什么成立"，detail 写判据（依据文件/用例/门1检查）
export interface ChecklistItem {
  group: 'A' | 'B' | 'C' | 'D'
  index: number
  text: string
  detail?: string
  kind?: ChecklistKind
  source?: 'template' | 'profile' | 'analysis'
  custom?: boolean
}

// 每条目的当前状态（按 index 持久化）
export interface ChecklistStateEntry {
  index: number
  state: ChecklistState
  reason?: string
}

// 通用模板（skill checklist.md 四大组 13 条）：全部改写成"断言 + 判据"，
// 让审核者清楚"要确认的是哪条规则、和什么一致、拿什么判据核对"。
export const REVIEW_CHECKLIST: ChecklistItem[] = [
  {
    group: 'A', index: 1, source: 'template',
    text: '点击滑行规则与游戏本体一致：逐格移动、被其它猪阻挡时停在阻挡格前一格、越界整只离场',
    detail: '依据：NormalPig.cs（OnPointerClick/移动与碰撞）、BoardDeadlockDetector.CanAdvanceOneStep；用例：滑行_被挡停在前一格、滑行_越界离场',
  },
  {
    group: 'A', index: 2, source: 'template',
    text: '胜利判定与游戏本体一致：所有计入过关的猪离场后才判胜',
    detail: '依据：GameManager.TrySettleIfCleared / PigExitedEvent；用例：求解_样例关有解（末帧清空）',
  },
  {
    group: 'A', index: 3, source: 'template',
    text: '死局判定与游戏本体一致：仍有猪且没有任何可起步猪时判负，歧义语义已写成用例',
    detail: '依据：BoardDeadlockDetector.IsWholeBoardBlocked；用例：贴脸死局_完整搜索可证无解',
  },
  {
    group: 'A', index: 4, source: 'template',
    text: '道具与特殊猪效果与游戏本体一致；不建模时校验器必须明确拒绝，不得静默按普通猪处理',
    detail: 'v1 子集：特殊猪/道具不建模；FormatErrors 以「v1 支持子集之外」拒绝（导入实测 33/466/0）',
  },
  {
    group: 'A', index: 5, source: 'template',
    text: '多猪连锁/结算顺序与游戏本体一致；仅单猪滑行时标记不适用并说明理由',
    detail: 'v1 子集无连锁结算；若后续支持特殊猪需补此项',
  },
  {
    group: 'B', index: 6, source: 'template',
    text: 'StateKey 覆盖全部玩法维度：每只猪的位置、朝向、类型与在场集合',
    detail: '门1 状态键判别样本覆盖「位置/朝向」两维；代码：PiggyFarmAdapter.StateKey',
  },
  {
    group: 'B', index: 7, source: 'template',
    text: 'ApplyMove 是纯函数：不修改入参状态、无隐藏状态',
    detail: '门1 纯函数检查：重复应用 3 次、三视角一致、入参未变',
  },
  {
    group: 'B', index: 8, source: 'template',
    text: '表现层随机/物理未进入规则层（资格报告列出的排除项已逐条核对）',
    detail: 'v1 子集无随机/物理；规则矩阵「确定性风险」条目',
  },
  {
    group: 'B', index: 9, source: 'template',
    text: '场景快照可视覆盖：猪的占格/朝向在 entities 可见，剩余猪在 globals 信息栏可见',
    detail: 'visual-mapping.json：pigHead/pigBody + remainingPigs',
  },
  {
    group: 'C', index: 10, source: 'template',
    text: '适配层无引擎引用、无 IO、无网络、无时间调用',
    detail: '门1 依赖扫描通过；代码抽查：无 UnityEngine/Cocos 引用',
  },
  {
    group: 'C', index: 11, source: 'template',
    text: '每条已确认语义都有对应测试用例',
    detail: '16 个 xUnit：解析/滑行/离场/死局/状态键/纯函数/求解/试玩/语料',
  },
  {
    group: 'D', index: 12, source: 'template',
    text: '门2 harness 注入走真实输入路径（若配置门2）',
    detail: '未跑门2时标记不适用；harness：Assets/Editor/LevelForgeHarness/PairingHarness.cs',
  },
  {
    group: 'D', index: 13, source: 'template',
    text: '门2 的初始/逐步语义状态与胜利信号来自游戏本体（非适配器重算）',
    detail: '未跑门2时标记不适用；门2 对拍报告',
  },
]

// 按项目画像派生条目（机器事实 → 可核对的断言）
export function seedChecklist(profile: ProjectProfile | null): ChecklistItem[] {
  const items = REVIEW_CHECKLIST.map((i) => ({ ...i }))
  let next = 100
  const push = (group: ChecklistItem['group'], text: string, detail: string, kind: ChecklistKind = 'confirm') => {
    items.push({ group, index: next++, text, detail, kind, source: 'profile', custom: true })
  }
  const top = profile?.levelDirCandidates?.[0]
  if (top) {
    push('A',
      `关卡数据形态已确认：${top.directory}/ 的 ${top.jsonCount} 个 ${top.namingHint} 命名 JSON 被适配层解析器支持，子集外文件被明确拒绝`,
      `扫描：${top.directory}/；导入实测：33 导入 / 466 子集外跳过 / 0 失败`,
      'fact')
  }
  const parsedSample = profile?.sampleFiles?.find((s) => s.parsed && s.topLevelKeys)
  if (parsedSample?.topLevelKeys) {
    push('B',
      `抽样关卡顶层键全部被格式说明书或校验器覆盖：{${parsedSample.topLevelKeys}}`,
      `来源：${parsedSample.path}；format-spec.md`,
      'fact')
  }
  if (profile?.engine === 'cocos-creator2' || profile?.engine === 'cocos-creator3') {
    push('C', 'Cocos 随机源只在表现层使用，未进入规则层', '资格报告「确定性」结论；代码抽查', 'confirm')
  }
  if (profile?.engine === 'unity') {
    push('C', 'Unity 物理/协程只影响表现，未混入规则层胜负判定', '资格报告「确定性」结论；规则矩阵风险条目', 'confirm')
  }
  return items
}

// 深层分析初稿 → 清单条目：AI 草稿可以是断言，也可能是「验证…/检查…」任务；
// 这里按文本前缀标注 kind，页面上任务与断言分开显示，避免"任务被当断言勾掉"。
export function seedChecklistFromAnalysis(profile: ProjectProfile | null, analysis: ProjectAnalysis | null): ChecklistItem[] {
  const items = seedChecklist(profile)
  if (!analysis?.checklistDraft?.length) return items
  let next = items.reduce((m, i) => Math.max(m, i.index), 99) + 1
  for (const d of analysis.checklistDraft) {
    const group = (['A', 'B', 'C', 'D'].includes(d.group) ? d.group : 'A') as ChecklistItem['group']
    const text = d.text?.trim()
    if (text) items.push({ group, index: next++, text, kind: inferKind(text), source: 'analysis', custom: true })
  }
  return items
}

// 旧草稿/人工追加文本的任务识别（新提示词要求断言式，但历史存档与手填可能仍是任务口吻）
export function inferKind(text: string): ChecklistKind {
  return /^(验证|检查|核对|补充|新增)/.test(text.trim()) ? 'task' : 'confirm'
}

// 读取向导存档：兼容旧格式（只有 items、无 states）——
// 旧存档若④已完成，视为全部已确认（当时确实全勾才能完成）；否则全部待确认。
export function parseChecklistDetail(
  detail: string | null | undefined,
  status: string,
): { items: ChecklistItem[]; states: ChecklistStateEntry[] } | null {
  if (!detail) return null
  try {
    const parsed = JSON.parse(detail) as { items?: ChecklistItem[]; states?: ChecklistStateEntry[] }
    if (!Array.isArray(parsed.items) || parsed.items.length === 0) return null
    const items = parsed.items.map((item) => ({
      ...item,
      kind: item.kind ?? inferKind(item.text),
      source: item.source ?? (item.custom ? 'analysis' : 'template'),
    }))
    const saved = Array.isArray(parsed.states) ? parsed.states.filter((s) => typeof s?.index === 'number') : []
    const states: ChecklistStateEntry[] = items.map((item) => {
      const hit = saved.find((s) => s.index === item.index)
      if (hit) return hit
      return { index: item.index, state: status === 'done' ? 'confirmed' : 'pending' }
    })
    return { items, states }
  } catch {
    return null   // 旧纯文本存档或损坏：回退到种子清单
  }
}

// ④初始化：任务未加载时返回 null（禁止用空 tasks 先播种，否则已存档的 states 会被 pending 冲掉）
export function initChecklistFromTasks(
  tasks: OnboardingTask[],
  profile: ProjectProfile | null,
  analysis: ProjectAnalysis | null,
): { items: ChecklistItem[]; states: ChecklistStateEntry[] } | null {
  if (tasks.length === 0) return null
  const step4 = tasks.find((t) => t.step === 4)
  const loaded = parseChecklistDetail(step4?.detail, step4?.status ?? 'todo')
  if (loaded) return loaded
  const seeded = seedChecklistFromAnalysis(profile, analysis)
  return { items: seeded, states: seeded.map((i) => ({ index: i.index, state: 'pending' as ChecklistState })) }
}

// 清单是否完成：每条都必须有终态；「不适用」必须写理由（否则等于没核对）
export function checklistComplete(states: ChecklistStateEntry[], items: ChecklistItem[]): boolean {
  if (items.length === 0) return false
  return items.every((item) => {
    const entry = states.find((s) => s.index === item.index)
    if (!entry || entry.state === 'pending') return false
    if (entry.state === 'na') return (entry.reason ?? '').trim().length > 0
    return true
  })
}

// 完成度统计（页面按钮文案/提示用）
export function checklistProgress(
  states: ChecklistStateEntry[],
  items: ChecklistItem[],
): { total: number; confirmed: number; na: number; pending: number } {
  let confirmed = 0
  let na = 0
  let pending = 0
  for (const item of items) {
    const entry = states.find((s) => s.index === item.index)
    if (!entry || entry.state === 'pending') pending++
    else if (entry.state === 'na') na++
    else confirmed++
  }
  return { total: items.length, confirmed, na, pending }
}

// 六步是否全部完成（上线注册/转正的前提）
export function allStepsDone(tasks: OnboardingTask[]): boolean {
  return tasks.length === 6 && tasks.every((t) => t.status === 'done')
}

// 步骤中文名
export function stepTitle(step: number): string {
  const titles: Record<number, string> = {
    1: '①登记（关卡目录与门2适用性）',
    2: '②资格评估（机器事实 + 报告存档）',
    3: '③产物验收 + 门1运行卡',
    4: '④人工审核清单（按项目）',
    5: '⑤门2对拍分诊',
    6: '⑥门3基准 diff + 上线注册',
  }
  return titles[step] ?? `步骤 ${step}`
}

// ---- 门3 四区（验证门 §5.2 diff 语义）----
// 红灯=结论变了（阻断转正）；黄灯=路径/难度漂移（需人判）；待确认=新关；一致=无变化
export interface Gate3Zones {
  red: string[]       // 红灯关号
  yellow: string[]    // 黄灯关号
  fresh: string[]     // 待确认新关
  match: number       // 一致关数
  blocked: boolean    // 是否阻断转正（存在红灯）
}

// 按 verdict 分组；未知 verdict 归入黄灯（宁可人工看一眼，也不静默放过）
export function groupGate3(rows: { levelId: string; verdict: string }[]): Gate3Zones {
  const red: string[] = []
  const yellow: string[] = []
  const fresh: string[] = []
  let match = 0
  for (const row of rows) {
    if (row.verdict === 'red') red.push(row.levelId)
    else if (row.verdict === 'new') fresh.push(row.levelId)
    else if (row.verdict === 'match') match++
    else yellow.push(row.levelId)
  }
  return { red, yellow, fresh, match, blocked: red.length > 0 }
}
