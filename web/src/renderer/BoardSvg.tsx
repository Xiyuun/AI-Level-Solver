// 通用 SVG 渲染薄层：投影产物 → SVG 元素（规范 §4 组件接口 render(prev, next, t)）
// 刻意不模仿任何游戏的 UI——所有外观来自视觉映射数据，本层只有"画什么形状"的知识

import { useMemo } from 'react'
import type { SceneSnapshot, VisualMapping } from './model'
import { project, type DrawItem } from './project'
import { tween } from './tween'

// 主题（亮/暗只影响底板与网格线，映射色不动）
export type Theme = 'light' | 'dark'

export interface BoardSvgProps {
  prev: SceneSnapshot | null // 前一帧（补间用；片头传 null）
  next: SceneSnapshot // 后一帧
  t: number // 插值系数 0..1
  mapping: VisualMapping
  theme?: Theme
  cell?: number // 单格像素边长，默认 48
  onEntityClick?: (entityId: string) => void // 试玩入口（§6 后端步进由页面接）
  warnings?: string[] // 投影告警回传（页面转控制台/覆盖审核提示）
}

// 棋盘组件：格子层 → 实体层（后画在上）→ globals 由页面另排信息栏
export function BoardSvg(props: BoardSvgProps) {
  const { prev, next, t, mapping, theme = 'light', cell = 48, onEntityClick } = props
  const scene = useMemo(() => {
    // 补间合成实体位置/透明度 → 投影（投影只认快照形态）
    const merged = tween(prev, next, t)
    const snapshot: SceneSnapshot = {
      board: merged.board,
      cells: merged.cells,
      entities: merged.entities.map((e) => ({
        id: e.id,
        type: e.type,
        x: e.x,
        y: e.y,
        props: e.props,
      })),
      globals: next.globals, // globals 取后一帧（信息栏随最新状态走）
    }
    const opacityById = new Map(merged.entities.map((e) => [e.id, e.opacity]))
    return project(snapshot, mapping, opacityById)
  }, [prev, next, t, mapping])
  props.warnings?.splice(0, props.warnings.length, ...scene.warnings)

  const palette = theme === 'dark' ? darkPalette : lightPalette
  const width = scene.width * cell
  const height = scene.height * cell

  return (
    <svg
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      style={{ background: palette.surface, borderRadius: 8 }}
      role="img"
    >
      {mapping.board?.grid !== false && <GridLines width={scene.width} height={scene.height} cell={cell} palette={palette} />}
      {scene.items
        .filter((i) => i.layer === 'cell')
        .map((item) => (
          <ItemShape key={item.id} item={item} cell={cell} />
        ))}
      {scene.items
        .filter((i) => i.layer === 'entity')
        .map((item) => (
          <g key={item.id} onClick={onEntityClick ? () => onEntityClick(item.id) : undefined} style={{ cursor: onEntityClick ? 'pointer' : undefined }}>
            <ItemShape item={item} cell={cell} />
          </g>
        ))}
    </svg>
  )
}

// 单个画面项的形状绘制：block 满格 / square 居中方块 / rounded 圆角 / circle 圆
function ItemShape({ item, cell }: { item: DrawItem; cell: number }) {
  const size = cell * item.size
  const offset = (cell - size) / 2
  const cx = item.x * cell + cell / 2
  const cy = item.y * cell + cell / 2
  const opacity = item.opacity
  const shapes: Record<DrawItem['shape'], React.ReactNode> = {
    block: <rect x={item.x * cell} y={item.y * cell} width={cell} height={cell} fill={item.fill} opacity={opacity} />,
    square: <rect x={item.x * cell + offset} y={item.y * cell + offset} width={size} height={size} fill={item.fill} opacity={opacity} />,
    rounded: (
      <rect x={item.x * cell + offset} y={item.y * cell + offset} width={size} height={size} rx={size * 0.22} fill={item.fill} opacity={opacity} />
    ),
    circle: <circle cx={cx} cy={cy} r={size / 2} fill={item.fill} opacity={opacity} />,
  }
  return (
    <g>
      {shapes[item.shape]}
      {item.icon && <Icon name={item.icon} cx={cx} cy={cy} r={size / 2} color={item.iconColor ?? '#000000'} opacity={opacity} />}
      {item.arrows.map((arrow, i) => (
        <Arrow key={i} cx={cx} cy={cy} r={size / 2} deg={arrow.deg} color={arrow.color} opacity={opacity} />
      ))}
      {item.badges.map((badge, i) => (
        <text key={i} x={cx + size / 2 - 2} y={cy - size / 2 + 4} fontSize={cell * 0.28} fill={badge.color} textAnchor="end" opacity={opacity}>
          {badge.text}
        </text>
      ))}
    </g>
  )
}

