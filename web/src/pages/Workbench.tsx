// 生成工作台（表现层_生成工作台 §1/§2/§3）：表单→流水线→结果→采纳→执行确认的完整闭环
// 状态机（§3）：空态→生成中（可取消）→成功态（回放⇄试玩）/失败态（自动应用建议重试一次，可还原）
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  generate,
  getSettings,
  getTapeJson,
  solvePasted,
  type GameDetail,
  type GameSummary,
  type GenerationRun,
  type SolveResponse,
} from '../api/client'
import type { SolutionTapeMirror, VisualMapping } from '../renderer/model'
import { AdoptDialog, type AdoptTarget } from '../components/AdoptDialog'
import { CandidateStrip } from '../components/CandidateStrip'
import { ConfirmationCard } from '../components/ConfirmationCard'
import { MetricsBar } from '../components/MetricsBar'
import { PhrasePicker } from '../components/PhrasePicker'
import { PipelineLog } from '../components/PipelineLog'
import { PreviewPane } from '../components/PreviewPane'
import { logEntriesFromRun, mechanicsHint, nextPhase, suggestionsFrom, type Phase } from '../state/workbench'

// 表单值 + "由系统调整"标记（失败自动重试时被调字段高亮，可一键还原 §5）
interface FormState {
  targetDifficulty: number
  levelNumber: string
  specialRequirements: string
  candidateLimit: number
  solveTimeoutMs: number
  adjustedFields: string[]
}

