import { useEffect, useState } from 'react'
import { subscribeToast, type ToastItem } from '../state/toast'

// 右下角提示容器：固定定位，不随页面滚动；最多显示 5 条，5 秒自动消失
export function ToastHost() {
  const [items, setItems] = useState<ToastItem[]>([])

  useEffect(() => subscribeToast((item) => {
    setItems((prev) => [...prev.slice(-4), item])
    window.setTimeout(() => {
      setItems((prev) => prev.filter((x) => x.id !== item.id))
    }, 5000)
  }), [])

  if (items.length === 0) return null
  return (
    <div className="toast-host" role="status" aria-live="polite">
      {items.map((item) => (
        <div key={item.id} className={`toast toast-${item.kind}`}>{item.message}</div>
      ))}
    </div>
  )
}
