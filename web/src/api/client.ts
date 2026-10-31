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
export const listPhrases = (gameId: string) => getJson<Phrase[]>(`/api/games/${gameId}/phrases`)
export const addPhrase = (gameId: string, text: string) =>
  sendJson<Phrase>(`/api/games/${gameId}/phrases`, 'POST', { text })
export const removePhrase = (phraseId: number) =>
  sendJson<void>(`/api/phrases/${phraseId}`, 'DELETE')
