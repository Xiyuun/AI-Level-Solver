// 平台 API client：薄封装 + 与后端一致的 camelCase 类型（后端 TapeJson 口径）
// 开发期经 Vite 代理 /api → 平台；打包后同源直连

import type { SceneSnapshot, SolutionTapeMirror } from '../renderer/model'

export interface GameSummary {
  gameId: string
  displayName: string
  status: string
  statusName: string
  adapterVer: string
  kernelVer: string
  gate2Applicable: boolean
  levelCount: number
}

export interface GameDetail extends GameSummary {
  adapterHash: string
  contractVer: string
  levelsRoot: string
  visualMapping: unknown // 解析后的映射对象，直接喂渲染器
  mechanics: unknown
  difficultyWeights: unknown
  gameBrief: string
  formatSpec: string
  ruleMatrix: string
}

export interface LevelRow {
  levelId: string
  levelHash: string
  source: string
  createdAt: string
}

export interface SolveResponse {
  runId: number
  gameId: string
  levelRef: string
  levelHash: string
  status: string
  optimality: string
  graphComplete: boolean
  terminationReason: string
  metricsScope: string
  solutionLength: number
  metrics: { statesExplored: number; uniqueStates: number; frontierPeak: number; elapsedMs: number; solutionLength: number; avgBranchingFactor: number } | null
  difficulty: { band: number; bandName: string; score: number; modelVersion: string; usedFeatures: string[]; notes: string[] }
  tapeId: number | null
  errorMessage: string | null
}

// ---- 生成 ----

export interface CandidateAttempt {
  attemptIndex: number
  contentJson: string | null
  failureClass: string
  failureDetail: string | null
  difficultyBand: number | null
  difficultyScore: number | null
  solveRunId: number | null
  tapeId: number | null
  llmCalls: number
  elapsedMs: number
}

export interface GenerationRun {
  runId: number
  gameId: string
  status: string // succeeded|failed|cancelled
  targetDifficulty: number
  levelNumber: string | null
  specialRequirements: string | null
  candidateLimit: number
  solveTimeoutMs: number
  promptVersion: string
  candidates: CandidateAttempt[]
  adoptedAttempt: number | null
  failureSummary: string | null
  totalElapsedMs: number
}

// ---- 采纳与执行确认 ----

export interface AdoptResult {
  levelId: string
  levelHash: string
  filePath: string
  sameContent: boolean
  replaced: boolean
  confirmationId: number
  confirmationState: string
  solveRunId: number | null
}

// 编号冲突（内容不同且未请求覆盖）：带旧文件指纹
export class AdoptConflictError extends Error {
  constructor(message: string, readonly conflictHash: string | null) {
    super(message)
  }
}

export interface Confirmation {
  id: number
  gameId: string
  levelId: string
  levelHash: string
  state: string // pending|running|passed|failed|invalidated
  mode: string
  outcome: string | null
  confirmedBy: string | null
  confirmedAt: string | null
  note: string | null
  solveRunId: number | null
  fileStatus: string // ok|missing|hashChanged
  currentFileHash: string | null
  createdAt: string
  updatedAt: string
}

export interface BaselineRowDto {
  id: number
  gameId: string
  levelId: string
  levelHash: string
  status: string
  solutionLength: number
  approvedBy: string | null
  approvedAt: string | null
}

// ---- 试玩 ----

export interface PlayMove {
  move: unknown
  moveText: string
}

export interface PlayState {
  snapshot: SceneSnapshot
  legalMoves: PlayMove[]
  isGoal: boolean
  movesExecuted: number
}

// ---- 短语 ----

export interface Phrase {
  id: number
  text: string
  sortOrder: number
}

async function getJson<T>(url: string): Promise<T> {
  const resp = await fetch(url)
  if (!resp.ok) throw new Error(`${url} → HTTP ${resp.status}`)
  return (await resp.json()) as T
}

async function sendJson<T>(url: string, method: string, body?: unknown): Promise<T> {
  const resp = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await resp.text()
  if (!resp.ok) {
    // 采纳冲突：结构化错误单独抛，前端弹替换确认
    if (resp.status === 409) {
      const payload = safeParse(text)
      throw new AdoptConflictError(payload?.message ?? text, payload?.conflictHash ?? null)
    }
    throw new Error(`${url} → HTTP ${resp.status}: ${text.slice(0, 200)}`)
  }
  return (text ? JSON.parse(text) : null) as T
}

