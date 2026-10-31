// 渲染器与视觉映射规范 §2/§3/§5 的 TypeScript 镜像类型
// 平台与磁带的 JSON 均为 camelCase（TapeJson 口径），这里按线格式逐一对应

// ---- 场景快照（磁带 initial/steps 中的显示投影）----

export interface BoardSize {
  width: number // 列数
  height: number // 行数
}

// 格子层：无 id，type 由游戏声明（wall/goal/ice……）
export interface CellItem {
  x: number // 列
  y: number // 行
  type: string
}

// 实体层：id 在一次关卡执行内稳定且唯一；props 为游戏自定义属性
export interface EntityItem {
  id: string
  type: string
  x: number
  y: number
  props?: Record<string, unknown>
}

// 全局量信息栏：标量或嵌套对象（globalFields.path 取值）
export interface SceneSnapshot {
  board: BoardSize
  cells: CellItem[]
  entities: EntityItem[]
  globals: Record<string, unknown>
}

// ---- 视觉映射（visual-mapping.json，纯数据零代码）----

// v1 四种形状：圆 / 圆角 / 方块 / 满格块
export type Shape = 'circle' | 'rounded' | 'square' | 'block'

// props 的显示规则：arrow 旋转指示 / badge 文字角标 / tint 着色覆盖
export interface PropRenderRule {
  render: 'arrow' | 'badge' | 'tint'
  color?: string
  rotate?: Record<string, number> // arrow 用：属性值 → 角度
  text?: string // badge 用：值 → 文案（缺省用值本身）
}

// 一种 type 的视觉声明
export interface TypeVisual {
  shape: Shape
  fill: string
  size?: number // 相对格子边长比例，默认 0.9
  icon?: string // 内置图标白名单：target/flag/star/heart/gem/key/bomb/skull/check/cross
  iconColor?: string
  props?: Record<string, PropRenderRule>
}

// globals 信息栏字段声明（按顺序显示；path 受限字段路径，不执行表达式）
export interface GlobalField {
  path: string
  label: string
  render: string // v1 仅 text
}

export interface VisualMapping {
  version: number
  board?: { background?: string; grid?: boolean }
  cellTypes: Record<string, TypeVisual>
  entityTypes: Record<string, TypeVisual>
  globalFields?: GlobalField[]
}

// ---- 解法磁带（渲染器只消费 initial/steps 的 snapshot）----

export interface TapeInitial {
  semanticState: unknown
  semanticStateHash: string
  snapshot: SceneSnapshot
}

export interface TapeStep {
  index: number
  move: unknown
  moveText: string
  semanticState: unknown
  semanticStateHash: string
  snapshot: SceneSnapshot
}

export interface SolutionTapeMirror {
  version: number
  gameId: string
  adapterVer: string
  adapterHash: string | null
  kernelVer: string
  levelRef: string
  levelHash: string
  contractVer: string
  runConfigHash: string | null
  moveSchemaVer: number
  semanticSchemaVer: number
  initial: TapeInitial
  steps: TapeStep[]
  outcome: 'win' | 'lose' | 'incomplete' | 'error'
  trajectoryHash: string
  artifactHash: string
}
