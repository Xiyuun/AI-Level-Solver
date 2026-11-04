// 关卡导入面板（接入向导③产物之一：原生关卡批量入库）
// 动机：过去只返回"成功 N / 跳过 M"的计数，110 关里 35 关被跳过却说不清是哪 35 关、为什么——
// 现在逐关给结论，并支持把跳过清单导出给策划核对。
import { useMemo, useState } from 'react'
import { importLevels, type LevelImportEntry, type LevelImportResult } from '../api/client'

// 三栏分组：成功 / 跳过 / 失败（顺序与"该关心什么"一致）
const COLUMNS: { key: LevelImportEntry['outcome']; title: string; hint: string }[] = [
  { key: 'imported', title: '成功入库', hint: '格式通过、已进入平台关卡库' },
  { key: 'skipped', title: '跳过（子集外）', hint: '玩法声明支持的子集之外的机制或类型' },
  { key: 'failed', title: '失败', hint: '格式/解析问题——这是需要修的' },
]

export function ImportPanel({ gameId, defaultDirectory }: { gameId: string; defaultDirectory: string }) {
  const [directory, setDirectory] = useState(defaultDirectory)
  const [result, setResult] = useState<LevelImportResult | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const grouped = useMemo(() => {
    const map: Record<string, LevelImportEntry[]> = { imported: [], skipped: [], failed: [] }
    for (const entry of result?.entries ?? []) {
      ;(map[entry.outcome] ??= []).push(entry)
    }
    return map
  }, [result])

  const run = async () => {
    setError('')
    setBusy(true)
    try {
      setResult(await importLevels(gameId, directory.trim()))
    } catch (e) {
      setError((e as Error).message)
      setResult(null)
    } finally {
      setBusy(false)
    }
  }

  // 导出跳过清单：策划拿这个去核对"哪些关这次进不来、为什么"
  const exportSkipped = () => {
    const rows = ['文件,关卡编号,结论,原因']
    for (const entry of result?.entries ?? []) {
      if (entry.outcome === 'imported') continue
      rows.push([entry.file, entry.levelId ?? '', entry.outcome, (entry.reason ?? '').replaceAll(',', '，')].join(','))
    }
    const blob = new Blob(['\uFEFF' + rows.join('\n')], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `import-report-${gameId}.csv`
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="import-panel">
      <div className="ob-form-row">
        <input
          value={directory}
          onChange={(e) => setDirectory(e.target.value)}
          placeholder="游戏项目关卡目录绝对路径"
          title="平台只读取该目录，不改动游戏项目任何文件"
        />
        <button className="primary" disabled={!directory.trim() || busy} onClick={() => void run()}>
          {busy ? '导入中…' : '导入原生关卡'}
        </button>
        {result && (result.skippedUnsupported > 0 || result.failed > 0) && (
          <button onClick={exportSkipped}>导出清单 CSV</button>
        )}
      </div>
      {error && <div className="error-text">{error}</div>}

      {result && (
        <>
          <div className="import-summary">
            共 {result.imported + result.skippedUnsupported + result.failed} 个文件：
            <b className="ok-text">{result.imported} 入库</b> ·
            <b className="warn-text">{result.skippedUnsupported} 跳过</b> ·
            <b className="bad-text">{result.failed} 失败</b>
          </div>
          <div className="import-columns">
            {COLUMNS.map((col) => {
              const entries = grouped[col.key] ?? []
              return (
                <div key={col.key} className={`import-column import-${col.key}`}>
                  <div className="import-column-head">
                    <b>{col.title}</b>
                    <span className="import-count">{entries.length}</span>
                  </div>
                  <div className="import-column-hint">{col.hint}</div>
                  <ul>
                    {entries.slice(0, 60).map((entry) => (
                      <li key={entry.file} title={entry.reason ?? undefined}>
                        <span className="import-file">{entry.levelId ?? entry.file}</span>
                        {entry.reason && <span className="import-reason">{entry.reason}</span>}
                      </li>
                    ))}
                    {entries.length > 60 && <li className="note">…共 {entries.length} 条，已折叠</li>}
                    {entries.length === 0 && <li className="note">无</li>}
                  </ul>
                </div>
              )
            })}
          </div>
        </>
      )}
    </div>
  )
}
