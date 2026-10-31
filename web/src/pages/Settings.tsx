// 设置页（表现层_设置）：LLM 三件套 + 测试连接、生成默认值、主题、短语库管理、玩法注册表最简管理
import { useEffect, useState } from 'react'
import {
  getSettings,
  listGames,
  saveSettings,
  testLlmConnection,
  type GameSummary,
} from '../api/client'
import { PhrasePicker } from '../components/PhrasePicker'

export function Settings({ onGamesChanged }: { onGamesChanged: () => void }) {
  const [baseUrl, setBaseUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [model, setModel] = useState('')
  const [candidateLimit, setCandidateLimit] = useState(3)
  const [solveTimeout, setSolveTimeout] = useState(30000)
  const [theme, setTheme] = useState('light')
  const [testResult, setTestResult] = useState<{ ok: boolean; latencyMs: number; error: string | null; replyPreview: string | null } | null>(null)
  const [games, setGames] = useState<GameSummary[]>([])
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  const load = async () => {
    try {
      const [settings, list] = await Promise.all([getSettings(), listGames()])
      setBaseUrl(String(settings['llm.baseUrl'] ?? ''))
      setApiKey(String(settings['llm.apiKey'] ?? ''))
      setModel(String(settings['llm.model'] ?? ''))
      setCandidateLimit(Number(settings['gen.candidateLimit'] ?? 3))
      setSolveTimeout(Number(settings['gen.solveTimeoutMs'] ?? 30000))
      setTheme(String(settings['ui.theme'] ?? 'light'))
      setGames(list)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  const save = async () => {
    setError('')
    setMessage('')
    try {
      await saveSettings({
        'llm.baseUrl': baseUrl.trim(),
        'llm.apiKey': apiKey,
        'llm.model': model.trim(),
        'gen.candidateLimit': candidateLimit,
        'gen.solveTimeoutMs': solveTimeout,
        'ui.theme': theme,
      })
      setMessage('已保存（本地存储，仅本实例可见）')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const test = async () => {
    setError('')
    setTestResult(null)
    try {
      // 未保存先测：当前表单值直接进测试请求
      setTestResult(await testLlmConnection({ baseUrl: baseUrl.trim(), apiKey, model: model.trim() }))
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <div className="settings-page">
      <h3>设置</h3>
      {message && <div className="ok-message">{message}</div>}
      {error && <div className="error">{error}</div>}

      <section className="settings-section">
        <h4>LLM API（实例自带，OpenAI 兼容）</h4>
        <label className="field">
          地址
          <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="https://api.example.com/v1" />
        </label>
        <label className="field">
          API Key（明文存储于本机）
          <input value={apiKey} onChange={(e) => setApiKey(e.target.value)} type="password" />
        </label>
        <label className="field">
          模型
          <input value={model} onChange={(e) => setModel(e.target.value)} placeholder="如 glm-4.7" />
        </label>
        <div className="form-actions">
          <button className="primary" onClick={() => void save()}>保存</button>
          <button onClick={() => void test()}>测试连接</button>
        </div>
        {testResult && (
          <div className={`llm-test ${testResult.ok ? 'ok' : 'bad'}`}>
            {testResult.ok
              ? `✓ 连接成功（${testResult.latencyMs}ms）：${testResult.replyPreview}`
              : `✗ ${testResult.error}`}
          </div>
        )}
      </section>

      <section className="settings-section">
        <h4>生成默认值</h4>
        <label className="field">
          候选上限（重生次数）
          <input type="number" min={1} max={10} value={candidateLimit} onChange={(e) => setCandidateLimit(Number(e.target.value) || 3)} />
        </label>
        <label className="field">
          单候选求解超时（ms）
          <input type="number" min={100} step={100} value={solveTimeout} onChange={(e) => setSolveTimeout(Number(e.target.value) || 30000)} />
        </label>
      </section>

      <section className="settings-section">
        <h4>主题</h4>
        <select value={theme} onChange={(e) => setTheme(e.target.value)}>
          <option value="light">亮色</option>
          <option value="dark">暗色</option>
        </select>
      </section>

      <section className="settings-section">
        <h4>短语库（按玩法分组）</h4>
        {games.map((g) => (
          <div key={g.gameId} className="phrase-game-row">
            <span>{g.displayName}</span>
            <PhrasePicker gameId={g.gameId} onInsert={() => onGamesChanged()} />
          </div>
        ))}
      </section>

      <section className="settings-section">
        <h4>玩法注册表（最简管理：查看；上线/状态变更走接入向导）</h4>
        <table className="games-table">
          <thead>
            <tr><th>玩法</th><th>状态</th><th>适配层版本</th><th>内核版本</th><th>门2</th><th>关卡数</th></tr>
          </thead>
          <tbody>
            {games.map((g) => (
              <tr key={g.gameId}>
                <td>{g.displayName}</td>
                <td>{g.statusName}</td>
                <td>{g.adapterVer}</td>
                <td>{g.kernelVer}</td>
                <td>{g.gate2Applicable ? '适用' : '不适用'}</td>
                <td>{g.levelCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}
