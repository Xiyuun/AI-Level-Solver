// 应用壳：顶栏玩法切换 + 导入项目 + 四页签（工作台 / 批量 / 接入向导 / 设置）+ 主题落地
import { useCallback, useEffect, useState } from 'react'
import { activateGame, getGame, getSettings, listGames, type GameDetail, type GameSummary } from './api/client'
import { ImportProjectDialog } from './components/ImportProjectDialog'
import { ToastHost } from './components/ToastHost'
import { Batch } from './pages/Batch'
import { Onboarding } from './pages/Onboarding'
import { Settings } from './pages/Settings'
import { Workbench } from './pages/Workbench'
import { applyTheme, resolveTheme, ThemeContext, type Theme } from './state/theme'

type Tab = 'workbench' | 'batch' | 'onboarding' | 'settings'

export function App() {
  const [tab, setTab] = useState<Tab>('workbench')
  const [games, setGames] = useState<GameSummary[]>([])
  const [activeGame, setActiveGame] = useState('')
  const [detail, setDetail] = useState<GameDetail | null>(null)
  const [error, setError] = useState('')
  const [theme, setTheme] = useState<Theme>('light')
  const [importing, setImporting] = useState(false)

  // 主题：设置页存 ui.theme，这里读出来写到 <html data-theme>
  // （此前 render 里写的是 theme-${String('')}，主题永远等于空串，设置页那个下拉框从来没生效过）
  const refreshTheme = useCallback(async () => {
    try {
      const settings = await getSettings()
      const next: Theme = resolveTheme(settings['ui.theme'])
      setTheme(next)
      applyTheme(next)
    } catch {
      // 读不到就维持当前主题：主题不该阻断主流程
    }
  }, [])

  useEffect(() => {
    void refreshTheme()
  }, [refreshTheme])

  const refreshGames = useCallback(async () => {
    try {
      const list = await listGames()
      setGames(list)
      setActiveGame((current) => (list.some((g) => g.gameId === current) ? current : list[0]?.gameId ?? ''))
      return list
    } catch (e) {
      setError(`玩法列表加载失败：${(e as Error).message}（确认平台已启动：dotnet run --project src/LevelForge.Platform --urls http://localhost:5000）`)
      return []
    }
  }, [])

  useEffect(() => {
    void refreshGames()
  }, [refreshGames])

  // 活跃玩法详情（视觉映射/机制表/关卡根目录）；状态可能被向导/设置页改掉，onStatusChanged 时重取
  const refreshDetail = useCallback(async () => {
    if (!activeGame) return
    try {
      setDetail(await getGame(activeGame))
    } catch (e) {
      setError((e as Error).message)
    }
  }, [activeGame])

  useEffect(() => {
    setDetail(null)
    void refreshDetail()
  }, [refreshDetail])

  const refreshAll = useCallback(async () => {
    const list = await refreshGames()
    // 列表刷新后重取详情（状态标识/适用性可能变化）
    const current = list.find((g) => g.gameId === activeGame)
    if (current) {
      try {
        setDetail(await getGame(activeGame))
      } catch {
        undefined
      }
    }
  }, [refreshGames, refreshDetail, activeGame])

  const current = games.find((g) => g.gameId === activeGame) ?? null

  // 导入成功：激活新玩法并直接进入其接入向导（用户流程第②→③步的无缝衔接）
  const handleImported = useCallback(async (gameId: string) => {
    setImporting(false)
    const list = await refreshGames()
    const fresh = list.find((g) => g.gameId === gameId)
    setActiveGame(gameId)
    setTab('onboarding')
    if (fresh) {
      void activateGame(gameId).catch(() => undefined)
    }
  }, [refreshGames])

  return (
    <ThemeContext.Provider value={theme}>
      <div className="app">
        <header className="topbar">
          <span className="brand">LevelForge</span>
          <select
            value={activeGame}
            onChange={(e) => {
              const gameId = e.target.value
              setActiveGame(gameId)
              void activateGame(gameId).catch((err) => setError((err as Error).message))
            }}
          >
            {games.map((g) => (
              <option key={g.gameId} value={g.gameId}>
                {g.displayName}（{g.statusName}）
              </option>
            ))}
          </select>
          <button className="import-project-btn" onClick={() => setImporting(true)} title="导入 Cocos/Unity 项目目录，登记为待接入玩法">
            ＋ 导入项目
          </button>
          <nav className="tabs">
            <button className={tab === 'workbench' ? 'active' : ''} onClick={() => setTab('workbench')}>工作台</button>
            <button className={tab === 'batch' ? 'active' : ''} onClick={() => setTab('batch')}>批量</button>
            <button className={tab === 'onboarding' ? 'active' : ''} onClick={() => setTab('onboarding')}>接入向导</button>
            <button className={tab === 'settings' ? 'active' : ''} onClick={() => setTab('settings')}>设置</button>
          </nav>
        </header>

        {error && <div className="error">{error}</div>}

        {importing && (
          <ImportProjectDialog onClose={() => setImporting(false)} onImported={(r) => void handleImported(r.gameId)} />
        )}

        {current && detail ? (
          tab === 'workbench' ? (
            <Workbench game={current} detail={detail} onOpenOnboarding={() => setTab('onboarding')} />
          ) : tab === 'batch' ? (
            <Batch game={current} />
          ) : tab === 'onboarding' ? (
            <Onboarding
              game={current}
              detail={detail}
              onStatusChanged={() => void refreshAll()}
              onProjectDeleted={() => {
                // 项目已删：refreshGames 发现当前玩法不在列表里会自动回落到第一个玩法
                void refreshGames()
              }}
            />
          ) : (
            <Settings onGamesChanged={() => void refreshAll()} onThemeChanged={() => void refreshTheme()} />
          )
        ) : (
          !error && <div className="empty big-empty">加载玩法详情…</div>
        )}
        <ToastHost />
      </div>
    </ThemeContext.Provider>
  )
}
