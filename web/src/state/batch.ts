// 批量生产的纯逻辑：配比校验 / 进度汇总 / 逐关状态归一
// 组件只做展示与装配；所有判断都在这里，便于单测（表现层_批量生产 §3/§4）
import type { BatchItem, BatchStatus } from '../api/client'

// 表单值（与 BatchStartRequest 同形，但不含 gameId）
export interface BatchForm {
  fromLevel: number
  toLevel: number
  difficulty1Count: number
  difficulty2Count: number
  difficulty3Count: number
  specialRequirements: string
  concurrency: number
  candidateLimit: number
  solveTimeoutMs: number
}

// 编号段长度（起止非法时返回 0，不让负数参与后续判断）
export function spanLength(fromLevel: number, toLevel: number): number {
  if (!Number.isFinite(fromLevel) || !Number.isFinite(toLevel)) return 0
  if (fromLevel < 1 || toLevel < fromLevel) return 0
  return toLevel - fromLevel + 1
}

// 配比总数（三档之和）
export function ratioTotal(form: Pick<BatchForm, 'difficulty1Count' | 'difficulty2Count' | 'difficulty3Count'>): number {
  return (form.difficulty1Count || 0) + (form.difficulty2Count || 0) + (form.difficulty3Count || 0)
}

// 提交前校验：返回第一条错误；通过返回 null
// 与后端 BatchService.Start 的规则保持一致（前端先拦一次，避免白发一轮请求）
export function validateBatchForm(form: BatchForm): string | null {
  if (form.fromLevel < 1) return '起始编号必须 ≥1'
  if (form.toLevel < form.fromLevel) return `结束编号 ${form.toLevel} 小于起始编号 ${form.fromLevel}`
  const span = spanLength(form.fromLevel, form.toLevel)
  if (span > 500) return `编号段跨度 ${span} 超过上限 500`
  const total = ratioTotal(form)
  if (total !== span) return `难度配比总数 ${total} ≠ 编号段长度 ${span}`
  if (form.concurrency < 1 || form.concurrency > 8) return '并发数须在 1~8 之间'
  if (form.candidateLimit < 1 || form.candidateLimit > 10) return '候选上限须在 1~10 之间'
  return null
}

// 进度条百分比（0~100 整数；总数为 0 时按 0）
export function progressPercent(status: BatchStatus): number {
  if (status.total <= 0) return 0
  return Math.min(100, Math.round((status.completed / status.total) * 100))
}

// 任务是否已收口（可做入库/导出）
export function isFinished(status: BatchStatus): boolean {
  return status.status !== 'running'
}

// 逐关状态 → 展示标签与色号（前端不猜，未知状态原样显示并归入中性色）
export interface ItemView {
  label: string
  tone: 'ok' | 'bad' | 'warn' | 'muted' | 'running'
}

export function itemView(item: BatchItem): ItemView {
  switch (item.status) {
    case 'solved':
      return { label: item.solutionLength !== null ? `解长 ${item.solutionLength}` : '已解', tone: 'ok' }
    case 'failed':
      return { label: '失败', tone: 'bad' }
    case 'skipped':
      return { label: '跳过', tone: 'muted' }
    case 'running':
      return { label: '进行中', tone: 'running' }
    case 'pending':
      return { label: '排队', tone: 'muted' }
    default:
      return { label: item.status, tone: 'warn' }
  }
}

// 失败关清单（重试按钮可用性 + 结果表排序用）
export function failedItems(status: BatchStatus): BatchItem[] {
  return status.items.filter((item) => item.status === 'failed')
}

// 结果摘要一句话（给页面顶部用；不粉饰失败）
export function summaryLine(status: BatchStatus): string {
  const parts = [
    `共 ${status.total} 关`,
    `成功 ${status.succeeded}`,
    `失败 ${status.failed}`,
    status.skipped > 0 ? `跳过 ${status.skipped}` : '',
    `进度 ${status.completed}/${status.total}`,
  ].filter(Boolean)
  return parts.join(' · ')
}

// 任务级状态的展示口径：后端 status 只表达"跑没跑完"（running/completed/cancelled），
// 不代表"结果好不好"。全都失败也是 completed——界面若显示绿色"已完成"就是粉饰。
export interface RunStatusView {
  label: string
  state: 'running' | 'passed' | 'failed' | 'pending'
}

export function runStatusView(status: BatchStatus): RunStatusView {
  if (status.status === 'running') return { label: '进行中', state: 'running' }
  if (status.status === 'cancelled') return { label: '已取消', state: 'pending' }
  if (status.total > 0 && status.failed === status.total) return { label: '全部失败', state: 'failed' }
  if (status.failed > 0) return { label: `已完成（${status.failed} 关失败）`, state: 'failed' }
  return { label: '已完成', state: 'passed' }
}
