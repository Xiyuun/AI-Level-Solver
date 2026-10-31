// 第 12 步骨架页：顶栏玩法切换 + 关卡预览纵向切片（求解→磁带→通用渲染器→步进）
// 生成工作台完整形态（表单/流水线日志/试玩/采纳）在第 13 步落

import { useCallback, useEffect, useMemo, useState } from 'react'
import { activateGame, getGame, getTape, listGames, listLevels, solveLevel, type GameSummary, type LevelRow, type SolveResponse } from './api/client'
import { BoardSvg } from './renderer/BoardSvg'
import { frameAt, framesOf } from './renderer/tween'
import type { SceneSnapshot, SolutionTapeMirror, VisualMapping } from './renderer/model'

export function App() {
  const [games, setGames] = useState<GameSummary[]>([])
  const [activeGame, setActiveGame] = useState<string>('')
  const [levels, setLevels] = useState<LevelRow[]>([])
  const [mapping, setMapping] = useState<VisualMapping | null>(null)
  const [solve, setSolve] = useState<SolveResponse | null>(null)
  const [tape, setTape] = useState<SolutionTapeMirror | null>(null)
  const [progress, setProgress] = useState(0) // 帧坐标（0..steps），播放/单步/重置共用（§4）
  const [playing, setPlaying] = useState(false)
  const [error, setError] = useState<string>('')

  // 玩法列表
  const refreshGames = useCallback(async () => {
    try {
      const list = await listGames()
      setGames(list)
      if (list.length > 0) {
        setActiveGame((current) => current || list[0].gameId)
      }
    } catch (e) {
      setError(`玩法列表加载失败：${(e as Error).message}（确认平台已启动：dotnet run --urls http://localhost:5000）`)
    }
  }, [])

  useEffect(() => {
    void refreshGames()
  }, [refreshGames])

  // 活跃玩法变化 → 关卡列表 + 视觉映射
  useEffect(() => {
    if (!activeGame) return
    setError('')
    setSolve(null)
    setTape(null)
    void (async () => {
      try {
        const [rows, detail] = await Promise.all([listLevels(activeGame), getGame(activeGame)])
        setLevels(rows)
        setMapping(detail.visualMapping as VisualMapping)
      } catch (e) {
        setError((e as Error).message)
      }
    })()
  }, [activeGame])

  // 点关卡：求解 → 取磁带 → 回到第 0 帧
  const openLevel = async (levelId: string) => {
    setError('')
    setPlaying(false)
    try {
      const result = await solveLevel(activeGame, levelId)
      setSolve(result)
      if (result.tapeId !== null) {
        setTape((await getTape(result.tapeId)) as SolutionTapeMirror)
        setProgress(0)
      } else {
        setTape(null)
      }
    } catch (e) {
      setError((e as Error).message)
    }
  }

  // 播放：小步推进帧坐标（补间由渲染层完成）
  useEffect(() => {
    if (!playing || !tape) return
    const total = tape.steps.length
    const timer = window.setInterval(() => {
      setProgress((p) => {
        if (p >= total) {
          setPlaying(false)
          return total
        }
        return p + 0.25
      })
    }, 120)
    return () => window.clearInterval(timer)
  }, [playing, tape])

  const frame = useMemo(() => {
    if (!tape) return null
    return frameAt(framesOf(tape), progress)
  }, [tape, progress])

  const currentMoveText = useMemo(() => {
    if (!tape) return ''
    const index = Math.round(progress)
    return index === 0 ? '初始局面' : tape.steps[Math.min(index, tape.steps.length) - 1]?.moveText ?? ''
  }, [tape, progress])

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
        <span className="hint">关卡求解与生成中台 · 骨架预览</span>
      </header>

      {error && <div className="error">{error}</div>}

      <main className="layout">
        <aside className="levels">
          <h3>关卡（{levels.length}）</h3>
          <ul>
            {levels.map((l) => (
              <li key={l.levelId}>
                <button onClick={() => void openLevel(l.levelId)}>{l.levelId}</button>
              </li>
            ))}
          </ul>
        </aside>

        <section className="preview">
          {!solve && <div className="empty">从左侧选择关卡：求解 → 磁带 → 通用渲染器预览</div>}
          {solve && (
            <>
              <div className="metrics">
                <span className={`status status-${solve.status}`}>{solve.status}</span>
                <span>解长 {solve.solutionLength}</span>
                <span>最优性 {solve.optimality}</span>
                <span>
                  难度 {solve.difficulty.band === 0 ? '不适用' : `${solve.difficulty.band} 档（${solve.difficulty.bandName}，${solve.difficulty.score.toFixed(1)}）`}
                </span>
                {solve.errorMessage && <span className="error-text">{solve.errorMessage}</span>}
              </div>
              {frame && mapping ? (
                <>
                  <BoardSvg prev={frame.prev as SceneSnapshot | null} next={frame.next as SceneSnapshot} t={frame.t} mapping={mapping} />
                  {tape && (
                    <div className="controls">
                      <button onClick={() => setProgress(0)}>重置</button>
                      <button onClick={() => { setPlaying(false); setProgress((p) => Math.max(0, Math.round(p) - 1)) }}>单步↩</button>
                      <button onClick={() => setPlaying((v) => !v)} disabled={!tape.steps.length}>
                        {playing ? '暂停' : '播放'}
                      </button>
                      <button onClick={() => { setPlaying(false); setProgress((p) => Math.min(tape.steps.length, Math.round(p) + 1)) }}>单步↪</button>
                      <input
                        type="range"
                        min={0}
                        max={tape.steps.length}
                        step={0.25}
                        value={progress}
                        onChange={(e) => { setPlaying(false); setProgress(Number(e.target.value)) }}
                      />
                      <span>
                        第 {Math.round(progress)}/{tape.steps.length} 步 · {currentMoveText} · 结局 {tape.outcome}
                      </span>
                    </div>
                  )}
                </>
              ) : (
                <div className="empty">{solve.status === 'solved' ? '加载磁带…' : '该结论无可回放磁带（无解/未知/取消）'}</div>
              )}
            </>
          )}
        </section>
      </main>
    </div>
  )
}