// 内置图标白名单（v1 约 10 个；矢量绘制，不引外部资源）
function Icon({ name, cx, cy, r, color, opacity }: { name: string; cx: number; cy: number; r: number; color: string; opacity: number }) {
  switch (name) {
    case 'target':
      return (
        <g opacity={opacity} stroke={color} fill="none">
          <circle cx={cx} cy={cy} r={r * 0.55} strokeWidth={r * 0.14} />
          <circle cx={cx} cy={cy} r={r * 0.18} fill={color} stroke="none" />
        </g>
      )
    case 'flag':
      return (
        <g opacity={opacity}>
          <rect x={cx - r * 0.08} y={cy - r * 0.6} width={r * 0.16} height={r * 1.2} fill={color} />
          <polygon points={`${cx + r * 0.08},${cy - r * 0.6} ${cx + r * 0.7},${cy - r * 0.35} ${cx + r * 0.08},${cy - r * 0.1}`} fill={color} />
        </g>
      )
    case 'star':
      return <Star cx={cx} cy={cy} r={r * 0.6} color={color} opacity={opacity} />
    case 'heart':
      return (
        <path
          d={`M ${cx} ${cy + r * 0.5} C ${cx - r * 1.1} ${cy - r * 0.4}, ${cx - r * 0.4} ${cy - r * 1.0}, ${cx} ${cy - r * 0.35} C ${cx + r * 0.4} ${cy - r * 1.0}, ${cx + r * 1.1} ${cy - r * 0.4}, ${cx} ${cy + r * 0.5} Z`}
          fill={color}
          opacity={opacity}
        />
      )
    case 'gem':
      return (
        <g opacity={opacity}>
          <polygon points={`${cx},${cy - r * 0.6} ${cx + r * 0.55},${cy - r * 0.15} ${cx},${cy + r * 0.6} ${cx - r * 0.55},${cy - r * 0.15}`} fill={color} />
        </g>
      )
    case 'key':
      return (
        <g opacity={opacity} stroke={color} strokeWidth={r * 0.14} fill="none">
          <circle cx={cx - r * 0.25} cy={cy} r={r * 0.3} />
          <line x1={cx + r * 0.05} y1={cy} x2={cx + r * 0.6} y2={cy} />
          <line x1={cx + r * 0.45} y1={cy} x2={cx + r * 0.45} y2={cy + r * 0.3} />
        </g>
      )
    case 'bomb':
      return (
        <g opacity={opacity}>
          <circle cx={cx - r * 0.1} cy={cy + r * 0.1} r={r * 0.5} fill={color} />
          <line x1={cx + r * 0.25} y1={cy - r * 0.25} x2={cx + r * 0.55} y2={cy - r * 0.55} stroke={color} strokeWidth={r * 0.12} />
        </g>
      )
    case 'skull':
      return (
        <g opacity={opacity}>
          <circle cx={cx} cy={cy - r * 0.1} r={r * 0.5} fill={color} />
          <rect x={cx - r * 0.22} y={cy + r * 0.25} width={r * 0.44} height={r * 0.3} fill={color} />
        </g>
      )
    case 'check':
      return (
        <path
          d={`M ${cx - r * 0.5} ${cy} L ${cx - r * 0.1} ${cy + r * 0.4} L ${cx + r * 0.55} ${cy - r * 0.4}`}
          stroke={color}
          strokeWidth={r * 0.18}
          fill="none"
          opacity={opacity}
        />
      )
    case 'cross':
      return (
        <g stroke={color} strokeWidth={r * 0.18} opacity={opacity}>
          <line x1={cx - r * 0.45} y1={cy - r * 0.45} x2={cx + r * 0.45} y2={cy + r * 0.45} />
          <line x1={cx + r * 0.45} y1={cy - r * 0.45} x2={cx - r * 0.45} y2={cy + r * 0.45} />
        </g>
      )
    default:
      return null // 未知图标名：不绘制不崩（词汇表缺项由投影层告警兜底）
  }
}

// 五角星路径
function Star({ cx, cy, r, color, opacity }: { cx: number; cy: number; r: number; color: string; opacity: number }) {
  const points: string[] = []
  for (let i = 0; i < 10; i++) {
    const radius = i % 2 === 0 ? r : r * 0.45
    const angle = (Math.PI / 5) * i - Math.PI / 2
    points.push(`${cx + radius * Math.cos(angle)},${cy + radius * Math.sin(angle)}`)
  }
  return <polygon points={points.join(' ')} fill={color} opacity={opacity} />
}

// 方向箭头（props render=arrow）：绕实体中心旋转的三角
function Arrow({ cx, cy, r, deg, color, opacity }: { cx: number; cy: number; r: number; deg: number; color: string; opacity: number }) {
  const tip = r * 0.55
  const base = r * 0.3
  return (
    <polygon
      points={`0,${-tip} ${base},${tip * 0.6} ${-base},${tip * 0.6}`}
      fill={color}
      opacity={opacity}
      transform={`translate(${cx} ${cy}) rotate(${deg})`}
    />
  )
}

// 网格线（board.grid 默认开）
function GridLines({ width, height, cell, palette }: { width: number; height: number; cell: number; palette: Palette }) {
  const lines: React.ReactNode[] = []
  for (let x = 1; x < width; x++) {
    lines.push(<line key={`v${x}`} x1={x * cell} y1={0} x2={x * cell} y2={height * cell} stroke={palette.grid} strokeWidth={1} />)
  }
  for (let y = 1; y < height; y++) {
    lines.push(<line key={`h${y}`} x1={0} y1={y * cell} x2={width * cell} y2={y * cell} stroke={palette.grid} strokeWidth={1} />)
  }
  return <g>{lines}</g>
}

interface Palette {
  surface: string
  grid: string
}

const lightPalette: Palette = { surface: '#f4f5f7', grid: '#e0e2e6' }
const darkPalette: Palette = { surface: '#26282e', grid: '#3a3d45' }
