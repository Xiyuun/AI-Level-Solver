// 接入向导纯逻辑：13 条审核清单（skill 详设 §5 四大组）、六步完成度、上线条件
import type { OnboardingTask } from '../api/client'

// 人工审核清单（checklist.md 条目；薄页面版照单勾选，语义歧义必须转用例禁止直接勾选）
export interface ChecklistItem {
  group: 'A' | 'B' | 'C' | 'D'
  index: number
  text: string
}

export const REVIEW_CHECKLIST: ChecklistItem[] = [
  { group: 'A', index: 1, text: '移动/操作规则一致：边界、阻挡、阻挡后的停位' },
  { group: 'A', index: 2, text: '胜利判定条件一致（逐字核对游戏本体判定）' },
  { group: 'A', index: 3, text: '失败/死局语义（死角算不算死局等）——语义歧义转用例，禁止直接勾选' },
  { group: 'A', index: 4, text: '道具/特殊机制效果一致（若有）' },
  { group: 'A', index: 5, text: '多实体连锁/结算顺序与游戏一致' },
  { group: 'B', index: 6, text: 'StateKey 覆盖状态维度枚举的全部维度（逐维度勾）' },
  { group: 'B', index: 7, text: 'ApplyMove 纯函数（入参不变、无隐藏状态）' },
  { group: 'B', index: 8, text: '排除清单核对：表现层随机/物理未进入规则' },
  { group: 'B', index: 9, text: '预览状态可视覆盖：局部量在实体层、全局量在信息栏实际可见' },
  { group: 'C', index: 10, text: '无引擎引用 / 无 IO / 无网络 / 无时间调用（门1覆盖，抽查）' },
  { group: 'C', index: 11, text: '每条已确认语义都有对应测试用例' },
  { group: 'D', index: 12, text: 'harness 注入走真实输入路径（假对拍检查）' },
  { group: 'D', index: 13, text: '初始/逐步语义状态与胜利信号来自游戏本体（非适配器重算）' },
]

// 六步是否全部完成（上线注册/转正的前提）
export function allStepsDone(tasks: OnboardingTask[]): boolean {
  return tasks.length === 6 && tasks.every((t) => t.status === 'done')
}

// 审核清单是否全部勾选（第④步完成前提）
export function checklistComplete(checked: number[]): boolean {
  return REVIEW_CHECKLIST.every((item) => checked.includes(item.index))
}

// 步骤中文名
export function stepTitle(step: number): string {
  const titles: Record<number, string> = {
    1: '①登记（表单与驱动配置）',
    2: '②资格评估（报告解析存档）',
    3: '③产物验收 + 门1运行卡',
    4: '④人工审核清单（13 条）',
    5: '⑤门2对拍分诊',
    6: '⑥门3基准 diff + 上线注册',
  }
  return titles[step] ?? `步骤 ${step}`
}
