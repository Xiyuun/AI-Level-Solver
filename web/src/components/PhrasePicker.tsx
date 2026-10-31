// 短语库（设计决定 #1：无预置、用户自建、本地存储）：点击插入特殊要求，增删自理
import { useEffect, useState } from 'react'
import { addPhrase, listPhrases, removePhrase, type Phrase } from '../api/client'

export function PhrasePicker({ gameId, onInsert }: { gameId: string; onInsert: (text: string) => void }) {
  const [open, setOpen] = useState(false)
  const [phrases, setPhrases] = useState<Phrase[]>([])
  const [draft, setDraft] = useState('')
  const [error, setError] = useState('')

  const refresh = async () => {
    try {
      setPhrases(await listPhrases(gameId))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  useEffect(() => {
    if (open && phrases.length === 0) void refresh()
  }, [open, phrases.length]) // eslint-disable-line react-hooks/exhaustive-deps

  const add = async () => {
    const text = draft.trim()
    if (!text) return
    try {
      await addPhrase(gameId, text)
      setDraft('')
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <div className="phrase-picker">
      <button type="button" className="link" onClick={() => setOpen((v) => !v)}>
        短语库
      </button>
      {open && (
        <div className="phrase-panel">
          {phrases.length === 0 && <div className="phrase-empty">无预置短语：自建常用要求，点击即插入</div>}
          <ul>
            {phrases.map((phrase) => (
              <li key={phrase.id}>
                <button
                  type="button"
                  onClick={() => {
                    onInsert(phrase.text)
                    setOpen(false)
                  }}
                >
                  {phrase.text}
                </button>
                <button type="button" className="phrase-delete" onClick={() => void removePhrase(phrase.id).then(refresh)}>
                  删除
                </button>
              </li>
            ))}
          </ul>
          <div className="phrase-add">
            <input
              value={draft}
              placeholder="新短语，如：必须包含 3 只朝下的猪"
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void add()}
            />
            <button type="button" onClick={() => void add()}>
              添加
            </button>
          </div>
          {error && <div className="error-text">{error}</div>}
        </div>
      )}
    </div>
  )
}
