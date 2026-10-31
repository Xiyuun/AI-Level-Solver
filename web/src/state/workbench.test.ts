// 工作台纯逻辑测试：状态机流转 / 失败建议映射 / 流水线日志 / 机制提示（vitest，无 DOM）
import { describe, expect, it } from 'vitest'
import type { CandidateAttempt, GenerationRun } from '../api/client'
import { logEntriesFromRun, mechanicsHint, nextPhase, suggestionsFrom, candidateLabel } from './workbench'

function candidate(overrides: Partial<CandidateAttempt>): CandidateAttempt {
  return {
    attemptIndex: 1,
    contentJson: '{}',
    failureClass: 'none',
    failureDetail: null,
    difficultyBand: null,
    difficultyScore: null,
    solveRunId: null,
    tapeId: null,
    llmCalls: 1,
    elapsedMs: 5,
    ...overrides,
  }
}

function run(overrides: Partial<GenerationRun>): GenerationRun {
  return {
    runId: 1,
    gameId: 'sokoban-toy',
    status: 'failed',
    targetDifficulty: 2,
    levelNumber: null,
    specialRequirements: null,
    candidateLimit: 3,
    solveTimeoutMs: 30000,
    promptVersion: 'v1',
    candidates: [],
    adoptedAttempt: null,
    failureSummary: '候选已耗尽（3/3）',
    totalElapsedMs: 100,
    ...overrides,
  }
}

describe('页面状态机（§3）', () => {
  it('空态→生成中→成功/失败', () => {
    expect(nextPhase('idle', 'start')).toBe('generating')
    expect(nextPhase('generating', 'succeed')).toBe('success')
    expect(nextPhase('generating', 'exhaust')).toBe('failed')
    expect(nextPhase('generating', 'apiError')).toBe('failed')
  })
  it('生成中可取消回空态；失败态应用建议重试回生成中', () => {
    expect(nextPhase('generating', 'cancel')).toBe('idle')
    expect(nextPhase('failed', 'start')).toBe('generating')
    expect(nextPhase('success', 'start')).toBe('generating')
  })
  it('非生成中收到终态事件不误转移（迟到的响应）', () => {
    expect(nextPhase('idle', 'succeed')).toBe('idle')
    expect(nextPhase('failed', 'exhaust')).toBe('failed')
  })
})

describe('失败建议映射（§5 自动应用直接重试）', () => {
  it('预算耗尽 → 超时加倍（封顶 120s）', () => {
    const s = suggestionsFrom(run({
      solveTimeoutMs: 80000,
      candidates: [candidate({ failureClass: 'resource_limit' })],
    }))!
    expect(s.solveTimeoutMs).toBe(120000)
    expect(s.reasons[0]).toContain('预算耗尽')
  })

  it('内容失败耗尽候选 → 上限 +2 封顶 10', () => {
    const s = suggestionsFrom(run({
      candidateLimit: 3,
      candidates: [candidate({ failureClass: 'unsolvable' })],
    }))!
    expect(s.candidateLimit).toBe(5)
    const capped = suggestionsFrom(run({
      candidateLimit: 9,
      candidates: [candidate({ failureClass: 'unsolvable' })],
    }))!
    expect(capped.candidateLimit).toBe(10)
  })

  it('全部难度不达标且可降 → 降一档；特殊要求一并放宽', () => {
    const s = suggestionsFrom(run({
      targetDifficulty: 3,
      specialRequirements: '必须包含 3 只朝下的猪',
      candidates: [candidate({ failureClass: 'difficulty_miss', attemptIndex: 1 }), candidate({ failureClass: 'difficulty_miss', attemptIndex: 2 })],
    }))!
    expect(s.targetDifficulty).toBe(2)
    expect(s.specialRequirements).toBe('')
    expect(s.reasons.some((r) => r.includes('难度'))).toBe(true)
    expect(s.reasons.some((r) => r.includes('特殊要求'))).toBe(true)
  })

  it('难度 1 档不达标无处可降：不产生降档建议', () => {
    const s = suggestionsFrom(run({
      targetDifficulty: 1,
      candidates: [candidate({ failureClass: 'difficulty_miss' })],
    }))!
    expect(s.targetDifficulty).toBeUndefined()
  })

  it('成功运行 / 空候选 / 纯 API 失败 → 无建议（等人工）', () => {
    expect(suggestionsFrom(run({ status: 'succeeded' }))).toBeNull()
    expect(suggestionsFrom(run({}))).toBeNull()
    expect(suggestionsFrom(run({ candidates: [candidate({ failureClass: 'api_error' })] }))).toBeNull()
  })
})

