// 流水线日志：五色角色标签行（§1⑩：LLM 蓝 / 求解器紫 / 难度模型橙 / 约束绿 / 集成青——与原型一致，五色固定）
import type { LogEntry } from '../state/workbench'

const roleLabels: Record<LogEntry['role'], string> = {
  llm: 'LLM',
  solver: '求解器',
  difficulty: '难度模型',
  constraint: '约束',
  integration: '集成',
}

export function PipelineLog({ entries }: { entries: LogEntry[] }) {
  if (entries.length === 0) {
    return <div className="pipeline-log empty-log">生成后此处显示逐候选流水（出题→判卷→难度→分类→重生）</div>
  }
  return (
    <div className="pipeline-log" role="log">
      {entries.map((entry, i) => (
        <div key={i} className={`log-line log-${entry.role}`}>
          <span className="log-role">{roleLabels[entry.role]}</span>
          <span className="log-text">{entry.text}</span>
        </div>
      ))}
    </div>
  )
}
