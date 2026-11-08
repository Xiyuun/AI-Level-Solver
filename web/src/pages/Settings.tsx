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
import { applyTheme, resolveTheme } from '../state/theme'
import { LLM_PRESETS, normalizeBaseUrl } from '../state/llmPresets'
import { useToastFor } from '../state/useToastFor'

export function Settings({ onGamesChanged, onThemeChanged }: { onGamesChanged: () => void; onThemeChanged: () => void }) {
  const [baseUrl, setBaseUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [model, setModel] = useState('')
  const [candidateLimit, setCandidateLimit] = useState(3)
  const [solveTimeout, setSolveTimeout] = useState(30000)
  const [timeoutSeconds, setTimeoutSeconds] = useState(60)
  const [analysisBudget, setAnalysisBudget] = useState(200000)
  const [genBudget, setGenBudget] = useState(300000)
  const [theme, setTheme] = useState('light')
  const [testResult, setTestResult] = useState<{ ok: boolean; latencyMs: number; error: string | null; replyPreview: string | null } | null>(null)
  const [games, setGames] = useState<GameSummary[]>([])
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  useToastFor(message, error)

  const load = async () => {
    try {
      const [settings, list] = await Promise.all([getSettings(), listGames()])
      setBaseUrl(String(settings['llm.baseUrl'] ?? ''))
      setApiKey(String(settings['llm.apiKey'] ?? ''))
      setModel(String(settings['llm.model'] ?? ''))
      setCandidateLimit(Number(settings['gen.candidateLimit'] ?? 3))
      setSolveTimeout(Number(settings['gen.solveTimeoutMs'] ?? 30000))
      setTimeoutSeconds(Number(settings['llm.timeoutSeconds'] ?? 60))
      setAnalysisBudget(Number(settings['analyze.codeBudgetChars'] ?? 200000))
      setGenBudget(Number(settings['gen.codeBudgetChars'] ?? 300000))
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
        'llm.baseUrl': normalizeBaseUrl(baseUrl),
        'llm.apiKey': apiKey,
        'llm.model': model.trim(),
        'llm.timeoutSeconds': timeoutSeconds,
        'analyze.codeBudgetChars': analysisBudget,
        'gen.codeBudgetChars': genBudget,
        'gen.candidateLimit': candidateLimit,
        'gen.solveTimeoutMs': solveTimeout,
        'ui.theme': theme,
      })
      setMessage('已保存（本地存储，仅本实例可见）')
      onThemeChanged()   // 主题改动立即生效，不用刷新页面
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
        {/* 预设：工具只会拼 {地址}/chat/completions，所以这里填的是 OpenAI 兼容端点根地址 */}
        <div className="preset-row">
          <span className="note">快速填充：</span>
          {LLM_PRESETS.map((preset) => (
            <button
              key={preset.name}
              className="preset-btn"
              title={`${preset.baseUrl} · ${preset.model}`}
              onClick={() => {
                setBaseUrl(preset.baseUrl)
                setModel(preset.model)
              }}
            >
              {preset.name}
            </button>
          ))}
        </div>
        <label className="field">
          地址（工具自动拼 /chat/completions）
          <input value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="https://api.deepseek.com" />
        </label>
        <label className="field">
          API Key（明文存储于本机）
          <input value={apiKey} onChange={(e) => setApiKey(e.target.value)} type="password" />
        </label>
        <label className="field">
          模型（原样透传给服务端，以控制台列出的 id 为准）
          <input value={model} onChange={(e) => setModel(e.target.value)} placeholder="deepseek-chat" />
        </label>
        <label className="field">
          单次调用超时（秒）
          <input
            type="number"
            min={10}
            max={300}
            value={timeoutSeconds}
            onChange={(e) => setTimeoutSeconds(Number(e.target.value) || 60)}
          />
        </label>
        <div className="note">
          快模型（flash 类）30~60s 够用；带思考链的模型生成密集关卡 JSON 时可能要 120s 以上。
          超时只影响单次模型调用，不影响求解器。
        </div>
        <label className="field">
          深度分析·玩法代码正文预算（字符）
          <input
            type="number"
            min={20000}
            max={2000000}
            step={10000}
            value={analysisBudget}
            onChange={(e) => setAnalysisBudget(Number(e.target.value) || 200000)}
          />
        </label>
        <label className="field">
          平台内生成·玩法代码正文预算（字符）
          <input
            type="number"
            min={20000}
            max={2000000}
            step={10000}
            value={genBudget}
            onChange={(e) => setGenBudget(Number(e.target.value) || 300000)}
          />
        </label>
        <div className="note">
          玩法代码全文送入模型的上限（超出的文件以全量索引兜底并如实标注）。
          按模型上下文窗口调：1M 窗口模型建议 20万~40万；小窗口模型过大反而会截断报错。
          预算越大证据越全，但耗时与 token 费用同比例上涨。
        </div>
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
        <select
          value={theme}
          onChange={(e) => {
            setTheme(e.target.value)
            // 立即预览：切换即生效，点保存才落库（不落库就下次刷新回到旧主题）
            applyTheme(resolveTheme(e.target.value))
          }}
        >
          <option value="light">亮色</option>
          <option value="dark">暗色</option>
        </select>
        <div className="note">主题改动即时预览，点「保存」后持久化到本实例设置。</div>
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
