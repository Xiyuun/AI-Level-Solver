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
  adapterReady: boolean // 适配层是否已注册（导入未接入的玩法为 false）
  engineType: string | null // 导入项目的引擎识别结果（unity / cocos-creator3 / …；内置玩法为 null）
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
  projectRoot: string | null // 导入项目的根目录（内置玩法为 null）
  projectProfile: ProjectProfile | null // 导入项目的扫描画像（关卡目录候选/抽样/机器观察）
  projectAnalysis: ProjectAnalysis | null // 深度分析结论初稿（平台内 LLM 产出，待用户审核确认）
  pluginsRoot: string | null // 该玩法的适配层插件目录（数据目录/plugins/{gameId}，内置玩法为 null）
}

// 深度分析结论（三轮 N12 完整版 B：初稿语义，必须经用户审核/编辑）
export interface ProjectAnalysis {
  analyzedAt: string
  model: string
  fourQuestions: { question: string; answer: string; evidence: string }[]
  mechanics: string[]
  checklistDraft: { group: string; text: string }[]
  risks: string[]
  summary: string
}

// 项目扫描画像（三轮 N12：全部是机器可直接验证的事实）
export interface ProjectProfile {
  scannedAt: string
  projectRoot: string
  engine: string
  levelDirCandidates: { directory: string; jsonCount: number; namingHint: string }[]
  sampleFiles: { path: string; bytes: number; parsed: boolean; topLevelKeys: string | null; parseError: string | null }[]
  observations: string[]
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

// 项目导入（用户流程第②步：选目录 → 识别 Unity/Cocos → 登记玩法）
export interface ProjectImportResult {
  gameId: string
  displayName: string
  engineType: string
  projectRoot: string
  alreadyImported: boolean
}

// 导入失败时把后端的可操作错误（目录不存在/无法识别/撞名）从 HTTP 错误文本里解出来
export class ProjectImportError extends Error {
  constructor(message: string, readonly conflict: boolean) {
    super(message)
  }
}

export async function importProject(directory: string): Promise<ProjectImportResult> {
  const resp = await fetch('/api/projects/import', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ directory }),
  })
  const text = await resp.text()
  if (!resp.ok) {
    const payload = safeParse(text)
    throw new ProjectImportError(payload?.message ?? `导入失败：HTTP ${resp.status}`, resp.status === 409)
  }
  return JSON.parse(text) as ProjectImportResult
}

export const recentProjects = () => getJson<string[]>('/api/projects/recent')

// 删除导入项目（从0重测）：级联清玩法行/向导存档/关卡/验证记录；仅限未注册适配层的导入项目
export const deleteProject = (gameId: string) =>
  sendJson<{ deleted: boolean; tapeFilesRemoved: number }>(`/api/projects/${gameId}`, 'DELETE')

// 项目重扫（向导内：项目内容变化后触发）与关卡根目录确认（向导①）
export const rescanProject = (gameId: string) =>
  sendJson<{ gameId: string; engineType: string; projectRoot: string }>(`/api/games/${gameId}/rescan`, 'POST')
export const setLevelsRoot = (gameId: string, levelsRoot: string) =>
  sendJson<{ levelsRoot: string }>(`/api/games/${gameId}/levels-root`, 'PUT', { levelsRoot })

// 深度分析（向导②：平台内调设置页模型，产出四问/机制/清单初稿；耗时 30~120s）
export const analyzeProject = (gameId: string) =>
  sendJson<ProjectAnalysis>(`/api/games/${gameId}/analyze`, 'POST')

// 适配层插件（向导③：上传 AI 产出的 DLL → 检测装载+绑定校验+自动门1）
export interface AdapterDetectResult {
  gameId: string
  loaded: string
  displayName: string | null
  adapterVersion: string | null
  adapterHash: string | null
  gate1Passed: boolean
  gate1Report: Record<string, { passed: boolean; issues: string[]; notes: string[] }> | null
  gate1Message: string
}

export const uploadPlugin = async (gameId: string, file: File): Promise<{ saved: string }> => {
  const form = new FormData()
  form.append('file', file)
  const resp = await fetch(`/api/games/${gameId}/plugins/upload`, { method: 'POST', body: form })
  const text = await resp.text()
  if (!resp.ok) {
    const payload = safeParse(text)
    throw new Error(payload?.message ?? `上传失败：HTTP ${resp.status}`)
  }
  return JSON.parse(text)
}

export const detectAdapter = (gameId: string) =>
  sendJson<AdapterDetectResult>(`/api/games/${gameId}/plugins/detect`, 'POST')

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
// 登记"门2不适用"（无需驱动命令；导入项目的确认态，三轮 N11）
export const setGate2NotApplicable = (gameId: string) =>
  sendJson<{ message: string; applicable: boolean }>(`/api/games/${gameId}/gate2-driver`, 'POST', {
    applicable: false,
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
    rows: Gate3Row[]
  }>(`/api/games/${gameId}/gates/3/run`, 'POST')

// 门3单关结论（四区展示用）
export interface Gate3Row {
  levelId: string
  verdict: 'match' | 'red' | 'yellow' | 'new' | string
  status: string
  notes: string[]
}

// 关卡导入（原生格式目录批量入库；子集外类型如实跳过）
export interface LevelImportEntry {
  file: string
  levelId: string | null
  outcome: 'imported' | 'skipped' | 'failed' | string
  reason: string | null
}

export interface LevelImportResult {
  imported: number
  skippedUnsupported: number
  failed: number
  entries: LevelImportEntry[]
  messages: string[]
}

export const importLevels = (gameId: string, directory: string) =>
  sendJson<LevelImportResult>(`/api/games/${gameId}/import-levels`, 'POST', { directory })

// ---- 批量生产（表现层_批量生产）----

export interface BatchItem {
  levelNumber: number
  status: string // pending|running|solved|failed|skipped
  difficultyBand: number | null
  solutionLength: number | null
  generationRunId: number | null
  failureSummary: string | null
  elapsedMs: number
}

export interface BatchStatus {
  taskId: number
  gameId: string
  status: string // running|completed|cancelled
  total: number
  completed: number
  succeeded: number
  failed: number
  skipped: number
  totalElapsedMs: number
  items: BatchItem[]
}

export interface BatchStartRequest {
  fromLevel: number
  toLevel: number
  difficulty1Count: number
  difficulty2Count: number
  difficulty3Count: number
  specialRequirements?: string
  concurrency?: number
  candidateLimit?: number
  solveTimeoutMs?: number
}

export const startBatch = (gameId: string, req: BatchStartRequest) =>
  sendJson<BatchStatus>('/api/batch/start', 'POST', { gameId, ...req })

export const getBatchStatus = (taskId: number) => getJson<BatchStatus>(`/api/batch/${taskId}/status`)

export const cancelBatch = (taskId: number) =>
  sendJson<{ message: string }>(`/api/batch/${taskId}/cancel`, 'POST').then(() => undefined)

export const retryBatchFailed = (taskId: number) =>
  sendJson<BatchStatus>(`/api/batch/${taskId}/retry`, 'POST')

export const adoptBatch = (taskId: number, fromLevel: number, toLevel: number, overwrite = false) =>
  sendJson<{ total: number; ok: number; failed: number; details: { levelNumber: number; ok: boolean; error: string | null }[] }>(
    `/api/batch/${taskId}/adopt`, 'POST', { fromLevel, toLevel, overwrite })

export const exportBatch = (taskId: number) =>
  getJson<{ levelNumber: number; contentJson: string }[]>(`/api/batch/${taskId}/export`)
