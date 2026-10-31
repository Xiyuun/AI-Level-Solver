// 应用壳：顶栏玩法切换（含状态标识）+ 生成工作台（第 13 步主页面）
import { useCallback, useEffect, useState } from 'react'
import { activateGame, getGame, listGames, type GameDetail, type GameSummary } from './api/client'
import { Workbench } from './pages/Workbench'

export function App() {
  const [games, setGames] = useState<GameSummary[]>([])
  const [activeGame, setActiveGame] = useState('')
  const [detail, setDetail] = useState<GameDetail | null>(null)
  const [error, setError] = useState('')

  const refreshGames = useCallback(async () => {
    try {
      const list = await listGames()
      setGames(list)
      setActiveGame((current) => (list.some((g) => g.gameId === current) ? current : list[0]?.gameId ?? ''))
    } catch (e) {
      setError(`玩法列表加载失败：${(e as Error).message}（确认平台已启动：dotnet run --project src/LevelForge.Platform --urls http://localhost:5000）`)
    }
  }, [])

  useEffect(() => {
    void refreshGames()
  }, [refreshGames])

  // 活跃玩法详情（视觉映射/机制表/关卡根目录）
  useEffect(() => {
    if (!activeGame) return
    setError('')
    setDetail(null)
    void getGame(activeGame)
      .then(setDetail)
      .catch((e) => setError((e as Error).message))
  }, [activeGame])

  const current = games.find((g) => g.gameId === activeGame) ?? null

  return (
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
        <span className="hint">策划工作台 · AI 出题 + 确定性求解器验题</span>
      </header>

      {error && <div className="error">{error}</div>}

      {current && detail ? (
        <Workbench game={current} detail={detail} />
      ) : (
        !error && <div className="empty big-empty">加载玩法详情…</div>
      )}
    </div>
  )
}
