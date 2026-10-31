// 补间与帧驱动测试：同 id 插值 / 新 id 淡入 / 消失 id 淡出 / setProgress 落帧（规范 §4）
import { describe, expect, it } from 'vitest'
import { frameAt, framesOf, tween } from './tween'
import type { SceneSnapshot, SolutionTapeMirror } from './model'

function snap(entities: { id: string; x: number; y: number }[]): SceneSnapshot {
  return {
    board: { width: 4, height: 4 },
    cells: [],
    entities: entities.map((e) => ({ ...e, type: 'box', props: {} })),
    globals: {},
  }
}

describe('补间', () => {
  const prev = snap([
    { id: 'a', x: 0, y: 0 },
    { id: 'gone', x: 3, y: 3 },
  ])
  const next = snap([
    { id: 'a', x: 2, y: 0 },
    { id: 'born', x: 1, y: 2 },
  ])

  it('同 id 坐标插值：t=0.5 取中点', () => {
    const merged = tween(prev, next, 0.5)
    const a = merged.entities.find((e) => e.id === 'a')!
    expect(a.x).toBe(1)
    expect(a.y).toBe(0)
    expect(a.opacity).toBe(1)
  })

  it('t=0 即前一帧、t=1 即后一帧', () => {
    expect(tween(prev, next, 0).entities.find((e) => e.id === 'a')!.x).toBe(0)
    expect(tween(prev, next, 1).entities.find((e) => e.id === 'a')!.x).toBe(2)
  })

  it('新 id 淡入：透明度 = t', () => {
    expect(tween(prev, next, 0.3).entities.find((e) => e.id === 'born')!.opacity).toBe(0.3)
    expect(tween(prev, next, 1).entities.find((e) => e.id === 'born')!.opacity).toBe(1)
  })

  it('消失 id 淡出：透明度 = 1-t，位置留在原地', () => {
    const gone = tween(prev, next, 0.25).entities.find((e) => e.id === 'gone')!
    expect(gone.opacity).toBe(0.75)
    expect(gone.x).toBe(3)
  })

  it('prev=null（片头）直接返回后一帧', () => {
    const merged = tween(null, next, 0.5)
    expect(merged.entities.find((e) => e.id === 'a')!.x).toBe(2)
    expect(merged.entities.every((e) => e.opacity === 1)).toBe(true)
  })
})

describe('帧驱动（setProgress 唯一入口）', () => {
  const tape = {
    version: 1,
    gameId: 't',
    adapterVer: '1',
    adapterHash: null,
    kernelVer: '0.1.0',
    levelRef: 'x',
    levelHash: 'h',
    contractVer: '1',
    runConfigHash: null,
    moveSchemaVer: 1,
    semanticSchemaVer: 1,
    initial: { semanticState: {}, semanticStateHash: 'i', snapshot: snap([{ id: 'a', x: 0, y: 0 }]) },
    steps: [1, 2, 3].map((i) => ({
      index: i,
      move: {},
      moveText: `step${i}`,
      semanticState: {},
      semanticStateHash: `s${i}`,
      snapshot: snap([{ id: 'a', x: i, y: 0 }]),
    })),
    outcome: 'win',
    trajectoryHash: 't',
    artifactHash: 'a',
  } as SolutionTapeMirror

  it('帧序列 = 初始帧 + 逐步快照', () => {
    const frames = framesOf(tape)
    expect(frames).toHaveLength(4)
    expect(frames[3].entities[0].x).toBe(3)
  })

  it('整数进度精确落帧', () => {
    const frames = framesOf(tape)
    const at2 = frameAt(frames, 2)
    expect(at2.next.entities[0].x).toBe(2)
    expect(at2.prev?.entities[0].x).toBe(1)
    expect(at2.t).toBe(1)
  })

  it('进度 0 为片头（prev=null）', () => {
    const at0 = frameAt(framesOf(tape), 0)
    expect(at0.prev).toBeNull()
  })

  it('小数进度跨帧插值', () => {
    const at = frameAt(framesOf(tape), 1.5)
    expect(at.prev!.entities[0].x).toBe(1)
    expect(at.next.entities[0].x).toBe(2)
    expect(at.t).toBe(0.5)
  })

  it('进度越界被夹取', () => {
    const frames = framesOf(tape)
    expect(frameAt(frames, 99).next.entities[0].x).toBe(3)
    expect(frameAt(frames, -5).prev).toBeNull()
  })
})
