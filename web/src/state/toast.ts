// 轻量全局提示（toast）：解决"操作成功/失败消息在页面顶部、按钮附近无反馈"的通用问题
// 只做展示，不做业务状态；页面里的 message/error 通过 useToastFor 自动映射到右下角提示
export type ToastKind = 'ok' | 'error' | 'info'

export interface ToastItem {
  id: number
  message: string
  kind: ToastKind
}

type Listener = (item: ToastItem) => void

let nextId = 1
const listeners = new Set<Listener>()

export function showToast(message: string, kind: ToastKind = 'ok'): void {
  const text = message?.trim()
  if (!text) return
  const item: ToastItem = { id: nextId++, message: text, kind }
  for (const listener of listeners) listener(item)
}

export function subscribeToast(listener: Listener): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}
