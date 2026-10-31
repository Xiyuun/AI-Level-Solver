// 相邻快照补间与帧驱动（规范 §4：同 id 坐标插值、新 id 淡入、消失 id 淡出）
// 播放/单步/重置共用一个进度接口——progress 为"帧坐标"（0..N，可为小数）

import type { SceneSnapshot, SolutionTapeMirror } from './model'

// 补间产物：实体位置/透明度已合成，cells 恒取后一帧（格子层静止）
export interface TweenedSnapshot {
  board: SceneSnapshot['board']
  cells: SceneSnapshot['cells']
  entities: (SceneSnapshot['entities'][number] & { opacity: number })[]
}

// 两帧插值：prev=null（片头）时直接返回 next；t∈[0,1]，t=0 即 prev、t=1 即 next
export function tween(
  prev: SceneSnapshot | null,
  next: SceneSnapshot,
  t: number,
): TweenedSnapshot {
  const clamped = Math.max(0, Math.min(1, t))
  if (prev === null) {
    return { board: next.board, cells: next.cells, entities: next.entities.map((e) => ({ ...e, opacity: 1 })) }
  }
  const nextById = new Map(next.entities.map((e) => [e.id, e]))
  const prevById = new Map(prev.entities.map((e) => [e.id, e]))
  const entities: TweenedSnapshot['entities'] = []

  // 后一帧仍存在的实体：同 id 坐标插值
  for (const target of next.entities) {
    const before = prevById.get(target.id)
    if (before) {
      entities.push({
        ...target,
        x: lerp(before.x, target.x, clamped),
        y: lerp(before.y, target.y, clamped),
        opacity: 1,
      })
    } else {
      entities.push({ ...target, opacity: clamped }) // 新 id 淡入
    }
  }
  // 前一帧有、后一帧没有的实体：淡出，位置留在原地
  for (const before of prev.entities) {
    if (!nextById.has(before.id)) {
      entities.push({ ...before, opacity: 1 - clamped })
    }
  }
  return { board: next.board, cells: next.cells, entities }
}

// 帧序列：初始帧 + 逐步快照（回放按 steps 切换 snapshot，§5）
export function framesOf(tape: SolutionTapeMirror): SceneSnapshot[] {
  return [tape.initial.snapshot, ...tape.steps.map((s) => s.snapshot)]
}

// 进度 → 两帧 + 插值系数（setProgress 的唯一实现入口）
// 整数进度 i → 精确落帧 i（t=1）；小数进度 i+f → 帧 i 与帧 i+1 之间按 f 插值
export function frameAt(
  frames: SceneSnapshot[],
  progress: number,
): { prev: SceneSnapshot | null; next: SceneSnapshot; t: number } {
  if (frames.length === 0) throw new Error('帧序列为空')
  const clamped = Math.max(0, Math.min(frames.length - 1, progress))
  const index = Math.floor(clamped)
  const frac = clamped - index
  if (frac === 0) {
    return { prev: index === 0 ? null : frames[index - 1], next: frames[index], t: 1 }
  }
  return { prev: frames[index], next: frames[index + 1], t: frac }
}

// 线性插值（补间固定一种缓动的基底：匀速）
function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t
}
