// 接入向导纯逻辑测试：清单完备性 / 六步完成度 / 勾选完成判定
import { describe, expect, it } from 'vitest'
import type { OnboardingTask } from '../api/client'
import { REVIEW_CHECKLIST, allStepsDone, checklistComplete, groupGate3, seedChecklist } from './onboarding'

function task(step: number, status: string): OnboardingTask {
  return { step, title: `step${step}`, status, detail: null, updatedAt: '' }
}

describe('审核清单（skill 详设 §5 四大组 13 条）', () => {
  it('A组5条/B组4条/C组2条/D组2条，编号 1..13 连续', () => {
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'A')).toHaveLength(5)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'B')).toHaveLength(4)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'C')).toHaveLength(2)
    expect(REVIEW_CHECKLIST.filter((i) => i.group === 'D')).toHaveLength(2)
    expect(REVIEW_CHECKLIST.map((i) => i.index)).toEqual(Array.from({ length: 13 }, (_, i) => i + 1))
  })
  it('含假对拍检查与状态可视覆盖关键项', () => {
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('真实输入路径'))).toBe(true)
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('可视覆盖'))).toBe(true)
    expect(REVIEW_CHECKLIST.some((i) => i.text.includes('禁止直接勾选'))).toBe(true)
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

describe('清单勾选完成', () => {
  it('全部条目勾完才完成；缺一条不行', () => {
    const all = Array.from({ length: 13 }, (_, i) => i + 1)
    expect(checklistComplete(all, REVIEW_CHECKLIST)).toBe(true)
    expect(checklistComplete(all.slice(0, 12), REVIEW_CHECKLIST)).toBe(false)
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
