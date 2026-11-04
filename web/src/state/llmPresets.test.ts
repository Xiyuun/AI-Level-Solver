// LLM 预设与地址归一化：拼 URL 的坑必须在纯逻辑层拦住
import { describe, expect, it } from 'vitest'
import { LLM_PRESETS, normalizeBaseUrl } from './llmPresets'

describe('LLM 地址归一化', () => {
  it('去掉尾部斜杠', () => {
    expect(normalizeBaseUrl('https://api.deepseek.com/')).toBe('https://api.deepseek.com')
    expect(normalizeBaseUrl('https://api.deepseek.com///')).toBe('https://api.deepseek.com')
  })

  it('去掉误填的 /chat/completions，避免拼成双份', () => {
    expect(normalizeBaseUrl('https://api.deepseek.com/chat/completions')).toBe('https://api.deepseek.com')
    expect(normalizeBaseUrl('https://api.openai.com/v1/chat/completions')).toBe('https://api.openai.com/v1')
  })

  it('保留 /v1 这类路径前缀（不能一起剥掉）', () => {
    expect(normalizeBaseUrl('https://api.openai.com/v1')).toBe('https://api.openai.com/v1')
    expect(normalizeBaseUrl('https://open.bigmodel.cn/api/paas/v4')).toBe('https://open.bigmodel.cn/api/paas/v4')
  })

  it('空串与空白安全', () => {
    expect(normalizeBaseUrl('   ')).toBe('')
  })
})

describe('预设清单', () => {
  it('每条预设都有完整三项且地址可归一', () => {
    for (const preset of LLM_PRESETS) {
      expect(preset.name.length).toBeGreaterThan(0)
      expect(preset.model.length).toBeGreaterThan(0)
      expect(normalizeBaseUrl(preset.baseUrl)).toMatch(/^https:\/\//)
      expect(normalizeBaseUrl(preset.baseUrl)).not.toMatch(/chat\/completions$/)
    }
  })

  it('名称不重复', () => {
    expect(new Set(LLM_PRESETS.map((p) => p.name)).size).toBe(LLM_PRESETS.length)
  })
})
