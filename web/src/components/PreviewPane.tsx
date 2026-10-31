// 预览区：同一棋盘上"解法回放 ⇄ 试玩"互斥切换（§4 设计决定 #5：切换即重置局面）
// 回放=播放/单步/重置+步数（磁带帧驱动）；试玩=点合法动作步进（后端步进 API，与求解同一份适配层）
// 边界提示（§4 预览防线）：模型内通关≠游戏执行通过，确认状态另列
import { useEffect, useMemo, useState } from 'react'
import type { PlayState } from '../api/client'
import { playStart, playStep } from '../api/client'
import { BoardSvg } from '../renderer/BoardSvg'
import { frameAt, framesOf } from '../renderer/tween'
import type { SceneSnapshot, SolutionTapeMirror, VisualMapping } from '../renderer/model'

export function PreviewPane({
  gameId,
  levelJson,
  tape,
  mapping,
}: {
  gameId: string
  levelJson: string | null // 试玩需要关卡原文（候选内容/质检粘贴）
  tape: SolutionTapeMirror | null
  mapping: VisualMapping
}) {
  const [mode, setMode] = useState<'replay' | 'play'>('replay')
  const [error, setError] = useState('')

  // 切换即重置（互斥语义）
  const switchMode = (next: 'replay' | 'play') => {
    setMode(next)
    setError('')
    setProgress(0)
    setPlaying(false)
    setPlayState(null)
    setPlayMoves([])
  }

  // ---- 回放子状态 ----
  const [progress, setProgress] = useState(0)
  const [playing, setPlaying] = useState(false)
  const frames = useMemo(() => (tape ? framesOf(tape) : []), [tape])
  const frame = frames.length > 0 ? frameAt(frames, progress) : null

  useEffect(() => {
    if (!playing || frames.length === 0) return
    const total = frames.length - 1
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
  }, [playing, frames])

  // ---- 试玩子状态 ----
  const [playState, setPlayState] = useState<PlayState | null>(null)
  const [playMoves, setPlayMoves] = useState<unknown[]>([])

  const startPlay = async () => {
    if (!levelJson) return
    setError('')
    try {
      setPlayState(await playStart(gameId, levelJson))
      setPlayMoves([])
    } catch (e) {
      setError((e as Error).message)
    }
  }

  // 首次进入试玩模式自动开局
  useEffect(() => {
    if (mode === 'play' && playState === null && levelJson) void startPlay()
  }, [mode]) // eslint-disable-line react-hooks/exhaustive-deps

  const applyMove = async (move: unknown) => {
    if (!levelJson) return
    const nextMoves = [...playMoves, move]
    try {
      const state = await playStep(gameId, levelJson, nextMoves)
      setPlayMoves(nextMoves)
      setPlayState(state) // 非法动作后端 400 → 不推进（前端只展示合法动作，此处为兜底）
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const currentMoveText =
    tape && mode === 'replay'
      ? (() => {
          const index = Math.round(progress)
          return index === 0 ? '初始局面' : tape.steps[Math.min(index, tape.steps.length) - 1]?.moveText ?? ''
        })()
      : ''

  return (
    <div className="preview-pane">
      <div className="preview-toolbar">
        <div className="mode-switch">
          <button className={mode === 'replay' ? 'active' : ''} onClick={() => switchMode('replay')} disabled={!tape}>
            解法回放
          </button>
          <button className={mode === 'play' ? 'active' : ''} onClick={() => switchMode('play')} disabled={!levelJson}>
            试玩
          </button>
        </div>
        {mode === 'replay' && tape && (
          <span className="outcome-hint">
            结局 {tape.outcome} · 模型内有解（游戏执行确认状态另列）
          </span>
        )}
        {mode === 'play' && <span className="outcome-hint">在当前适配模型中试玩；游戏执行确认状态另列</span>}
      </div>

      {error && <div className="error">{error}</div>}

      {mode === 'replay' &&
        (frame ? (
          <>
            <BoardSvg prev={frame.prev as SceneSnapshot | null} next={frame.next as SceneSnapshot} t={frame.t} mapping={mapping} />
            <div className="controls">
              <button onClick={() => setProgress(0)}>重置</button>
              <button onClick={() => { setPlaying(false); setProgress((p) => Math.max(0, Math.round(p) - 1)) }}>单步↩</button>
              <button onClick={() => setPlaying((v) => !v)} disabled={frames.length <= 1}>
                {playing ? '暂停' : '播放'}
              </button>
              <button onClick={() => { setPlaying(false); setProgress((p) => Math.min(frames.length - 1, Math.round(p) + 1)) }}>单步↪</button>
              <input
                type="range"
                min={0}
                max={frames.length - 1}
                step={0.25}
                value={progress}
                onChange={(e) => { setPlaying(false); setProgress(Number(e.target.value)) }}
              />
              <span>
                第 {Math.round(progress)}/{frames.length - 1} 步 · {currentMoveText}
              </span>
            </div>
          </>
        ) : (
          <div className="empty">该候选无可回放磁带</div>
        ))}

      {mode === 'play' && (
        <>
          {playState && (
            <>
              <BoardSvg prev={null} next={playState.snapshot} t={1} mapping={mapping} />
              {playState.isGoal ? (
                <div className="goal-banner">🎉 在当前适配模型中通关（游戏执行确认状态另列）</div>
              ) : (
                <div className="play-moves">
                  {playState.legalMoves.map((move, i) => (
                    <button key={i} onClick={() => void applyMove(move.move)}>
                      {move.moveText}
                    </button>
                  ))}
                  {playState.legalMoves.length === 0 && <span className="empty">无合法动作</span>}
                </div>
              )}
              <div className="controls">
                <span>已走 {playState.movesExecuted} 步</span>
                <button onClick={() => void startPlay()}>回到初始</button>
              </div>
            </>
          )}
          {!playState && <div className="empty">试玩加载中…</div>}
        </>
      )}
    </div>
  )
}
