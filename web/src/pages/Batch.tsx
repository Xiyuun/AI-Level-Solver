// 批量生产页（表现层_批量生产 §2/§3）：编号段 + 难度配比 → 后台逐关跑 → 结果网格 → 入库/导出
// 页面可关：任务在后端队列执行，单机进程在任务就在（回到本页按 taskId 继续轮询）
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  adoptBatch,
  cancelBatch,
  exportBatch,
  getBatchStatus,
  retryBatchFailed,
  startBatch,
  type BatchStatus,
  type GameSummary,
} from '../api/client'
import {
  failedItems,
  isFinished,
  itemView,
  progressPercent,
  ratioTotal,
  spanLength,
  runStatusView,
  summaryLine,
  validateBatchForm,
  type BatchForm,
} from '../state/batch'

const POLL_MS = 1500

export function Batch({ game }: { game: GameSummary }) {
  const [form, setForm] = useState<BatchForm>({
    fromLevel: 111,
    toLevel: 130,
    difficulty1Count: 8,
    difficulty2Count: 12,
    difficulty3Count: 0,
    specialRequirements: '',
    concurrency: 4,
    candidateLimit: 3,
    solveTimeoutMs: 30000,
  })
  const [taskId, setTaskId] = useState<number | null>(null)
  const [status, setStatus] = useState<BatchStatus | null>(null)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [overwrite, setOverwrite] = useState(false)
  const timerRef = useRef<number | null>(null)

  const span = spanLength(form.fromLevel, form.toLevel)
  const ratios = ratioTotal(form)
  const formError = validateBatchForm(form)
  const remaining = span - ratios

  // 轮询：任务收口即停（页面关掉不影响后端执行）
  const poll = useCallback(async (id: number) => {
    try {
      const next = await getBatchStatus(id)
      setStatus(next)
      if (isFinished(next) && timerRef.current !== null) {
        window.clearInterval(timerRef.current)
        timerRef.current = null
      }
      return next
    } catch (e) {
      setError((e as Error).message)
      return null
    }
  }, [])

  useEffect(() => {
    if (taskId === null) return
    void poll(taskId)
    timerRef.current = window.setInterval(() => void poll(taskId), POLL_MS)
    return () => {
      if (timerRef.current !== null) {
        window.clearInterval(timerRef.current)
        timerRef.current = null
      }
    }
  }, [taskId, poll])

  const submit = async () => {
    setError('')
    setMessage('')
    if (formError) {
      setError(formError)
      return
    }
    setBusy(true)
    try {
      const started = await startBatch(game.gameId, {
        fromLevel: form.fromLevel,
        toLevel: form.toLevel,
        difficulty1Count: form.difficulty1Count,
        difficulty2Count: form.difficulty2Count,
        difficulty3Count: form.difficulty3Count,
        specialRequirements: form.specialRequirements.trim() || undefined,
        concurrency: form.concurrency,
        candidateLimit: form.candidateLimit,
        solveTimeoutMs: form.solveTimeoutMs,
      })
      setStatus(started)
      setTaskId(started.taskId)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const cancel = async () => {
    if (taskId === null) return
    try {
      await cancelBatch(taskId)
      setMessage('已发出取消指令：排队关不再执行，进行中的关会做完当前关')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const retry = async () => {
    if (taskId === null) return
    try {
      const next = await retryBatchFailed(taskId)
      setStatus(next)
      setTaskId(next.taskId)
      setMessage('已重新排队失败关')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  // 批量入库：默认整段；覆盖由用户显式勾选（落盘引擎另有冲突复查）
  const adopt = async () => {
    if (taskId === null || status === null) return
    setBusy(true)
    setError('')
    try {
      const result = await adoptBatch(taskId, form.fromLevel, form.toLevel, overwrite)
      setMessage(`入库完成：成功 ${result.ok}，失败 ${result.failed}（共 ${result.total}）`)
      if (result.failed > 0) {
        setError(result.details.filter((d) => !d.ok).map((d) => `${d.levelNumber}：${d.error}`).join('；'))
      }
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // 导出：下载 JSON 数组（每项含编号与原生关卡内容）
  const download = async () => {
    if (taskId === null) return
    try {
      const items = await exportBatch(taskId)
      const blob = new Blob([JSON.stringify(items, null, 2)], { type: 'application/json' })
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = `batch-${taskId}-levels.json`
      link.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const running = status !== null && !isFinished(status)
  const failed = status ? failedItems(status) : []
  const grid = useMemo(() => status?.items ?? [], [status])

  return (
    <div className="batch-page">
      {message && <div className="ok-message">{message}</div>}
      {error && <div className="error">{error}</div>}

      <div className="batch-layout">
        {/* 左：参数 */}
        <div className="batch-form">
          <h3>批量生产 · {game.displayName}</h3>

          <div className="batch-row">
            <label className="field">
              起始编号
              <input type="number" min={1} value={form.fromLevel}
                onChange={(e) => setForm({ ...form, fromLevel: Number(e.target.value) || 1 })} />
            </label>
            <label className="field">
              结束编号
              <input type="number" min={1} value={form.toLevel}
                onChange={(e) => setForm({ ...form, toLevel: Number(e.target.value) || 1 })} />
            </label>
          </div>
          <div className="note">编号段长度 {span} 关（难度配比总数必须等于它）</div>

          <div className="batch-row">
            <label className="field">
              难度 1 · 轻松
              <input type="number" min={0} value={form.difficulty1Count}
                onChange={(e) => setForm({ ...form, difficulty1Count: Number(e.target.value) || 0 })} />
            </label>
            <label className="field">
              难度 2 · 标准
              <input type="number" min={0} value={form.difficulty2Count}
                onChange={(e) => setForm({ ...form, difficulty2Count: Number(e.target.value) || 0 })} />
            </label>
            <label className="field">
              难度 3 · 烧脑
              <input type="number" min={0} value={form.difficulty3Count}
                onChange={(e) => setForm({ ...form, difficulty3Count: Number(e.target.value) || 0 })} />
            </label>
          </div>
          <div className={remaining === 0 ? 'note ok-text' : 'note warn-text'}>
            已分配 {ratios} / {span}
            {remaining > 0 ? `，还差 ${remaining}` : remaining < 0 ? `，超出 ${-remaining}` : '，正好'}
          </div>

          <label className="field">
            统一特殊要求（可选）
            <textarea value={form.specialRequirements}
              onChange={(e) => setForm({ ...form, specialRequirements: e.target.value })}
              placeholder="例：本批全部包含至少 2 个铁笼" />
          </label>

          <details className="advanced">
            <summary>高级选项</summary>
            <label className="field">
              并发数（1~8）
              <input type="number" min={1} max={8} value={form.concurrency}
                onChange={(e) => setForm({ ...form, concurrency: Number(e.target.value) || 4 })} />
            </label>
            <label className="field">
              每关候选上限（1~10）
              <input type="number" min={1} max={10} value={form.candidateLimit}
                onChange={(e) => setForm({ ...form, candidateLimit: Number(e.target.value) || 3 })} />
            </label>
            <label className="field">
              单候选求解超时（ms）
              <input type="number" min={1000} step={1000} value={form.solveTimeoutMs}
                onChange={(e) => setForm({ ...form, solveTimeoutMs: Number(e.target.value) || 30000 })} />
            </label>
          </details>

          <div className="form-actions">
            <button className="primary" disabled={busy || formError !== null} onClick={() => void submit()}>
              {busy ? '提交中…' : '开始批量生产'}
            </button>
            {running && <button onClick={() => void cancel()}>取消任务</button>}
          </div>
          {formError && <div className="error-text">⚠ {formError}</div>}
          <div className="note">
            任务在后台队列执行，关掉页面不影响进度；回到本页会继续显示。失败关不会阻塞其他关。
          </div>
        </div>

        {/* 右：运行监控与结果 */}
        <div className="batch-monitor">
          {!status && (
            <div className="stage-empty">
              <div className="stage-empty-art">▤</div>
              <div className="stage-empty-title">还没有批量任务</div>
              <div className="stage-empty-hint">
                左边填编号段与三档难度数量（总和必须等于编号段长度），开始后这里显示逐关进度。
                每关走和工作台完全相同的流水线：模型出题 → 求解判卷 → 难度评分 → 不合格自动重生。
              </div>
            </div>
          )}

          {status && (
            <>
              <div className="batch-head">
                {(() => {
                  const view = runStatusView(status)
                  return (
                    <span className={`state-chip state-${view.state}`}>
                      任务 #{status.taskId} · {view.label}
                    </span>
                  )
                })()}
                <span className="note">{summaryLine(status)}</span>
                <span className="note">总耗时 {(status.totalElapsedMs / 1000).toFixed(1)}s</span>
              </div>

              <div className="batch-progress">
                <div className="batch-progress-fill" style={{ width: `${progressPercent(status)}%` }} />
              </div>

              <div className="batch-grid">
                {grid.map((item) => {
                  const view = itemView(item)
                  return (
                    <div
                      key={item.levelNumber}
                      className={`batch-cell tone-${view.tone}`}
                      title={`${item.levelNumber}：${view.label}${item.failureSummary ? ` — ${item.failureSummary}` : ''}`}
                    >
                      <span className="batch-cell-no">{item.levelNumber}</span>
                      <span className="batch-cell-label">{view.label}</span>
                    </div>
                  )
                })}
              </div>

              {!running && (
                <div className="batch-actions">
                  <button className="primary" disabled={busy || status.succeeded === 0} onClick={() => void adopt()}>
                    批量入库（{status.succeeded} 关）
                  </button>
                  <label className="inline-check">
                    <input type="checkbox" checked={overwrite} onChange={(e) => setOverwrite(e.target.checked)} />
                    覆盖同编号旧文件
                  </label>
                  <button disabled={status.succeeded === 0} onClick={() => void download()}>导出 JSON</button>
                  {failed.length > 0 && <button onClick={() => void retry()}>重试失败关（{failed.length}）</button>}
                </div>
              )}

              <details className="batch-detail">
                <summary>逐关明细（{grid.length} 条）</summary>
                <table className="games-table">
                  <thead>
                    <tr><th>编号</th><th>状态</th><th>难度</th><th>解长</th><th>耗时</th><th>失败原因</th></tr>
                  </thead>
                  <tbody>
                    {grid.map((item) => (
                      <tr key={item.levelNumber}>
                        <td>{item.levelNumber}</td>
                        <td>{itemView(item).label}</td>
                        <td>{item.difficultyBand ?? '—'}</td>
                        <td>{item.solutionLength ?? '—'}</td>
                        <td>{(item.elapsedMs / 1000).toFixed(1)}s</td>
                        <td className="note">{item.failureSummary ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </details>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
