// 候选记录条（§1⑬）：逐次尝试结论（通过/模型内无解/预算耗尽/难度不达标/格式不合法…）
import type { CandidateAttempt } from '../api/client'
import { candidateLabel } from '../state/workbench'

export function CandidateStrip({
  candidates,
  adoptedAttempt,
  selected,
  onSelect,
}: {
  candidates: CandidateAttempt[]
  adoptedAttempt: number | null
  selected: number
  onSelect: (attemptIndex: number) => void
}) {
  return (
    <div className="candidate-strip">
      {candidates.map((candidate) => (
        <button
          key={candidate.attemptIndex}
          className={`candidate-chip chip-${candidate.failureClass} ${candidate.attemptIndex === selected ? 'chip-selected' : ''} ${candidate.attemptIndex === adoptedAttempt ? 'chip-adopted' : ''}`}
          onClick={() => onSelect(candidate.attemptIndex)}
          title={candidate.failureDetail ?? ''}
        >
          <span className="chip-index">#{candidate.attemptIndex}</span>
          <span className="chip-label">{candidateLabel(candidate)}</span>
          {candidate.difficultyBand !== null && <span className="chip-difficulty">{candidate.difficultyBand} 档</span>}
        </button>
      ))}
    </div>
  )
}
