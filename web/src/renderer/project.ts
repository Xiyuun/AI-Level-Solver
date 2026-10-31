// 通用渲染器 · 投影核心：场景快照 × 视觉映射 → 纯数据画面项（规范 §1/§3/§4）
// 纯函数无副作用：同一输入永远同一输出；本模块不接触 React/DOM，补间与绘制都消费这里的产物
// 兜底铁律：映射中未声明的 type/prop 用中性灰兜底并记 warning——不因词汇表缺项而崩

import type { EntityItem, GlobalField, PropRenderRule, SceneSnapshot, TypeVisual, VisualMapping } from './model'

// 单个画面项：格子或实体的完整绘制描述
export interface DrawItem {
  layer: 'cell' | 'entity'
  id: string // 格子无 id，用坐标合成；实体用自身 id（补间按 id 对位）
  x: number
  y: number
  shape: TypeVisual['shape']
  fill: string
  size: number
  opacity: number // 补间淡入淡出用，静止帧恒 1
  icon?: string
  iconColor?: string
  arrows: { deg: number; color: string }[] // props arrow 规则产物
  badges: { text: string; color: string }[] // props badge 规则产物
}

// globals 信息栏一行
export interface GlobalRow {
  label: string
  value: string
  missing: boolean // 必需字段缺失：显示"缺失"，不能默认显示 0（§3）
}

// 投影结论：SVG 薄层唯一消费物；warnings 供控制台与覆盖审核提示
export interface ProjectedScene {
  width: number
  height: number
  items: DrawItem[]
  globals: GlobalRow[]
  warnings: string[]
}

// 中性灰兜底样式（§3：未声明 type 的降级）
const FallbackVisual: TypeVisual = { shape: 'block', fill: '#9e9e9e', size: 0.9 }

// 投影主函数：snapshot × mapping → 画面项 + globals 行 + 告警
// opacityById：补间传入的每实体透明度覆盖（缺省 1）
export function project(
  snapshot: SceneSnapshot,
  mapping: VisualMapping,
  opacityById?: ReadonlyMap<string, number>,
): ProjectedScene {
  const warnings: string[] = []
  const items: DrawItem[] = []

  // 格子层：type 查 cellTypes，未声明 → 灰兜底 + 告警
  for (const cell of snapshot.cells) {
    const visual = mapping.cellTypes[cell.type]
    if (!visual) {
      warnings.push(`格子类型 [${cell.type}] 未在视觉映射中声明，已用中性灰兜底`)
    }
    const v = visual ?? FallbackVisual
    items.push({
      layer: 'cell',
      id: `cell:${cell.x},${cell.y}`,
      x: cell.x,
      y: cell.y,
      shape: v.shape,
      fill: v.fill,
      size: v.size ?? 0.9,
      opacity: 1,
      icon: v.icon,
      iconColor: v.iconColor,
      arrows: [],
      badges: [],
    })
  }

  // 实体层：type 查 entityTypes + props 显示规则（arrow/badge/tint）
  for (const entity of snapshot.entities) {
    const visual = mapping.entityTypes[entity.type]
    if (!visual) {
      warnings.push(`实体类型 [${entity.type}] 未在视觉映射中声明，已用中性灰兜底`)
    }
    const v = visual ?? FallbackVisual
    const item = applyProps(entity, v, warnings)
    items.push({
      layer: 'entity',
      id: entity.id,
      x: entity.x,
      y: entity.y,
      shape: item.shape,
      fill: item.fill,
      size: v.size ?? 0.9,
      opacity: opacityById?.get(entity.id) ?? 1,
      icon: v.icon,
      iconColor: v.iconColor,
      arrows: item.arrows,
      badges: item.badges,
    })
  }

  return {
    width: snapshot.board.width,
    height: snapshot.board.height,
    items,
    globals: projectGlobals(snapshot.globals, mapping.globalFields ?? [], warnings),
    warnings,
  }
}

// props 规则应用：tint 覆盖填色、arrow 按值映射角度、badge 出文字角标
function applyProps(
  entity: EntityItem,
  visual: TypeVisual,
  warnings: string[],
): { shape: TypeVisual['shape']; fill: string; arrows: DrawItem['arrows']; badges: DrawItem['badges'] } {
  let fill = visual.fill
  const arrows: DrawItem['arrows'] = []
  const badges: DrawItem['badges'] = []
  const props = entity.props ?? {}
  const rules = visual.props ?? {}
  for (const [name, value] of Object.entries(props)) {
    const rule: PropRenderRule | undefined = rules[name]
    if (!rule) {
      // 未声明的 prop：告警但不绘制（外观与正确性隔离——缺声明不影响其余渲染）
      warnings.push(`实体 [${entity.id}] 的属性 [${name}] 未声明显示规则，未绘制`)
      continue
    }
    if (rule.render === 'tint') {
      if (value) fill = rule.color ?? fill // 真值才着色覆盖
    } else if (rule.render === 'arrow') {
      const deg = rule.rotate?.[String(value)]
      if (deg === undefined) {
        warnings.push(`实体 [${entity.id}] 的属性 [${name}=${String(value)}] 无角度映射，箭头未绘制`)
      } else {
        arrows.push({ deg, color: rule.color ?? '#ffffff' })
      }
    } else if (rule.render === 'badge') {
      badges.push({ text: rule.text ?? String(value), color: rule.color ?? '#333333' })
    }
  }
  return { shape: visual.shape, fill, arrows, badges }
}

// globals 信息栏：按声明顺序显示；缺失→"缺失"；未映射字段按字段名显示并告警（§3）
function projectGlobals(
  globals: Record<string, unknown>,
  fields: GlobalField[],
  warnings: string[],
): GlobalRow[] {
  const rows: GlobalRow[] = []
  for (const field of fields) {
    const value = lookupPath(globals, field.path)
    if (value === undefined) {
      warnings.push(`全局字段 [${field.path}] 缺失（标签"${field.label}"）——不能默认显示 0`)
      rows.push({ label: field.label, value: '缺失', missing: true })
    } else {
      rows.push({ label: field.label, value: formatScalar(value), missing: false })
    }
  }
  // 未映射的全局字段：用字段名和值显示并告警
  const declared = new Set(fields.map((f) => f.path))
  for (const [key, value] of Object.entries(globals)) {
    const root = key.split('.')[0]
    const covered = [...declared].some((d) => d === key || d.split('.')[0] === root && d.includes('.'))
    if (!covered) {
      warnings.push(`全局字段 [${key}] 未在映射 globalFields 中声明，按字段名显示`)
      rows.push({ label: key, value: formatScalar(value), missing: false })
    }
  }
  return rows
}

// 受限字段路径取值：仅按点分段逐级访问，不执行任何表达式
function lookupPath(source: Record<string, unknown>, path: string): unknown {
  let current: unknown = source
  for (const segment of path.split('.')) {
    if (current === null || typeof current !== 'object') return undefined
    current = (current as Record<string, unknown>)[segment]
  }
  return current
}

// 标量格式化：嵌套对象/数组以 JSON 展示（信息栏兜底形态）
function formatScalar(value: unknown): string {
  if (value === null || value === undefined) return String(value)
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}