function safeParse(text: string): { message?: string; conflictHash?: string } | null {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

// 玩法
export const listGames = () => getJson<GameSummary[]>('/api/games')
export const getGame = (gameId: string) => getJson<GameDetail>(`/api/games/${gameId}`)
export const activateGame = (gameId: string) =>
  sendJson<void>(`/api/games/${gameId}/activate`, 'PUT')

// 求解与磁带
export const listLevels = (gameId: string) => getJson<LevelRow[]>(`/api/games/${gameId}/levels`)
export const solveLevel = (gameId: string, levelId: string) =>
  sendJson<SolveResponse>('/api/solve', 'POST', { gameId, levelId })
export const solvePasted = (gameId: string, levelJson: string) =>
  sendJson<SolveResponse>('/api/solve', 'POST', { gameId, levelJson })
export const getTapeJson = async (tapeId: number): Promise<SolutionTapeMirror> => {
  const resp = await fetch(`/api/tapes/${tapeId}`)
  if (!resp.ok) throw new Error(`磁带获取失败：HTTP ${resp.status}`)
  return (await resp.json()) as SolutionTapeMirror
}

// 生成（可中断：signal 透传给 fetch）
export function generate(gameId: string, req: {
  targetDifficulty: number
  levelNumber?: string
  specialRequirements?: string
  candidateLimit?: number
  solveTimeoutMs?: number
}, signal?: AbortSignal): Promise<GenerationRun> {
  return fetch('/api/generate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ gameId, ...req }),
    signal,
  }).then(async (resp) => {
    const text = await resp.text()
    if (!resp.ok) throw new Error(`生成失败 HTTP ${resp.status}: ${text.slice(0, 200)}`)
    return JSON.parse(text) as GenerationRun
  })
}

// ---- 异步生成任务（实时进度）----
// 动机：整轮生成可达数分钟，同步接口会让页面长时间无反馈；改为任务号 + SSE 事件流（全流程优化 §8.4）

export interface GenerationEvent {
  seq: number
  elapsedMs: number
  stage: string // llm | solver | difficulty | constraint | integration
  message: string
  attemptIndex: number | null
  failureClass: string | null
}

export interface GenerationJob {
  jobId: number
  status: string // running | succeeded | failed | cancelled
  elapsedMs: number
  events: GenerationEvent[]
  run: GenerationRun | null
  error: string | null
}

export interface GenerateParams {
  targetDifficulty: number
  levelNumber?: string
  specialRequirements?: string
  candidateLimit?: number
  solveTimeoutMs?: number
}

// 发起后台生成：立即返回任务号（202）
export async function startGenerationJob(gameId: string, req: GenerateParams): Promise<number> {
  const resp = await fetch('/api/generate/jobs', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ gameId, ...req }),
  })
  const text = await resp.text()
  if (!resp.ok) throw new Error(`发起生成失败 HTTP ${resp.status}: ${text.slice(0, 200)}`)
  return (JSON.parse(text) as { jobId: number }).jobId
}

// 任务快照（断线重连/兜底轮询）
export const getGenerationJob = (jobId: number) => getJson<GenerationJob>(`/api/generate/jobs/${jobId}`)

// 取消任务
export function cancelGenerationJob(jobId: number): Promise<void> {
  return sendJson<{ message: string }>(`/api/generate/jobs/${jobId}/cancel`, 'POST').then(() => undefined)
}

// 订阅 SSE：返回取消订阅函数。progress 逐条回调，done 回调携带终态与运行记录
export function subscribeGenerationJob(
  jobId: number,
  handlers: {
    onProgress: (event: GenerationEvent) => void
    onDone: (job: GenerationJob) => void
    onError: (message: string) => void
  },
): () => void {
  const source = new EventSource(`/api/generate/jobs/${jobId}/events`)
  source.addEventListener('progress', (raw) => {
    try {
      handlers.onProgress(JSON.parse((raw as MessageEvent).data as string) as GenerationEvent)
    } catch {
      // 坏帧忽略：事件流不该因单条解析失败中断
    }
  })
  source.addEventListener('done', (raw) => {
    source.close()
    try {
      handlers.onDone(JSON.parse((raw as MessageEvent).data as string) as GenerationJob)
    } catch (e) {
      handlers.onError(`终态解析失败：${(e as Error).message}`)
    }
  })
  source.onerror = () => {
    // 浏览器会自动重连；只有连接彻底不可用才提示（终态由 done 负责收尾）
    if (source.readyState === EventSource.CLOSED) {
      handlers.onError('进度连接已断开')
    }
  }
  return () => source.close()
}

