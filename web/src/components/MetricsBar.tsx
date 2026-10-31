// 指标条（§1⑫）：难度（目标 vs 评估）、解长及最优性、候选尝试次数、耗时
import type { CandidateAttempt, GenerationRun } from '../api/client'

export function MetricsBar({
  run,
  adopted,
  solutionLength,
}: {
  run: GenerationRun
  adopted: CandidateAttempt | null
  solutionLength: number | null
}) {
  const cells: { label: string; value: string }[] = [
    {
      label: '难度（目标 vs 评估）',
      value: adopted?.difficultyBand
        ? `${run.targetDifficulty} 档 → ${adopted.difficultyBand} 档（${adopted.difficultyScore?.toFixed(1)}）`
        : `${run.targetDifficulty} 档 → 未评`,
    },
    {
      label: '解长及最优性',
      value: adopted ? `${solutionLength ?? '—'} 步（unitCostShortest）` : '—',
    },
    {
      label: '候选尝试次数',
      value: `${run.candidates.length}/${run.candidateLimit}`,
    },
    {
      label: '总耗时',
      value: `${run.totalElapsedMs} ms`,
    },
  ]
  return (
    <div className="metrics-bar">
      {cells.map((cell) => (
        <div key={cell.label} className="metric-cell">
          <div className="metric-label">{cell.label}</div>
          <div className="metric-value">{cell.value}</div>
        </div>
      ))}
    </div>
  )
}