export function Workbench({ game, detail }: { game: GameSummary; detail: GameDetail }) {
  const [mode, setMode] = useState<'generate' | 'qc'>('generate')
  const [phase, setPhase] = useState<Phase>('idle')
  const [form, setForm] = useState<FormState>({
    targetDifficulty: 2,
    levelNumber: '',
    specialRequirements: '',
    candidateLimit: 3,
    solveTimeoutMs: 30000,
    adjustedFields: [],
  })
  const [originalForm, setOriginalForm] = useState<FormState | null>(null) // 自动调整前的快照（一键还原用）
  const [autoRetried, setAutoRetried] = useState(false)
  const [autoRetryReasons, setAutoRetryReasons] = useState<string[]>([])

  const [run, setRun] = useState<GenerationRun | null>(null)
  const [selectedAttempt, setSelectedAttempt] = useState(1)
  const [tape, setTape] = useState<SolutionTapeMirror | null>(null)
  const [qcResult, setQcResult] = useState<SolveResponse | null>(null)
  const [qcTape, setQcTape] = useState<SolutionTapeMirror | null>(null)
  const [qcJson, setQcJson] = useState('')
  const [message, setMessage] = useState('')
  const [adoptTarget, setAdoptTarget] = useState<AdoptTarget | null>(null)
  const [adoptedLevelId, setAdoptedLevelId] = useState<string | null>(null)
  const [confirmRefresh, setConfirmRefresh] = useState(0)
  const abortRef = useRef<AbortController | null>(null)

  const mapping = detail.visualMapping as VisualMapping

  // 默认值取设置页（gen.*）
  useEffect(() => {
    void getSettings().then((settings) => {
      setForm((f) => ({
        ...f,
        candidateLimit: typeof settings['gen.candidateLimit'] === 'number' ? (settings['gen.candidateLimit'] as number) : f.candidateLimit,
        solveTimeoutMs: typeof settings['gen.solveTimeoutMs'] === 'number' ? (settings['gen.solveTimeoutMs'] as number) : f.solveTimeoutMs,
      }))
    }).catch(() => undefined)
  }, [])

  // 选中候选的磁带
  const selectedCandidate = run?.candidates.find((c) => c.attemptIndex === selectedAttempt) ?? null
  useEffect(() => {
    if (selectedCandidate?.tapeId) {
      void getTapeJson(selectedCandidate.tapeId).then(setTape).catch((e) => setMessage((e as Error).message))
    } else {
      setTape(null)
    }
  }, [selectedCandidate])

  const mechHint = useMemo(() => mechanicsHint(detail.mechanics, form.levelNumber), [detail.mechanics, form.levelNumber])
  const logEntries = useMemo(() => (run ? logEntriesFromRun(run) : []), [run])
  const adoptedCandidate = run?.candidates.find((c) => c.attemptIndex === run.adoptedAttempt) ?? null

  // ---- 生成主流程（含失败自动应用建议重试一次，§5 设计决定 #4）----
  const runGenerate = useCallback(
    async (params: FormState) => {
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller
      setPhase(nextPhase('idle', 'start'))
      setMessage('')
      try {
        const result = await generate(game.gameId, {
          targetDifficulty: params.targetDifficulty,
          levelNumber: params.levelNumber.trim() || undefined,
          specialRequirements: params.specialRequirements.trim() || undefined,
          candidateLimit: params.candidateLimit,
          solveTimeoutMs: params.solveTimeoutMs,
        }, controller.signal)
        setRun(result)
        setSelectedAttempt(result.adoptedAttempt ?? result.candidates[0]?.attemptIndex ?? 1)
        if (result.status === 'succeeded') {
          setPhase(nextPhase('generating', 'succeed'))
          return
        }
        if (result.status === 'failed') {
          // 失败即自动应用建议直接重试（仅一次；被调字段高亮，可还原）
          const suggestions = suggestionsFrom(result)
          if (suggestions && !autoRetried) {
            setOriginalForm(params)
            const adjusted: FormState = {
              ...params,
              targetDifficulty: suggestions.targetDifficulty ?? params.targetDifficulty,
              solveTimeoutMs: suggestions.solveTimeoutMs ?? params.solveTimeoutMs,
              candidateLimit: suggestions.candidateLimit ?? params.candidateLimit,
              specialRequirements: suggestions.specialRequirements !== undefined ? suggestions.specialRequirements : params.specialRequirements,
            }
            const adjustedFields = Object.keys(suggestions).filter((k) => k !== 'reasons' && suggestions[k as keyof typeof suggestions] !== undefined)
            setForm({ ...adjusted, adjustedFields })
            setAutoRetryReasons(suggestions.reasons)
            setAutoRetried(true)
            void runGenerateRef.current?.(adjusted)
          } else {
            setPhase(nextPhase('generating', 'exhaust'))
          }
          return
        }
        setPhase('idle') // cancelled
      } catch (e) {
        if ((e as Error).name === 'AbortError') {
          setPhase(nextPhase('generating', 'cancel'))
          return
        }
        setMessage((e as Error).message)
        setPhase(nextPhase('generating', 'apiError'))
      }
    },
    [game.gameId, autoRetried],
  )
  const runGenerateRef = useRef<typeof runGenerate | null>(null)
  runGenerateRef.current = runGenerate

  const cancelGenerate = () => {
    abortRef.current?.abort()
  }

  const resetAll = () => {
    setPhase('idle')
    setRun(null)
    setTape(null)
    setAutoRetried(false)
    setAutoRetryReasons([])
    if (originalForm) {
      setForm({ ...originalForm, adjustedFields: [] })
      setOriginalForm(null)
    }
  }

  // ---- 质检模式：粘贴原生 JSON 直解直判 ----
  const runQc = async () => {
    setMessage('')
    setQcResult(null)
    setQcTape(null)
    try {
      const result = await solvePasted(game.gameId, qcJson)
      setQcResult(result)
      if (result.tapeId !== null) {
        setQcTape(await getTapeJson(result.tapeId))
      }
    } catch (e) {
      setMessage((e as Error).message)
    }
  }

  // ---- 采纳（生成候选 / 质检内容共用）----
  const openAdopt = (levelJson: string, source: 'generated' | 'imported', difficulty: string, solution: string) => {
    const id = (() => {
      try {
        return (JSON.parse(levelJson) as { id?: string }).id ?? null
      } catch {
        return null
      }
    })()
    setAdoptTarget({ levelJson, source, levelId: id, difficulty, solution })
  }

  const download = (levelJson: string, levelId: string | null) => {
    const blob = new Blob([levelJson], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${levelId ?? 'level'}.json`
    link.click()
    URL.revokeObjectURL(url)
  }

  const generating = phase === 'generating'

  return (
    <div className="workbench">
      {game.status === 'trial' && (
        <div className="trial-watermark">该玩法适配层未经真机对拍，结论仅供参考</div>
      )}

      <div className="wb-layout">
        {/* 左列：表单 + 流水线日志（§2 画面分区） */}
        <div className="wb-left">
          <div className="mode-tabs">
            <button className={mode === 'generate' ? 'active' : ''} onClick={() => setMode('generate')}>生成关卡</button>
            <button className={mode === 'qc' ? 'active' : ''} onClick={() => setMode('qc')}>验证关卡（质检）</button>
          </div>

          {mode === 'generate' ? (
            <div className="wb-form">
              {/* 难度三段（§4：大按钮组，选中即主色填充） */}
              <div className="difficulty-group">
                {[1, 2, 3].map((band) => (
                  <button
                    key={band}
                    className={`difficulty-btn ${form.targetDifficulty === band ? 'active' : ''} ${form.adjustedFields.includes('targetDifficulty') ? 'auto-adjusted' : ''}`}
                    onClick={() => setForm({ ...form, targetDifficulty: band })}
                  >
                    {band} · {band === 1 ? '轻松' : band === 2 ? '标准' : '烧脑'}
                  </button>
                ))}
              </div>

              <label className="field">
                关卡编号
                <input
                  value={form.levelNumber}
                  onChange={(e) => setForm({ ...form, levelNumber: e.target.value })}
                  placeholder="如 501（决定可用机制范围）"
                />
              </label>
              {mechHint && <div className="mech-hint">{mechHint}</div>}

              <label className="field">
                特殊要求
                <PhrasePicker gameId={game.gameId} onInsert={(text) => setForm({ ...form, specialRequirements: form.specialRequirements ? `${form.specialRequirements}；${text}` : text })} />
                <textarea
                  value={form.specialRequirements}
                  onChange={(e) => setForm({ ...form, specialRequirements: e.target.value })}
                  placeholder="例：必须包含 3 只朝下的猪；炸弹最多 1 次"
                  className={form.adjustedFields.includes('specialRequirements') ? 'auto-adjusted' : ''}
                />
              </label>

              <details className="advanced">
                <summary>高级选项</summary>
                <label className="field">
                  候选上限
                  <input
                    type="number"
                    min={1}
                    max={10}
                    value={form.candidateLimit}
                    className={form.adjustedFields.includes('candidateLimit') ? 'auto-adjusted' : ''}
                    onChange={(e) => setForm({ ...form, candidateLimit: Number(e.target.value) || 3 })}
                  />
                </label>
                <label className="field">
                  单候选求解超时（ms）
                  <input
                    type="number"
                    min={100}
                    step={100}
                    value={form.solveTimeoutMs}
                    className={form.adjustedFields.includes('solveTimeoutMs') ? 'auto-adjusted' : ''}
                    onChange={(e) => setForm({ ...form, solveTimeoutMs: Number(e.target.value) || 30000 })}
                  />
                </label>
              </details>

              <div className="form-actions">
                {!generating ? (
                  <button className="primary" onClick={() => { setAutoRetried(false); setAutoRetryReasons([]); void runGenerate(form) }}>
                    {phase === 'failed' ? '重新生成' : phase === 'success' ? '再次生成' : '生成'}
                  </button>
                ) : (
                  <button onClick={cancelGenerate}>取消</button>
                )}
                {(phase === 'success' || phase === 'failed') && <button onClick={resetAll}>回到空态</button>}
                {originalForm && (
                  <button
                    className="link"
                    onClick={() => {
                      setForm({ ...originalForm, adjustedFields: [] })
                      setOriginalForm(null)
                    }}
                  >
                    一键还原系统调整
                  </button>
                )}
              </div>

              {autoRetryReasons.length > 0 && (
                <div className="auto-retry-box">
                  <div>系统已自动应用建议并重试（高亮字段可还原）：</div>
                  <ul>
                    {autoRetryReasons.map((reason) => (
                      <li key={reason}>{reason}</li>
                    ))}
                  </ul>
                </div>
              )}

              <PipelineLog entries={logEntries} />
            </div>
          ) : (
            <div className="wb-form">
              <label className="field">
                粘贴项目原生格式关卡 JSON（零转换直解）
                <textarea
                  className="qc-input"
                  value={qcJson}
                  onChange={(e) => setQcJson(e.target.value)}
                  placeholder='{"id":"...","grid":[...]}'
                />
              </label>
              <div className="form-actions">
                <button className="primary" disabled={!qcJson.trim()} onClick={() => void runQc()}>
                  验证可解性
                </button>
              </div>
              {qcResult && (
                <div className="qc-summary">
                  <span className={`status status-${qcResult.status}`}>{qcResult.status}</span>
                  <span>解长 {qcResult.solutionLength}</span>
                  <span>最优性 {qcResult.optimality}</span>
                  <span>
                    难度 {qcResult.difficulty.band === 0 ? '不适用' : `${qcResult.difficulty.band} 档（${qcResult.difficulty.score.toFixed(1)}）`}
                  </span>
                  {qcResult.errorMessage && <span className="error-text">{qcResult.errorMessage}</span>}
                </div>
              )}
              {message && <div className="error">{message}</div>}
            </div>
          )}
        </div>

        {/* 右列：结果区（空态/成功态/失败态三选一 §2） */}
        <div className="wb-right">
          {phase === 'idle' && !qcResult && (
            <div className="empty big-empty">
              {mode === 'generate'
                ? '选择难度与要求，点"生成"——LLM 出题、求解器验题，候选迭代过程在左侧日志可见'
                : '粘贴关卡 JSON，先验可解性再决定是否采纳'}
            </div>
          )}

          {phase === 'success' && run && (
            <>
              <MetricsBar run={run} adopted={adoptedCandidate} solutionLength={tape?.steps.length ?? null} />
              <CandidateStrip
                candidates={run.candidates}
                adoptedAttempt={run.adoptedAttempt}
                selected={selectedAttempt}
                onSelect={setSelectedAttempt}
              />
              {selectedCandidate?.contentJson ? (
                <>
                  <PreviewPane
                    gameId={game.gameId}
                    levelJson={selectedCandidate.contentJson}
                    tape={tape}
                    mapping={mapping}
                  />
                  <div className="result-actions">
                    {selectedCandidate.attemptIndex === run.adoptedAttempt && (
                      <button
                        className="primary"
                        onClick={() =>
                          openAdopt(
                            selectedCandidate.contentJson!,
                            'generated',
                            `第 ${selectedCandidate.difficultyBand} 档（${selectedCandidate.difficultyScore?.toFixed(1)}）`,
                            `${tape?.steps.length ?? '—'} 步（unitCostShortest）`,
                          )
                        }
                      >
                        采纳落盘
                      </button>
                    )}
                    <button onClick={() => download(selectedCandidate.contentJson!, selectedCandidate ? (JSON.parse(selectedCandidate.contentJson!) as { id?: string }).id ?? null : null)}>
                      下载 JSON
                    </button>
                  </div>
                </>
              ) : (
                <div className="empty">该候选无内容（API 失败）</div>
              )}
              <ConfirmationCard gameId={game.gameId} levelId={adoptedLevelId} refreshKey={confirmRefresh} />
            </>
          )}

          {phase === 'failed' && run && (
            <div className="failure-report">
              <h3>生成失败报告</h3>
              {/* 三段式（§5）：候选清单 → 结论 → 建议（已自动应用并重试过则如实说明） */}
              <CandidateStrip candidates={run.candidates} adoptedAttempt={null} selected={selectedAttempt} onSelect={setSelectedAttempt} />
              <div className="failure-conclusion">{run.failureSummary}</div>
              {selectedCandidate && <div className="failure-detail">{selectedCandidate.failureDetail}</div>}
              {autoRetryReasons.length > 0 ? (
                <div className="failure-advice">系统已自动应用建议重试一次仍失败：请人工调整（降难度/放宽要求/提高候选）后再生成。</div>
              ) : (
                <div className="failure-advice">建议：降一档难度、放宽特殊要求、提高候选上限或增加超时后重试。</div>
              )}
              <ConfirmationCard gameId={game.gameId} levelId={adoptedLevelId} refreshKey={confirmRefresh} />
            </div>
          )}

          {mode === 'qc' && qcResult && (
            <>
              {qcTape ? (
                <PreviewPane gameId={game.gameId} levelJson={qcJson} tape={qcTape} mapping={mapping} />
              ) : (
                <div className="empty">该结论无可回放磁带（无解/未知/取消不评分不回放）</div>
              )}
              <div className="result-actions">
                <button
                  className="primary"
                  onClick={() =>
                    openAdopt(
                      qcJson,
                      'imported',
                      qcResult.difficulty.band === 0 ? '不适用' : `第 ${qcResult.difficulty.band} 档（${qcResult.difficulty.score.toFixed(1)}）`,
                      `${qcResult.solutionLength} 步（${qcResult.optimality}）`,
                    )
                  }
                >
                  采纳落盘
                </button>
                <button onClick={() => download(qcJson, qcResult ? (JSON.parse(qcJson) as { id?: string }).id ?? null : null)}>下载 JSON</button>
              </div>
              <ConfirmationCard gameId={game.gameId} levelId={adoptedLevelId} refreshKey={confirmRefresh} />
            </>
          )}
        </div>
      </div>

      {adoptTarget && (
        <AdoptDialog
          gameId={game.gameId}
          levelsRoot={detail.levelsRoot}
          target={adoptTarget}
          onClose={() => setAdoptTarget(null)}
          onAdopted={(result) => {
            setAdoptTarget(null)
            setAdoptedLevelId(result.levelId)
            setConfirmRefresh((k) => k + 1)
            setMessage(`已落盘：${result.filePath}（待游戏执行确认${result.sameContent ? '；同内容未重写' : ''}）`)
          }}
        />
      )}
    </div>
  )
}
