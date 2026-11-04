// 项目导入弹窗（用户流程第②步）：输入项目目录绝对路径 → 后端识别 Unity/Cocos → 登记玩法并进向导
// 浏览器拿不到原生目录选择框：路径输入 + 最近导入目录快捷回填（后端 settings 记忆，跨浏览器会话）
import { useEffect, useState } from 'react'
import { importProject, recentProjects, type ProjectImportResult } from '../api/client'

export function ImportProjectDialog({
  onClose,
  onImported,
}: {
  onClose: () => void
  onImported: (result: ProjectImportResult) => void
}) {
  const [directory, setDirectory] = useState('')
  const [recent, setRecent] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    void recentProjects().then(setRecent).catch(() => undefined)   // 快捷列表读不到就为空，不阻断
  }, [])

  const doImport = async () => {
    if (!directory.trim()) {
      setError('请输入项目目录的绝对路径（例如 E:\\Projects\\CocosProjects\\MyGame）')
      return
    }
    setBusy(true)
    setError('')
    try {
      const result = await importProject(directory.trim())
      onImported(result)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-backdrop">
      <div className="dialog">
        <h3>导入项目</h3>
        <div className="dialog-note" style={{ marginTop: 0 }}>
          选择设备上的 Cocos / Unity 项目目录，工具会按目录名登记为一个待接入的玩法，然后跟随接入向导制作求解器适配层。
        </div>
        <label className="field" style={{ marginTop: 12 }}>
          项目目录（绝对路径）
          <input
            value={directory}
            onChange={(e) => setDirectory(e.target.value)}
            placeholder="E:\Projects\CocosProjects\MyGame"
            onKeyDown={(e) => { if (e.key === 'Enter' && !busy) void doImport() }}
            autoFocus
          />
        </label>
        {recent.length > 0 && (
          <div className="import-recent">
            <span className="dialog-note">最近导入：</span>
            {recent.map((dir) => (
              <button key={dir} className="import-recent-item" onClick={() => setDirectory(dir)} title={dir}>
                {dir}
              </button>
            ))}
          </div>
        )}
        {error && <div className="error">{error}</div>}
        <div className="dialog-actions">
          <button className="primary" disabled={busy} onClick={() => void doImport()}>
            {busy ? '识别中…' : '识别并导入'}
          </button>
          <button onClick={onClose}>取消</button>
        </div>
      </div>
    </div>
  )
}
