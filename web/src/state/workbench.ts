// 生成工作台的纯逻辑层：页面状态机、失败建议映射、流水线日志构造、机制范围提示
// 全部纯函数（vitest 直接测），组件只做展示与装配（表现层_生成工作台 §3/§5）

import type { CandidateAttempt, GenerationRun } from '../api/client'

// ---- 页面状态机（§3）----

export type Phase = 'idle' | 'generating' | 'success' | 'failed'

export type PhaseEvent = 'start' | 'cancel' | 'succeed' | 'exhaust' | 'apiError'

// 状态流转：空态→生成中→成功/失败；失败态应用建议重试→生成中；任何终态可重新开始
export function nextPhase(phase: Phase, event: PhaseEvent): Phase {
  switch (event) {
    case 'start':
      return 'generating'
    case 'cancel':
      return 'idle'
    case 'succeed':
      return phase === 'generating' ? 'success' : phase
    case 'exhaust':
    case 'apiError':
      return phase === 'generating' ? 'failed' : phase
  }
}

// ---- 失败建议（§5：失败即自动应用建议直接重试，字段高亮"由系统调整"可一键还原）----

export interface FailureSuggestions {
  candidateLimit?: number // 提高候选上限
  solveTimeoutMs?: number // 增加求解超时
  targetDifficulty?: number // 必要时降难度
  specialRequirements?: string // 放宽特殊要求（清空）
  reasons: string[] // 每条建议的依据（报告展示）
}

// 失败分类 → 建议映射；未产生有效建议返回 null（停留失败态等人工）
export function suggestionsFrom(run: GenerationRun): FailureSuggestions | null {
  if (run.status !== 'failed' || run.candidates.length === 0) return null
  const result: FailureSuggestions = { reasons: [] }
  const classes = run.candidates.map((c) => c.failureClass)

  // 预算耗尽：求解超时加倍（封顶 2 分钟）
  if (classes.includes('resource_limit')) {
    const doubled = Math.min(run.solveTimeoutMs * 2, 120000)
    result.solveTimeoutMs = doubled
    result.reasons.push(`出现预算耗尽候选：求解超时 ${run.solveTimeoutMs}ms → ${doubled}ms`)
  }
  // 内容失败烧完候选：上限 +2（封顶 10）
  const contentFailures = classes.filter((c) =>
    c === 'unsolvable' || c === 'format_invalid' || c === 'difficulty_miss' || c === 'constraint_violation')
  if (contentFailures.length === classes.length && run.candidateLimit < 10) {
    const raised = Math.min(run.candidateLimit + 2, 10)
    result.candidateLimit = raised
    result.reasons.push(`候选已耗尽（${run.candidateLimit}）：候选上限 → ${raised}`)
  }
  // 全部难度不达标且还有下降空间：降一档
  if (classes.every((c) => c === 'difficulty_miss') && run.targetDifficulty > 1) {
    result.targetDifficulty = run.targetDifficulty - 1
    result.reasons.push(`连续难度不达标：目标难度 ${run.targetDifficulty} 档 → ${run.targetDifficulty - 1} 档`)
  }
  // 布局类失败 + 有特殊要求：放宽要求（标为建议，由系统调整可还原）
  const layoutFailures = classes.filter((c) => c === 'unsolvable' || c === 'difficulty_miss' || c === 'constraint_violation')
  if (layoutFailures.length > 0 && (run.specialRequirements ?? '').trim().length > 0) {
    result.specialRequirements = ''
    result.reasons.push('布局类失败且存在特殊要求：先放宽特殊要求')
  }
  return result.reasons.length > 0 ? result : null
}

// ---- 流水线日志（§1⑩：五色角色标签行；候选迭代与重生触发全部可见）----

export type LogRole = 'llm' | 'solver' | 'difficulty' | 'constraint' | 'integration'

export interface LogEntry {
  role: LogRole
  text: string
}

