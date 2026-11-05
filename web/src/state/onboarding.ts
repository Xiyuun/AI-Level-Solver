// 接入向导纯逻辑：审核清单（skill checklist.md 模板 + 项目裁剪）、六步完成度、上线条件
import type { OnboardingTask, ProjectAnalysis, ProjectProfile } from '../api/client'

// 审核清单条目（模板条目照抄 checklist.md 四大组；custom=true 是按项目画像派生的条目）
export interface ChecklistItem {
  group: 'A' | 'B' | 'C' | 'D'
  index: number
  text: string
  custom?: boolean
}

// 通用模板（checklist.md 原文；仅作每玩法清单的种子——页面会叠加按项目画像派生的条目，且可增删）
export const REVIEW_CHECKLIST: ChecklistItem[] = [
  { group: 'A', index: 1, text: '移动/操作规则一致：边界、阻挡、阻挡后的停位' },
  { group: 'A', index: 2, text: '胜利判定条件一致（逐字核对游戏本体判定）' },
  { group: 'A', index: 3, text: '失败/死局语义——语义歧义转用例，禁止直接勾选' },
  { group: 'A', index: 4, text: '道具/特殊机制效果一致（若有）' },
  { group: 'A', index: 5, text: '多实体连锁/结算顺序与游戏一致' },
  { group: 'B', index: 6, text: 'StateKey 覆盖状态维度枚举的全部维度（逐维度勾）' },
  { group: 'B', index: 7, text: 'ApplyMove 纯函数（入参不变、无隐藏状态）' },
  { group: 'B', index: 8, text: '排除清单核对：资格报告列出的表现层随机/物理未进入规则' },
  { group: 'B', index: 9, text: '预览状态可视覆盖：局部量在 cells/entities，全局量在 globals 及信息栏实际可见' },
  { group: 'C', index: 10, text: '无引擎引用 / 无 IO / 无网络 / 无时间调用（门1覆盖，抽查）' },
  { group: 'C', index: 11, text: '每条已确认语义都有对应测试用例' },
  { group: 'D', index: 12, text: 'harness 注入走真实输入路径（假对拍检查，若产出 harness）' },
  { group: 'D', index: 13, text: '初始/逐步语义状态与胜利信号来自游戏本体（非适配器重算）' },
]

// 按项目画像派生条目（三轮 N12：清单随项目变化——机器事实可直接转成核对项）
export function seedChecklist(profile: ProjectProfile | null): ChecklistItem[] {
  const items = REVIEW_CHECKLIST.map((i) => ({ ...i }))
  let next = 100
  const push = (group: ChecklistItem['group'], text: string) => {
    items.push({ group, index: next++, text, custom: true })
  }
  const top = profile?.levelDirCandidates?.[0]
  if (top) {
    push('A', `关卡数据形态确认：${top.directory}/ 的 ${top.jsonCount} 个 JSON（${top.namingHint} 命名）与适配层解析一一对应`)
  }
  const parsedSample = profile?.sampleFiles?.find((s) => s.parsed && s.topLevelKeys)
  if (parsedSample?.topLevelKeys) {
    push('B', `抽样关卡顶层键全部被覆盖：{${parsedSample.topLevelKeys}}（来源 ${parsedSample.path}）`)
  }
  if (profile?.engine === 'cocos-creator2' || profile?.engine === 'cocos-creator3') {
    push('C', 'Cocos 特有：随机源（洗牌/随机数种子）未进入规则层，仅在表现层使用')
  }
  if (profile?.engine === 'unity') {
    push('C', 'Unity 特有：物理/协程表现未混入胜负判定，判层结果以游戏本体判定为准')
  }
  return items
}

// 深度分析初稿 → 清单条目（三轮 N12 完整版 B）：模型产出的核对项并入模板种子，逐条标注"AI 初稿"
export function seedChecklistFromAnalysis(profile: ProjectProfile | null, analysis: ProjectAnalysis | null): ChecklistItem[] {
  const items = seedChecklist(profile)
  if (!analysis?.checklistDraft?.length) return items
  let next = items.reduce((m, i) => Math.max(m, i.index), 99) + 1
  for (const d of analysis.checklistDraft) {
    const group = (['A', 'B', 'C', 'D'].includes(d.group) ? d.group : 'A') as ChecklistItem['group']
    if (d.text?.trim()) items.push({ group, index: next++, text: d.text.trim(), custom: true })
  }
  return items
}

// 六步是否全部完成（上线注册/转正的前提）
export function allStepsDone(tasks: OnboardingTask[]): boolean {
  return tasks.length === 6 && tasks.every((t) => t.status === 'done')
}

// 审核清单是否全部勾选（第④步完成前提；条目按项目可变，长度不固定）
export function checklistComplete(checked: number[], items: ChecklistItem[]): boolean {
  return items.length > 0 && items.every((item) => checked.includes(item.index))
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
