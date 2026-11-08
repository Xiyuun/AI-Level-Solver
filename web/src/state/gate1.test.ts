import { describe, expect, it } from 'vitest'
import { gate1Checks } from './gate1'

describe('gate1Checks', () => {
  it('只保留检查项对象，过滤 allPassed 这类派生布尔字段', () => {
    const checks = gate1Checks({
      dependencyScan: { passed: true, issues: [], notes: ['ok'] },
      purity: { passed: false, issues: ['入参被修改'], notes: [] },
      allPassed: true,
    })
    expect(checks.map(([name]) => name)).toEqual(['dependencyScan', 'purity'])
    expect(checks[1][1].issues).toEqual(['入参被修改'])
  })

  it('缺字段/非法值兜底为空数组，不抛异常', () => {
    const checks = gate1Checks({
      broken: { passed: true },
      nullish: null,
      stringy: 'x',
    })
    expect(checks).toHaveLength(1)
    expect(checks[0][1].issues).toEqual([])
    expect(checks[0][1].notes).toEqual([])
  })

  it('空报告返回空列表', () => {
    expect(gate1Checks(null)).toEqual([])
    expect(gate1Checks({})).toEqual([])
  })
})
