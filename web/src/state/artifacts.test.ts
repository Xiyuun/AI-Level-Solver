import { describe, expect, it } from 'vitest'
import { prettyJsonLike } from './artifacts'

describe('prettyJsonLike', () => {
  it('JSON 字符串格式化为缩进文本', () => {
    expect(prettyJsonLike('{"a":1}')).toBe('{\n  "a": 1\n}')
  })

  it('markdown/非 JSON 原文原样返回', () => {
    const md = '# 规则矩阵\n| 规则 | 预期 |'
    expect(prettyJsonLike(md)).toBe(md)
  })

  it('对象直接格式化；循环对象不抛异常', () => {
    expect(prettyJsonLike({ a: 1 })).toBe('{\n  "a": 1\n}')
    const cyclic: Record<string, unknown> = {}
    cyclic.self = cyclic
    expect(typeof prettyJsonLike(cyclic)).toBe('string')
  })
})
