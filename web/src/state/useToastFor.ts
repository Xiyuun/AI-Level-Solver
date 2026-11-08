import { useEffect } from 'react'
import { showToast } from '../state/toast'

// 把页面现有的 message/error 状态映射成右下角提示：任何按钮只要走既有的 setMessage/setError，
// 就会在视口内得到反馈，不再依赖页面顶部的消息条。
export function useToastFor(message: string, error?: string) {
  useEffect(() => {
    if (message) showToast(message, 'ok')
  }, [message])
  useEffect(() => {
    if (error) showToast(error, 'error')
  }, [error])
}
