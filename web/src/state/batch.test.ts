// 批量生产纯逻辑测试：配比校验必须与后端规则一致（前端先拦，避免白发请求）
import { describe, expect, it } from 'vitest'
import type { BatchItem, BatchStatus } from '../api/client'
import {
  failedItems,
  isFinished,
  itemView,
  progressPercent,
  ratioTotal,
  runStatusView,
  spanLength,
  summaryLine,
  validateBatchForm,
  type BatchForm,
} from './batch'

const base: BatchForm = {
  fromLevel: 111,
  toLevel: 120,
  difficulty1Count: 4,
  difficulty2Count: 6,
  difficulty3Count: 0,
  specialRequirements: '',
  concurrency: 4,
  candidateLimit: 3,
  solveTimeoutMs: 30000,
}

describe('编号段与配比', () => {
  it('跨度计算：含首尾', () => {
    expect(spanLength(111, 120)).toBe(10)
    expect(spanLength(5, 5)).toBe(1)
  })

  it('非法起止返回 0（不让负数参与判断）', () => {
    expect(spanLength(0, 10)).toBe(0)
    expect(spanLength(20, 10)).toBe(0)
    expect(spanLength(Number.NaN, 10)).toBe(0)
  })

  it('配比总数等于三档之和', () => {
    expect(ratioTotal({ difficulty1Count: 4, difficulty2Count: 6, difficulty3Count: 0 })).toBe(10)
    expect(ratioTotal({ difficulty1Count: 0, difficulty2Count: 0, difficulty3Count: 0 })).toBe(0)
  })
})

describe('表单校验（与 BatchService.Start 同规则）', () => {
  it('配比正好等于跨度时通过', () => {
    expect(validateBatchForm(base)).toBeNull()
  })

  it('配比不等时报错并给出两个数字', () => {
    const msg = validateBatchForm({ ...base, difficulty2Count: 8 })
    expect(msg).toContain('12')
    expect(msg).toContain('10')
  })

  it('结束小于起始、起始小于 1、跨度超 500 都拦下', () => {
    expect(validateBatchForm({ ...base, toLevel: 100 })).toContain('小于起始')
    expect(validateBatchForm({ ...base, fromLevel: 0 })).toContain('≥1')
    expect(validateBatchForm({ ...base, fromLevel: 1, toLevel: 600, difficulty1Count: 600, difficulty2Count: 0 })).toContain('500')
  })

  it('并发与候选上限越界拦下', () => {
    expect(validateBatchForm({ ...base, concurrency: 0 })).toContain('并发')
    expect(validateBatchForm({ ...base, candidateLimit: 11 })).toContain('候选上限')
  })
})

function status(overrides: Partial<BatchStatus> = {}): BatchStatus {
  return {
    taskId: 1,
    gameId: 'slide-pig',
    status: 'running',
    total: 10,
    completed: 5,
    succeeded: 4,
    failed: 1,
    skipped: 0,
    totalElapsedMs: 12000,
    items: [],
    ...overrides,
  }
}

function item(overrides: Partial<BatchItem>): BatchItem {
  return {
    levelNumber: 111,
    status: 'pending',
    difficultyBand: null,
    solutionLength: null,
    generationRunId: null,
    failureSummary: null,
    elapsedMs: 0,
    ...overrides,
  }
}

describe('进度与结果展示', () => {
  it('进度百分比取整且不越界', () => {
    expect(progressPercent(status())).toBe(50)
    expect(progressPercent(status({ completed: 10 }))).toBe(100)
    expect(progressPercent(status({ total: 0, completed: 0 }))).toBe(0)
    expect(progressPercent(status({ completed: 12, total: 10 }))).toBe(100)
  })

  it('running 未收口，completed/cancelled 收口', () => {
    expect(isFinished(status())).toBe(false)
    expect(isFinished(status({ status: 'completed' }))).toBe(true)
    expect(isFinished(status({ status: 'cancelled' }))).toBe(true)
  })

  it('逐关状态映射到可读标签', () => {
    expect(itemView(item({ status: 'solved', solutionLength: 42 })).label).toBe('解长 42')
    expect(itemView(item({ status: 'solved' })).label).toBe('已解')
    expect(itemView(item({ status: 'failed' })).tone).toBe('bad')
    expect(itemView(item({ status: 'skipped' })).tone).toBe('muted')
    expect(itemView(item({ status: 'running' })).tone).toBe('running')
    // 未知状态原样显示且归入警示色——不静默当成功
    const unknown = itemView(item({ status: 'weird' }))
    expect(unknown.label).toBe('weird')
    expect(unknown.tone).toBe('warn')
  })

  it('失败关清单只含 failed', () => {
    const s = status({
      items: [
        item({ levelNumber: 1, status: 'solved', solutionLength: 10 }),
        item({ levelNumber: 2, status: 'failed' }),
        item({ levelNumber: 3, status: 'skipped' }),
      ],
    })
    expect(failedItems(s).map((i) => i.levelNumber)).toEqual([2])
  })

  it('摘要不粉饰失败', () => {
    const line = summaryLine(status({ failed: 3, skipped: 2 }))
    expect(line).toContain('成功 4')
    expect(line).toContain('失败 3')
    expect(line).toContain('跳过 2')
    expect(line).toContain('5/10')
  })
})

describe('任务级状态展示（不粉饰）', () => {
  it('进行中 / 已取消', () => {
    expect(runStatusView(status()).state).toBe('running')
    expect(runStatusView(status({ status: 'cancelled' })).label).toBe('已取消')
  })

  it('全部失败不能显示成绿色已完成', () => {
    const allFailed = status({ status: 'completed', total: 4, completed: 4, succeeded: 0, failed: 4 })
    expect(runStatusView(allFailed).label).toBe('全部失败')
    expect(runStatusView(allFailed).state).toBe('failed')
  })

  it('部分失败标注失败关数', () => {
    const view = runStatusView(status({ status: 'completed', succeeded: 8, failed: 2 }))
    expect(view.label).toContain('2 关失败')
    expect(view.state).toBe('failed')
  })

  it('全绿才是 passed', () => {
    const view = runStatusView(status({ status: 'completed', total: 10, completed: 10, succeeded: 10, failed: 0 }))
    expect(view).toEqual({ label: '已完成', state: 'passed' })
  })
})
