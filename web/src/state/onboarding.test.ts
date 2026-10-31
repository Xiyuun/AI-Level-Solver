// 接入向导纯逻辑测试：清单完备性 / 六步完成度 / 勾选完成判定
import { describe, expect, it } from 'vitest'
import type { OnboardingTask } from '../api/client'
import { REVIEW_CHECKLIST, allStepsDone, checklistComplete } from './onboarding'

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
  it('13 条全勾才完成；缺一条不行', () => {
    const all = Array.from({ length: 13 }, (_, i) => i + 1)
    expect(checklistComplete(all)).toBe(true)
    expect(checklistComplete(all.slice(0, 12))).toBe(false)
  })
})
