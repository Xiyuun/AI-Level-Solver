// 执行确认卡（§4.2）：当前文件路径/指纹/验证版本 + 确认状态机操作
// 人工降级（仅最终结果）/ 导入执行报告（指纹匹配）/ 确认加入基准（需执行通过+文件未变）
import { useCallback, useEffect, useState } from 'react'
import { approveBaseline, importReport, listConfirmations, recordManualResult, type Confirmation } from '../api/client'

export function ConfirmationCard({ gameId, levelId, refreshKey }: { gameId: string; levelId: string | null; refreshKey: number }) {
  const [items, setItems] = useState<Confirmation[]>([])
  const [error, setError] = useState('')
  const [person, setPerson] = useState('')
  const [note, setNote] = useState('')

  const refresh = useCallback(async () => {
    try {
      const all = await listConfirmations(gameId)
      setItems(levelId ? all.filter((c) => c.levelId === levelId) : all)
    } catch (e) {
      setError((e as Error).message)
    }
  }, [gameId, levelId])

  useEffect(() => {
    void refresh()
  }, [refresh, refreshKey])

  const act = async (action: () => Promise<unknown>) => {
    setError('')
    try {
      await action()
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const fileStatusText: Record<string, string> = {
    ok: '文件一致',
    missing: '文件缺失',
    hashChanged: '内容已变化',
  }

  return (
    <div className="confirmation-card">
      <h4>游戏执行确认 {levelId ? `· ${levelId}` : ''}</h4>
      {items.length === 0 && <div className="empty">暂无确认记录（采纳落盘后自动登记"待执行确认"）</div>}
      {items.map((item) => (
        <div key={item.id} className="confirmation-row">
          <span className={`state-chip state-${item.state}`}>
            {stateLabel(item.state)}
          </span>
          <span className="hash-line" title={item.levelHash}>
            {item.levelHash.slice(0, 20)}…
          </span>
          <span className={`file-status fs-${item.fileStatus}`}>{fileStatusText[item.fileStatus] ?? item.fileStatus}</span>
          {item.outcome && <span>结局 {item.outcome}</span>}
          {item.confirmedBy && <span>签认 {item.confirmedBy}</span>}
          {item.note && <span className="note">{item.note}</span>}
          {(item.state === 'pending' || item.state === 'failed') && item.fileStatus === 'ok' && (
            <span className="row-actions">
              <button onClick={() => void act(() => recordManualResult(item.id, 'win', person || '未署名', note || undefined))} disabled={!person}>
                人工·通关
              </button>
              <button onClick={() => void act(() => recordManualResult(item.id, 'lose', person || '未署名', note || undefined))} disabled={!person}>
                人工·判负
              </button>
              <button onClick={() => void act(() => importReport(item.id, item.levelHash, 'win'))}>导入报告·win</button>
            </span>
          )}
          {item.state === 'passed' && item.fileStatus === 'ok' && (
            <button onClick={() => void act(() => approveBaseline(item.id, person || '未署名'))} disabled={!person}>
              确认加入基准
            </button>
          )}
        </div>
      ))}
      <div className="confirm-inputs">
        <input placeholder="签认人（必填）" value={person} onChange={(e) => setPerson(e.target.value)} />
        <input placeholder="备注（可选）" value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
      {error && <div className="error">{error}</div>}
      <div className="dialog-note">
        人工降级仅记录最终结果；导入报告校验关卡指纹；"确认加入基准"需执行通过且文件未变（重复确认不重复追加）。
      </div>
    </div>
  )
}

// 状态中文名（五态状态机 §4.2）
function stateLabel(state: string): string {
  const labels: Record<string, string> = {
    pending: '待执行确认',
    running: '执行中',
    passed: '执行通过',
    failed: '执行失败',
    invalidated: '已失效',
  }
  return labels[state] ?? state
}
