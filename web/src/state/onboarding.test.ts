// 接入向导纯逻辑测试：清单完备性（三态）/ 六步完成度 / 门3 四区
import { describe, expect, it } from 'vitest'
import type { OnboardingTask } from '../api/client'
import {
  REVIEW_CHECKLIST,
  allStepsDone,
  checklistComplete,
  checklistProgress,
  groupGate3,
  parseChecklistDetail,
  seedChecklist,
  type ChecklistStateEntry,
} from './onboarding'

function task(step: number, status: string): OnboardingTask {
  return { step, title: `step${step}`, status, detail: null, updatedAt: '' }
}

function confirmedAll(): ChecklistStateEntry[] {
  return REVIEW_CHECKLIST.map((i) => ({ index: i.index, state: 'confirmed' }))
}

describe('审核清单（skill 详设 §5 四大组 13 条）', () => {
  it('A组5条/B组4条/C组2条/D组2条，编号 1..13 连续', () => {
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'A')).toHaveLength(5)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'B')).toHaveLength(4)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'C')).toHaveLength(2)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'D')).toHaveLength(2)
    expect(REVIEW_CHECKLIST.map((i) => i.index)).toEqual(Array.from({ length: 13 }, (_, i) => i + 1))
  })

  it('每条都是"断言 + 判据"，写清与什么一致/依据是什么', () => {
    expect(REVIEW_CHECKLIST.every((i) => i.text.length > 12 && (i.detail ?? '').length > 5)).toBe(true)
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('与游戏本体一致'))).toBe(true)
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('真实输入路径'))).toBe(true)
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('可视覆盖'))).toBe(true)
  })
})

describe('六步完成度', () => {
  it('全部 done 才算完成', () => {
    expect(allStepsDone([1, 2, 3, 4, 5, 6].map((s) => task(s, 'done')))).toBe(true)
    expect(allStepsDone([1, 2, 3, 4, 5, 6].map((s) => task(s, s === 5 ? 'doing' : 'done')))).toBe(false)
  })
  it('步骤缺失不算完成', () => {
    expect(allStepsDone([1, 2, 3].map((s) => task(s, 'done')))).toBe(false)
  })
})

describe('清单三态完成判定', () => {
  it('全部条目有终态才完成；缺一条待确认不行', () => {
    expect(checklistComplete(confirmedAll(), REVIEW_CHECKLIST)).toBe(true)
    const missing = confirmedAll()
    missing[5] = { index: REVIEW_CHECKLIST[5].index, state: 'pending' }
    expect(checklistComplete(missing, REVIEW_CHECKLIST)).toBe(false)
  })

  it('「不适用」必须写理由，否则不算完成', () => {
    const states = confirmedAll()
    states[0] = { index: REVIEW_CHECKLIST[0].index, state: 'na' }
    expect(checklistComplete(states, REVIEW_CHECKLIST)).toBe(false)
    states[0] = { index: REVIEW_CHECKLIST[0].index, state: 'na', reason: 'v1 未建模特殊猪' }
    expect(checklistComplete(states, REVIEW_CHECKLIST)).toBe(true)
  })

  it('进度统计区分 已确认/不适用/待确认', () => {
    const states: ChecklistStateEntry[] = [
      { index: 1, state: 'confirmed' },
      { index: 2, state: 'na', reason: '不适用' },
    ]
    const progress = checklistProgress(states, REVIEW_CHECKLIST)
    expect(progress.confirmed).toBe(1)
    expect(progress.na).toBe(1)
    expect(progress.pending).toBe(11)
    expect(progress.total).toBe(13)
  })
})

describe('向导存档解析（兼容旧格式）', () => {
  it('旧格式④已完成：视为全部已确认', () => {
    const detail = JSON.stringify({ items: REVIEW_CHECKLIST.slice(0, 3) })
    const parsed = parseChecklistDetail(detail, 'done')!
    expect(parsed.items).toHaveLength(3)
    expect(parsed.states.every((s) => s.state === 'confirmed')).toBe(true)
  })

  it('旧格式④未完成：全部待确认；缺 kind 按文本推断', () => {
    const detail = JSON.stringify({ items: [
      { group: 'A', index: 1, text: '验证点击后的停位' },
      { group: 'A', index: 2, text: '胜利判定与游戏本体一致' },
    ] })
    const parsed = parseChecklistDetail(detail, 'todo')!
    expect(parsed.states.every((s) => s.state === 'pending')).toBe(true)
    expect(parsed.items[0].kind).toBe('task')
    expect(parsed.items[1].kind).toBe('confirm')
  })

  it('新格式保留已存状态与不适用理由', () => {
    const detail = JSON.stringify({
      items: [{ group: 'A', index: 1, text: '滑行规则与游戏本体一致', kind: 'confirm' }],
      states: [{ index: 1, state: 'na', reason: 'v1 子集不涉及' }],
    })
    const parsed = parseChecklistDetail(detail, 'todo')!
    expect(parsed.states[0]).toEqual({ index: 1, state: 'na', reason: 'v1 子集不涉及' })
  })

  it('损坏/纯文本存档返回 null（回退种子清单）', () => {
    expect(parseChecklistDetail('报告：接入难度高', 'todo')).toBeNull()
    expect(parseChecklistDetail(null, 'todo')).toBeNull()
  })
})

describe('按项目画像生成清单初稿', () => {
  it('无画像 = 纯模板', () => {
    expect(seedChecklist(null).every((i) => !i.custom)).toBe(true)
  })
  it('有画像 = 模板 + 项目派生条目（关卡目录/抽样键/引擎特有）', () => {
    const profile = {
      scannedAt: '2026-11-04T00:00:00Z',
      projectRoot: 'E:/proj',
      engine: 'cocos-creator3',
      levelDirCandidates: [{ directory: 'assets/levels', jsonCount: 64, namingHint: 'numbered' }],
      sampleFiles: [{ path: 'assets/levels/1.json', bytes: 320, parsed: true, topLevelKeys: 'levelId,grid', parseError: null }],
      observations: [],
    }
    const items = seedChecklist(profile as never)
    const customs = items.filter((i) => i.custom)
    expect(customs.length).toBeGreaterThanOrEqual(3)
    expect(customs.some((i) => i.text.includes('assets/levels'))).toBe(true)
    expect(customs.some((i) => i.text.includes('levelId,grid'))).toBe(true)
    expect(customs.some((i) => i.text.includes('Cocos'))).toBe(true)
    expect(customs.every((i) => i.index >= 100)).toBe(true)
  })
})

describe('门3 四区分组', () => {
  const rows = (verdicts: string[]) => verdicts.map((verdict, i) => ({ levelId: String(i + 1), verdict }))

  it('按 verdict 分到四个区', () => {
    const zones = groupGate3(rows(['red', 'yellow', 'new', 'match', 'match']))
    expect(zones.red).toEqual(['1'])
    expect(zones.yellow).toEqual(['2'])
    expect(zones.fresh).toEqual(['3'])
    expect(zones.match).toBe(2)
  })

  it('存在红灯即阻断转正', () => {
    expect(groupGate3(rows(['red', 'match'])).blocked).toBe(true)
    expect(groupGate3(rows(['match', 'yellow', 'new'])).blocked).toBe(false)
    expect(groupGate3([]).blocked).toBe(false)
  })

  it('未知 verdict 归入黄灯（宁可人工看一眼，不静默放过）', () => {
    const zones = groupGate3(rows(['something-new']))
    expect(zones.yellow).toEqual(['1'])
    expect(zones.blocked).toBe(false)
  })
})