describe('流水线日志（§1⑩ 五色角色行）', () => {
  it('逐候选展开：出题→判卷→难度→分类→重生触发；运行级结论收尾', () => {
    const entries = logEntriesFromRun(run({
      status: 'succeeded',
      candidateLimit: 2,
      adoptedAttempt: 2,
      candidates: [
        candidate({ attemptIndex: 1, failureClass: 'unsolvable', failureDetail: '完成可达图遍历，未发现目标状态', solveRunId: 11, llmCalls: 1 }),
        candidate({ attemptIndex: 2, failureClass: 'none', failureDetail: '求解验证通过', solveRunId: 12, difficultyBand: 2, difficultyScore: 5.5, tapeId: 7 }),
      ],
    }))
    const roles = entries.map((e) => e.role)
    expect(roles).toContain('llm')
    expect(roles).toContain('solver')
    expect(roles).toContain('difficulty')
    expect(roles).toContain('constraint')
    expect(roles).toContain('integration')
    expect(entries.some((e) => e.role === 'integration' && e.text.includes('触发第 2 次重生'))).toBe(true)
    expect(entries.at(-1)!.text).toContain('运行成功')
    expect(entries.at(-1)!.text).toContain('第 2 次尝试被采纳')
  })

  it('失败运行以候选耗尽结论收尾，措辞不断言需求不可满足', () => {
    const entries = logEntriesFromRun(run({
      candidates: [candidate({ failureClass: 'difficulty_miss' })],
      candidateLimit: 1,
    }))
    const last = entries.at(-1)!
    expect(last.role).toBe('integration')
    expect(last.text).toContain('候选已耗尽')
  })

  it('API 失败候选：LLM 行后直接失败结论，不进判卷链', () => {
    const entries = logEntriesFromRun(run({
      candidates: [candidate({ failureClass: 'api_error', failureDetail: 'HTTP 500' })],
      failureSummary: 'LLM API 调用失败（第 1 次尝试）：HTTP 500',
    }))
    const afterLlm = entries.slice(entries.findIndex((e) => e.role === 'llm') + 1)
    expect(afterLlm[0].role).toBe('integration')
    expect(afterLlm[0].text).toContain('API 失败')
  })
})

describe('机制范围提示（§4）', () => {
  const mechanics = {
    version: 1,
    ranges: [
      { from: 1, to: 10, mechanics: ['walk', 'push'], note: '玩具玩法无道具机制' },
      { from: 501, to: 600, mechanics: ['walk', 'push', 'remove'], note: '解锁移除道具' },
    ],
  }

  it('编号落在区间内：显示范围+机制+备注', () => {
    expect(mechanicsHint(mechanics, '3')).toContain('1–10')
    expect(mechanicsHint(mechanics, '3')).toContain('walk、push')
    expect(mechanicsHint(mechanics, '550')).toContain('解锁移除道具')
  })
  it('区间外/非数字/空 → 空串', () => {
    expect(mechanicsHint(mechanics, '999')).toBe('')
    expect(mechanicsHint(mechanics, 'toy-001')).toBe('')
    expect(mechanicsHint(mechanics, '')).toBe('')
  })
})

describe('候选结论标签', () => {
  it('八类失败/通过标签齐全', () => {
    expect(candidateLabel(candidate({ failureClass: 'none' }))).toBe('通过')
    expect(candidateLabel(candidate({ failureClass: 'unsolvable' }))).toBe('模型内无解')
    expect(candidateLabel(candidate({ failureClass: 'resource_limit' }))).toBe('预算耗尽')
    expect(candidateLabel(candidate({ failureClass: 'difficulty_miss' }))).toBe('难度不达标')
  })
})
