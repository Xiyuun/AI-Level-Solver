// 适配层声明语义的展示格式化：只影响显示，不改变存储
// 规则矩阵/格式说明书是 markdown 原文；机制表/难度权重是 JSON 字符串 → 尽量格式化，解析失败就原样显示
export function prettyJsonLike(value: unknown): string {
  if (typeof value === 'string') {
    try {
      return JSON.stringify(JSON.parse(value), null, 2)
    } catch {
      return value
    }
  }
  try {
    return JSON.stringify(value, null, 2) ?? String(value)
  } catch {
    return String(value)
  }
}
