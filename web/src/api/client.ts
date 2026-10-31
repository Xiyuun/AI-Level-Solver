// 平台 API client：薄封装 + 与后端一致的 camelCase 类型（后端 TapeJson 口径）
// 开发期经 Vite 代理 /api → 平台；打包后同源直连

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

async function getJson<T>(url: string): Promise<T> {
  const resp = await fetch(url)
  if (!resp.ok) throw new Error(`${url} → HTTP ${resp.status}`)
  return (await resp.json()) as T
}

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const resp = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!resp.ok) {
    const text = await resp.text()
    throw new Error(`${url} → HTTP ${resp.status}: ${text.slice(0, 200)}`)
  }
  return (await resp.json()) as T
}

// 玩法列表 / 详情 / 切换
export const listGames = () => getJson<GameSummary[]>('/api/games')
export const getGame = (gameId: string) => getJson<GameDetail>(`/api/games/${gameId}`)
export const activateGame = async (gameId: string): Promise<void> => {
  const resp = await fetch(`/api/games/${gameId}/activate`, { method: 'PUT' })
  if (!resp.ok) throw new Error(`切换玩法失败：HTTP ${resp.status}`)
}

// 关卡列表 / 求解（存量关）/ 磁带原文
export const listLevels = (gameId: string) => getJson<LevelRow[]>(`/api/games/${gameId}/levels`)
export const solveLevel = (gameId: string, levelId: string) =>
  postJson<SolveResponse>('/api/solve', { gameId, levelId })
export const getTape = async (tapeId: number): Promise<unknown> => {
  const resp = await fetch(`/api/tapes/${tapeId}`)
  if (!resp.ok) throw new Error(`磁带获取失败：HTTP ${resp.status}`)
  return resp.json()
}
