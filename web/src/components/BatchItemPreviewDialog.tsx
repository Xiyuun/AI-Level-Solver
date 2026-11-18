// 批量单关预览弹窗：自包含取数（单关详情 → 磁带全文），body 复用工作台同一套 PreviewPane
// 回放/试玩/单步/倍速全部来自 PreviewPane，这里只负责把批量条目的数据凑齐（2026-11-16 批量预览）
import { useEffect, useState } from 'react'
import { getBatchItem, getTapeJson, type BatchItemDetail } from '../api/client'
import type { SolutionTapeMirror, VisualMapping } from '../renderer/model'
import { PreviewPane } from './PreviewPane'

export function BatchItemPreviewDialog({
  gameId,
  mapping,
  taskId,
  levelNumber,
  onClose,
}: {
  gameId: string
  mapping: VisualMapping
  taskId: number
  levelNumber: number
  onClose: () => void
}) {
  const [detail, setDetail] = useState<BatchItemDetail | null>(null)
  const [tape, setTape] = useState<SolutionTapeMirror | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  // 挂载即取数：单关详情（题面 + 磁带引用）→ 磁带全文（含每步快照，驱动帧回放）
  useEffect(() => {
    let alive = true
    setLoading(true)
    setError('')
    setDetail(null)
    setTape(null)
    void (async () => {
      try {
        const item = await getBatchItem(taskId, levelNumber)
        if (!alive) return
        setDetail(item)
        if (item.tapeId !== null) {
          const tapeJson = await getTapeJson(item.tapeId)
          if (alive) setTape(tapeJson)
        }
      } catch (e) {
        if (alive) setError((e as Error).message)
      } finally {
        if (alive) setLoading(false)
      }
    })()
    return () => {
      alive = false
    }
  }, [taskId, levelNumber])

  return (
    <div className="dialog-backdrop">
      <div className="dialog dialog-preview">
        <h3>关卡 {levelNumber} · 预览与回放</h3>
        {error && <div className="error">{error}</div>}
        {loading ? (
          <div className="stage-empty">
            <div className="stage-empty-title">加载中…</div>
            <div className="stage-empty-hint">正在取回该关题面与解法磁带。</div>
          </div>
        ) : detail ? (
          <>
            <div className="batch-preview-facts">
              <span>状态：{detail.status === 'solved' ? '已解' : detail.status}</span>
              <span>难度：{detail.difficultyBand ?? '—'}</span>
              <span>解长：{detail.solutionLength ?? '—'}</span>
              <span>回放步数：{tape ? tape.steps.length : '—'}</span>
            </div>
            {detail.status === 'solved' ? (
              <PreviewPane gameId={gameId} levelJson={detail.contentJson} tape={tape} mapping={mapping} />
            ) : (
              <div className="stage-empty">
                <div className="stage-empty-title">该关没有可预览的内容</div>
                <div className="stage-empty-hint">
                  {detail.failureSummary ?? '只有"已解"的关卡才有题面与磁带。'}
                </div>
              </div>
            )}
          </>
        ) : null}
        <div className="dialog-actions">
          <button onClick={onClose}>关闭</button>
        </div>
      </div>
    </div>
  )
}
