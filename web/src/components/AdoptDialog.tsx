// 采纳落盘二次确认弹窗（§4 设计决定 #2）：编号/难度评估/解长及最优性/目标路径/文件指纹
// 编号冲突 → 展示旧文件指纹，确认替换（带 expectedOldHash 防并发覆盖）
import { useState } from 'react'
import { adopt, AdoptConflictError, type AdoptResult } from '../api/client'

export interface AdoptTarget {
  levelJson: string
  source: 'generated' | 'imported'
  levelId: string | null
  difficulty: string // 评估描述
  solution: string // 解长及最优性描述
}

export function AdoptDialog({
  gameId,
  levelsRoot,
  target,
  onClose,
  onAdopted,
}: {
  gameId: string
  levelsRoot: string
  target: AdoptTarget
  onClose: () => void
  onAdopted: (result: AdoptResult) => void
}) {
  const [busy, setBusy] = useState(false)
  const [conflict, setConflict] = useState<AdoptConflictError | null>(null)
  const [error, setError] = useState('')

  const doAdopt = async (overwrite: boolean, expectedOldHash?: string) => {
    setBusy(true)
    setError('')
    try {
      const result = await adopt(gameId, target.levelJson, target.source, overwrite, expectedOldHash)
      onAdopted(result)
    } catch (e) {
      if (e instanceof AdoptConflictError) {
        setConflict(e) // 内容不同且未覆盖：显示旧指纹，交人工决定
      } else {
        setError((e as Error).message)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-backdrop">
      <div className="dialog">
        <h3>采纳落盘确认</h3>
        <dl className="adopt-facts">
          <dt>关卡编号</dt>
          <dd>{target.levelId ?? '（自动从内容提取）'}</dd>
          <dt>难度评估</dt>
          <dd>{target.difficulty}</dd>
          <dt>解长及最优性</dt>
          <dd>{target.solution}</dd>
          <dt>目标路径</dt>
          <dd>
            {levelsRoot}/{target.levelId ?? '…'}.json
          </dd>
        </dl>
        {conflict ? (
          <div className="conflict-box">
            <div>{conflict.message}</div>
            <div className="hash-line">旧文件指纹：{conflict.conflictHash}</div>
            <div className="dialog-actions">
              <button disabled={busy} onClick={() => void doAdopt(true, conflict.conflictHash ?? undefined)}>
                确认替换（旧指纹将提交复查）
              </button>
              <button onClick={onClose}>取消</button>
            </div>
          </div>
        ) : (
          <div className="dialog-actions">
            <button disabled={busy} onClick={() => void doAdopt(false)}>
              {busy ? '写入中…' : '确认落盘'}
            </button>
            <button onClick={onClose}>取消</button>
          </div>
        )}
        {error && <div className="error">{error}</div>}
        <div className="dialog-note">落盘后进入"待游戏执行确认"；人工确认后才加入基准。</div>
      </div>
    </div>
  )
}
