// 门1 报告 → 可渲染的检查项列表（纯函数）
// 背景：后端 ComplianceReport 曾把派生属性 allPassed 一并序列化，前端按"检查项字典"遍历时
// 把布尔值当成检查项，访问 result.issues.length 抛异常，React 卸载整棵根节点 → 整页白屏。
// 这里统一做类型过滤：只保留"形如检查项"的对象，任何服务端附加字段都不会再打崩页面。
export interface Gate1Check {
  passed: boolean
  issues: string[]
  notes: string[]
}

export function gate1Checks(report: Record<string, unknown> | null | undefined): [string, Gate1Check][] {
  if (!report) return []
  const checks: [string, Gate1Check][] = []
  for (const [name, value] of Object.entries(report)) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) continue
    const item = value as Partial<Gate1Check>
    if (typeof item.passed !== 'boolean') continue
    checks.push([name, {
      passed: item.passed,
      issues: Array.isArray(item.issues) ? item.issues : [],
      notes: Array.isArray(item.notes) ? item.notes : [],
    }])
  }
  return checks
}