// 运行记录 → 日志行：逐候选（出题→判卷→难度→分类→重生）+ 运行级结论
export function logEntriesFromRun(run: GenerationRun): LogEntry[] {
  const entries: LogEntry[] = []
  entries.push(integration(`生成开始：目标难度 ${run.targetDifficulty} 档，候选上限 ${run.candidateLimit}，求解超时 ${run.solveTimeoutMs}ms`))
  for (const candidate of run.candidates) {
    entries.push(llm(`第 ${candidate.attemptIndex} 次出题（${candidate.llmCalls} 次调用${candidate.llmCalls > 1 ? '，含格式修复' : ''}）`))
    if (candidate.failureClass === 'api_error') {
      entries.push(integration(`第 ${candidate.attemptIndex} 次尝试 API 失败：${candidate.failureDetail ?? ''}`))
      continue
    }
    if (candidate.failureClass === 'format_invalid') {
      entries.push(integration(`第 ${candidate.attemptIndex} 次候选格式未通过：${short(candidate.failureDetail)}`))
    } else {
      entries.push(solver(`第 ${candidate.attemptIndex} 次判卷：${candidate.solveRunId === null ? '未进入求解' : `求解运行 #${candidate.solveRunId}`}`))
      if (candidate.difficultyBand !== null) {
        entries.push(difficulty(`难度评估 ${candidate.difficultyScore?.toFixed(1)}（第 ${candidate.difficultyBand} 档）`))
      } else {
        entries.push(difficulty('难度不适用（无解/未知不评分）'))
      }
      entries.push(constraint('约束检查：未配置可机检约束（特殊要求由策划预览判断）'))
      entries.push(integration(`第 ${candidate.attemptIndex} 次结论：${candidate.failureClass} —— ${short(candidate.failureDetail)}`))
    }
    // 重生触发：非末次的失败候选都会回填反馈（单候选失败不打扰策划，只在日志可见 §3）
    if (candidate.attemptIndex < run.candidateLimit && candidate.failureClass !== 'none') {
      entries.push(integration(`已回填失败反馈，触发第 ${candidate.attemptIndex + 1} 次重生`))
    }
  }
  if (run.status === 'succeeded') {
    entries.push(integration(`运行成功：第 ${run.adoptedAttempt} 次尝试被采纳（总耗时 ${run.totalElapsedMs}ms）`))
  } else if (run.status === 'failed') {
    entries.push(integration(`运行失败：${run.failureSummary ?? '候选已耗尽'}`))
  } else {
    entries.push(integration('运行已取消'))
  }
  return entries
}

// ---- 机制范围提示（§4：编号下方灰字显示该编号的机制解锁范围）----

// 机制表 + 编号 → 提示文案；编号非数字或不在任何区间返回空串
export function mechanicsHint(mechanics: unknown, levelNumber: string): string {
  const number = Number(levelNumber)
  if (levelNumber.trim() === '' || !Number.isFinite(number)) return ''
  const ranges = (mechanics as { ranges?: { from?: number; to?: number; mechanics?: string[]; note?: string }[] } | null)?.ranges
  if (!Array.isArray(ranges)) return ''
  for (const range of ranges) {
    if (typeof range.from === 'number' && typeof range.to === 'number' && number >= range.from && number <= range.to) {
      const parts = [
        range.mechanics?.length ? `可用机制：${range.mechanics.join('、')}` : '',
        range.note ?? '',
      ].filter((s) => s.length > 0)
      return parts.length > 0 ? `${range.from}–${range.to}：${parts.join('；')}` : ''
    }
  }
  return ''
}

// ---- 候选记录条（§1⑬）----

// 候选结论的展示标签
export function candidateLabel(candidate: CandidateAttempt): string {
  const labels: Record<string, string> = {
    none: '通过',
    api_error: 'API失败',
    format_invalid: '格式不合法',
    unsolvable: '模型内无解',
    resource_limit: '预算耗尽',
    difficulty_miss: '难度不达标',
    constraint_violation: '约束冲突',
    solve_error: '求解异常',
  }
  return labels[candidate.failureClass] ?? candidate.failureClass
}

// ---- 内部工具 ----

function llm(text: string): LogEntry { return { role: 'llm', text } }
function solver(text: string): LogEntry { return { role: 'solver', text } }
function difficulty(text: string): LogEntry { return { role: 'difficulty', text } }
function constraint(text: string): LogEntry { return { role: 'constraint', text } }
function integration(text: string): LogEntry { return { role: 'integration', text } }

// 详情截断（日志行不过长）
function short(text: string | null): string {
  const value = text ?? ''
  return value.length > 80 ? `${value.slice(0, 80)}…` : value
}