// 试玩
export const playStart = (gameId: string, levelJson: string) =>
  sendJson<PlayState>('/api/play/start', 'POST', { gameId, levelJson })
export const playStep = (gameId: string, levelJson: string, moves: unknown[]) =>
  sendJson<PlayState>('/api/play/step', 'POST', { gameId, levelJson, moves })

// 采纳与执行确认
export function adopt(gameId: string, levelJson: string, source: 'generated' | 'imported',
  overwrite = false, expectedOldHash?: string): Promise<AdoptResult> {
  return sendJson<AdoptResult>('/api/adopt', 'POST', {
    gameId, levelJson, source, overwrite, expectedOldHash,
  })
}
export const listConfirmations = (gameId: string) =>
  getJson<Confirmation[]>(`/api/games/${gameId}/confirmations`)
export const recordManualResult = (id: number, outcome: string, confirmedBy: string, note?: string) =>
  sendJson<Confirmation>(`/api/confirmations/${id}/manual-result`, 'POST', { outcome, confirmedBy, note })
export const importReport = (id: number, levelHash: string, outcome: string, gameBuildFingerprint?: string) =>
  sendJson<Confirmation>(`/api/confirmations/${id}/report`, 'POST', { levelHash, outcome, gameBuildFingerprint })
export const approveBaseline = (id: number, approvedBy: string) =>
  sendJson<{ baselineId: number; alreadyExists: boolean }>(`/api/confirmations/${id}/approve`, 'POST', { approvedBy })
export const listBaselines = (gameId: string) =>
  getJson<BaselineRowDto[]>(`/api/games/${gameId}/baselines`)

// 设置与短语
export const getSettings = () => getJson<Record<string, unknown>>('/api/settings')
export const saveSettings = (entries: Record<string, unknown>) =>
  sendJson<Record<string, unknown>>('/api/settings', 'PUT', entries)
export const listPhrases = (gameId: string) => getJson<Phrase[]>(`/api/games/${gameId}/phrases`)
export const addPhrase = (gameId: string, text: string) =>
  sendJson<Phrase>(`/api/games/${gameId}/phrases`, 'POST', { text })
export const removePhrase = (phraseId: number) =>
  sendJson<void>(`/api/phrases/${phraseId}`, 'DELETE')

// ---- 接入向导 ----

export interface OnboardingTask {
  step: number
  title: string
  status: string // todo|doing|done|blocked
  detail: string | null
  updatedAt: string
}

export const getOnboarding = (gameId: string) =>
  getJson<OnboardingTask[]>(`/api/games/${gameId}/onboarding`)
export const updateOnboardingStep = (gameId: string, step: number, status: string, detail?: string) =>
  sendJson<{ message: string }>(`/api/games/${gameId}/onboarding/${step}`, 'POST', { status, detail })
export const setGameStatus = (gameId: string, status: string) =>
  sendJson<{ status: string }>(`/api/games/${gameId}/status`, 'POST', { status })
export const setGate2Driver = (gameId: string, command: string, args: string[], timeoutSeconds?: number) =>
  sendJson<{ message: string; applicable: boolean }>(`/api/games/${gameId}/gate2-driver`, 'POST', {
    command, args, timeoutSeconds,
  })

// LLM 测试连接（可带待验证配置，未保存先测）
export const testLlmConnection = (body: { baseUrl?: string; apiKey?: string; model?: string }) =>
  sendJson<{ ok: boolean; latencyMs: number; error: string | null; replyPreview: string | null }>(
    '/api/settings/llm/test', 'POST', body)

// 门1运行（向导③产物验收用）
export const runGate1 = (gameId: string) =>
  sendJson<{ recordId: number; passed: boolean; report: Record<string, { passed: boolean; issues: string[]; notes: string[] }> | null }>(
    `/api/games/${gameId}/gates/1/run`, 'POST')

// 门3运行（向导⑥上线前对照用）
export const runGate3 = (gameId: string) =>
  sendJson<{
    recordId: number; total: number; matched: number; red: number; yellow: number; newLevels: number
    rows: { levelId: string; verdict: string; status: string; notes: string[] }[]
  }>(`/api/games/${gameId}/gates/3/run`, 'POST')
