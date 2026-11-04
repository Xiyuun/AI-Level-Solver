// 主题纯逻辑测试：设置值 → 主题的归一化（脏数据不该让界面半暗不亮）
import { describe, expect, it } from 'vitest'
import { applyTheme, resolveTheme } from './theme'

describe('主题归一化', () => {
  it('只认 dark，其余一律亮色', () => {
    expect(resolveTheme('dark')).toBe('dark')
    expect(resolveTheme('light')).toBe('light')
    expect(resolveTheme(undefined)).toBe('light')
    expect(resolveTheme(null)).toBe('light')
    expect(resolveTheme('Dark')).toBe('light')   // 大小写不宽容：设置页只写小写
    expect(resolveTheme('')).toBe('light')
    expect(resolveTheme(123)).toBe('light')
  })

  it('浏览器外调用不抛异常（单测环境无 document）', () => {
    expect(() => applyTheme('dark')).not.toThrow()
  })
})
