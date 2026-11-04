// 主题上下文：设置页的 ui.theme 是唯一来源，App 负责写到 <html data-theme> 并向下广播
// 组件只需要知道"现在是什么主题"（SVG 调色板），CSS 侧完全靠 data-theme 变量，无需 JS 参与
import { createContext, useContext } from 'react'

export type Theme = 'light' | 'dark'

export const ThemeContext = createContext<Theme>('light')

// 读取当前主题（默认亮色，避免无 Provider 时炸掉）
export function useTheme(): Theme {
  return useContext(ThemeContext)
}

// 设置值 → 主题：只认 'dark'，其余一律亮色（脏数据不该让界面变成半暗不亮）
export function resolveTheme(value: unknown): Theme {
  return value === 'dark' ? 'dark' : 'light'
}

// 把主题写到文档根节点：CSS 的 [data-theme='dark'] 选择器据此生效
// 浏览器外（单测/SSR）调用无副作用，不做 document 存在性判断会让纯逻辑测试没法跑
export function applyTheme(theme: Theme): void {
  if (typeof document === 'undefined') return
  document.documentElement.dataset.theme = theme
}
