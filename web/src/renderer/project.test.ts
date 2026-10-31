// 投影核心测试：映射命中/兜底/props 三规则/globals 缺失与未映射（规范 §3 逐条）
import { describe, expect, it } from 'vitest'
import { project } from './project'
import type { SceneSnapshot, VisualMapping } from './model'

// 与玩具推箱子同款的映射样本（SokobanToyContent.VisualMapping 的 TS 化）
const mapping: VisualMapping = {
  version: 1,
  board: { background: 'surface', grid: true },
  cellTypes: {
    wall: { shape: 'block', fill: '#9aa0ad' },
    goal: { shape: 'rounded', fill: '#e8d5a3', icon: 'target', iconColor: '#8a6d1a' },
  },
  entityTypes: {
    player: { shape: 'circle', fill: '#5a8fd6', size: 0.85 },
    box: {
      shape: 'rounded',
      fill: '#b98a5a',
      size: 0.9,
      props: { onGoal: { render: 'tint', color: '#7fb069' } },
    },
    pig: {
      shape: 'circle',
      fill: '#ee8fa0',
      props: { facing: { render: 'arrow', color: '#ffffff', rotate: { up: 0, right: 90, down: 180, left: 270 } } },
    },
    carrier: {
      shape: 'square',
      fill: '#8f6fd6',
      props: { stock: { render: 'badge', color: '#333333' } },
    },
  },
  globalFields: [
    { path: 'boxesOnGoal', label: '归位箱子', render: 'text' },
    { path: 'totalBoxes', label: '箱子总数', render: 'text' },
  ],
}

function snapshotOf(overrides?: Partial<SceneSnapshot>): SceneSnapshot {
  return {
    board: { width: 5, height: 5 },
    cells: [
      { x: 0, y: 0, type: 'wall' },
      { x: 3, y: 1, type: 'goal' },
    ],
    entities: [
      { id: 'player', type: 'player', x: 1, y: 1, props: {} },
      { id: 'box-0', type: 'box', x: 3, y: 1, props: { onGoal: true } },
    ],
    globals: { boxesOnGoal: 1, totalBoxes: 2 },
    ...overrides,
  }
}

describe('类型与形状映射', () => {
  it('格子与实体按映射绘制', () => {
    const scene = project(snapshotOf(), mapping)
    expect(scene.width).toBe(5)
    const wall = scene.items.find((i) => i.id === 'cell:0,0')!
    expect(wall.shape).toBe('block')
    expect(wall.fill).toBe('#9aa0ad')
    const player = scene.items.find((i) => i.id === 'player')!
    expect(player.shape).toBe('circle')
    expect(player.size).toBe(0.85) // 映射声明的 size 生效
    expect(player.opacity).toBe(1)
  })

  it('未声明 size 的类型取默认 0.9', () => {
    const scene = project(snapshotOf(), mapping)
    expect(scene.items.find((i) => i.id === 'cell:3,1')!.size).toBe(0.9)
  })

  it('未声明的类型用中性灰兜底并告警，不崩', () => {
    const scene = project(
      snapshotOf({ cells: [{ x: 2, y: 2, type: 'ice-unknown' }], entities: [{ id: 'e1', type: 'ghost', x: 2, y: 2 }] }),
      mapping,
    )
    const ghostCell = scene.items.find((i) => i.id === 'cell:2,2')!
    expect(ghostCell.fill).toBe('#9e9e9e')
    const ghostEntity = scene.items.find((i) => i.id === 'e1')!
    expect(ghostEntity.fill).toBe('#9e9e9e')
    expect(scene.warnings.some((w) => w.includes('ice-unknown'))).toBe(true)
    expect(scene.warnings.some((w) => w.includes('ghost'))).toBe(true)
  })

  it('图标随类型声明携带', () => {
    const scene = project(snapshotOf(), mapping)
    expect(scene.items.find((i) => i.id === 'cell:3,1')!.icon).toBe('target')
  })
})

describe('props 显示规则', () => {
  it('tint：真值时着色覆盖', () => {
    const scene = project(snapshotOf(), mapping)
    expect(scene.items.find((i) => i.id === 'box-0')!.fill).toBe('#7fb069') // onGoal=true → 绿
  })

  it('tint：假值保持原色', () => {
    const scene = project(snapshotOf({ entities: [{ id: 'box-0', type: 'box', x: 3, y: 1, props: { onGoal: false } }] }), mapping)
    expect(scene.items.find((i) => i.id === 'box-0')!.fill).toBe('#b98a5a')
  })

  it('arrow：属性值按 rotate 表映射角度', () => {
    const scene = project(
      snapshotOf({ entities: [{ id: 'p1', type: 'pig', x: 1, y: 1, props: { facing: 'right' } }] }),
      mapping,
    )
    const pig = scene.items.find((i) => i.id === 'p1')!
    expect(pig.arrows).toEqual([{ deg: 90, color: '#ffffff' }])
  })

  it('arrow：无角度映射的值不绘制并告警', () => {
    const scene = project(
      snapshotOf({ entities: [{ id: 'p1', type: 'pig', x: 1, y: 1, props: { facing: 'north-east' } }] }),
      mapping,
    )
    expect(scene.items.find((i) => i.id === 'p1')!.arrows).toEqual([])
    expect(scene.warnings.some((w) => w.includes('north-east'))).toBe(true)
  })

  it('badge：值转文字角标', () => {
    const scene = project(
      snapshotOf({ entities: [{ id: 'c1', type: 'carrier', x: 1, y: 1, props: { stock: 3 } }] }),
      mapping,
    )
    expect(scene.items.find((i) => i.id === 'c1')!.badges).toEqual([{ text: '3', color: '#333333' }])
  })

  it('未声明规则的属性：告警但不绘制', () => {
    const scene = project(
      snapshotOf({ entities: [{ id: 'p1', type: 'player', x: 1, y: 1, props: { mood: 'happy' } }] }),
      mapping,
    )
    expect(scene.warnings.some((w) => w.includes('mood'))).toBe(true)
  })
})

describe('globals 信息栏', () => {
  it('按声明顺序显示', () => {
    const scene = project(snapshotOf(), mapping)
    expect(scene.globals.map((g) => g.label)).toEqual(['归位箱子', '箱子总数'])
    expect(scene.globals[0].value).toBe('1')
    expect(scene.globals[0].missing).toBe(false)
  })

  it('缺失字段显示"缺失"而非 0，并告警', () => {
    const scene = project(snapshotOf({ globals: { totalBoxes: 2 } }), mapping)
    const missing = scene.globals.find((g) => g.label === '归位箱子')!
    expect(missing.value).toBe('缺失')
    expect(missing.missing).toBe(true)
    expect(scene.warnings.some((w) => w.includes('boxesOnGoal'))).toBe(true)
  })

  it('未映射的全局字段按字段名显示并告警', () => {
    const scene = project(snapshotOf({ globals: { boxesOnGoal: 1, totalBoxes: 2, secretScore: 42 } }), mapping)
    const extra = scene.globals.find((g) => g.label === 'secretScore')!
    expect(extra.value).toBe('42')
    expect(scene.warnings.some((w) => w.includes('secretScore'))).toBe(true)
  })

  it('嵌套路径取值（点分段，不执行表达式）', () => {
    const nested: VisualMapping = {
      ...mapping,
      globalFields: [{ path: 'tools.remove', label: '移除道具', render: 'text' }],
    }
    const scene = project(snapshotOf({ globals: { tools: { remove: 1 } } }), nested)
    expect(scene.globals[0].value).toBe('1')
  })
})
