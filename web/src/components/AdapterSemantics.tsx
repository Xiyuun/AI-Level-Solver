import type { GameDetail } from '../api/client'
import { prettyJsonLike } from '../state/artifacts'

// ③/④ 共用的"适配层声明语义"查看器：把人审需要的产物原文集中展示
// 背景：此前③只显示 5 个 ✓，用户在④要核对规则时看不到规则矩阵/格式说明书内容，只能去翻 API。
export function AdapterSemantics({ detail }: { detail: GameDetail }) {
  const sections = [
    { title: '规则矩阵（rule-matrix）', body: detail.ruleMatrix },
    { title: '格式说明书（format-spec）', body: detail.formatSpec },
    { title: '玩法规则书（game-brief）', body: detail.gameBrief },
    { title: '机制表（mechanics-table）', body: prettyJsonLike(detail.mechanics) },
    { title: '难度权重（difficulty-weights）', body: prettyJsonLike(detail.difficultyWeights) },
  ].filter((s) => (s.body ?? '').trim().length > 0)

  if (sections.length === 0) return null

  return (
    <details className="semantics-viewer">
      <summary>查看适配层声明的语义（规则矩阵 / 格式说明书 / 玩法书 / 机制表 / 难度权重）</summary>
      <div className="semantics-note">
        这里看的是<b>适配层“声称的规则”</b>；声明语义 ≠ 实现语义，最终以适配层代码、试玩回放与门2对拍为准。
        ④审核时用这些文本对照游戏本体逐条核对。
      </div>
      {sections.map((section) => (
        <div key={section.title} className="semantics-section">
          <div className="semantics-title">{section.title}</div>
          <pre className="semantics-pre">{section.body}</pre>
        </div>
      ))}
    </details>
  )
}
