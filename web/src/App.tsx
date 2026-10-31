// 应用壳：顶栏玩法切换 + 三页签（工作台 / 接入向导 / 设置）
import { useCallback, useEffect, useState } from 'react'
import { activateGame, getGame, listGames, type GameDetail, type GameSummary } from './api/client'
import { Onboarding } from './pages/Onboarding'
import { Settings } from './pages/Settings'
import { Workbench } from './pages/Workbench'

type Tab = 'workbench' | 'onboarding' | 'settings'

export function App() {
  const [tab, setTab] = useState<Tab>('workbench')
  const [games, setGames] = useState<GameSummary[]>([])
  const [activeGame, setActiveGame] = useState('')
  const [detail, setDetail] = useState<GameDetail | null>(null)
  const [error, setError] = useState('')

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

  return (
    <div className={`app theme-${String('')}`}>
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
        <nav className="tabs">
          <button className={tab === 'workbench' ? 'active' : ''} onClick={() => setTab('workbench')}>工作台</button>
          <button className={tab === 'onboarding' ? 'active' : ''} onClick={() => setTab('onboarding')}>接入向导</button>
          <button className={tab === 'settings' ? 'active' : ''} onClick={() => setTab('settings')}>设置</button>
        </nav>
      </header>

      {error && <div className="error">{error}</div>}

      {current && detail ? (
        tab === 'workbench' ? (
          <Workbench game={current} detail={detail} />
        ) : tab === 'onboarding' ? (
          <Onboarding game={current} detail={detail} onStatusChanged={() => void refreshAll()} />
        ) : (
          <Settings onGamesChanged={() => void refreshAll()} />
        )
      ) : (
        !error && <div className="empty big-empty">加载玩法详情…</div>
      )}
    </div>
  )
}
