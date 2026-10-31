// 接入向导（薄页面版六步，冻结决策）：登记/报告存档/产物验收+门1/审核清单/门2分诊/门3diff+上线
// 平台只存档与编排——适配层生成由开发者本机编码 agent + 接入套件 skill 完成（第15步产出）
import { useCallback, useEffect, useState } from 'react'
import {
  getOnboarding,
  runGate1,
  runGate3,
  setGameStatus,
  setGate2Driver,
  updateOnboardingStep,
  type GameDetail,
  type GameSummary,
  type OnboardingTask,
} from '../api/client'
import { REVIEW_CHECKLIST, allStepsDone, checklistComplete, stepTitle } from '../state/onboarding'

export function Onboarding({ game, detail, onStatusChanged }: { game: GameSummary; detail: GameDetail; onStatusChanged: () => void }) {
  const [tasks, setTasks] = useState<OnboardingTask[]>([])
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')

  // ①登记表单
  const [driverCommand, setDriverCommand] = useState('')
  const [driverArgs, setDriverArgs] = useState('')
  const [driverTimeout, setDriverTimeout] = useState(180)

  // ②报告
  const [report, setReport] = useState('')

  // ③门1
  const [gate1, setGate1] = useState<{ passed: boolean; report: Record<string, { passed: boolean; issues: string[]; notes: string[] }> | null } | null>(null)

  // ④清单
  const [checked, setChecked] = useState<number[]>([])

  // ⑤门2
  const [gate2Note, setGate2Note] = useState('')

  // ⑥门3
  const [gate3, setGate3] = useState<Awaited<ReturnType<typeof runGate3>> | null>(null)

  const refresh = useCallback(async () => {
    try {
      setTasks(await getOnboarding(game.gameId))
    } catch (e) {
      setError((e as Error).message)
    }
  }, [game.gameId])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const markStep = async (step: number, status: string, detailText?: string) => {
    setError('')
    try {
      await updateOnboardingStep(game.gameId, step, status, detailText)
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const submitDriver = async () => {
    setError('')
    try {
      const args = driverArgs.split(/\s+/).filter(Boolean)
      await setGate2Driver(game.gameId, driverCommand, args, driverTimeout)
      await markStep(1, 'done', `驱动：${driverCommand} ${driverArgs}`)
      setMessage('门2驱动配置已登记')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const submitReport = async () => {
    await markStep(2, report.trim() ? 'done' : 'blocked', report.trim() || '未提供报告')
  }

  const submitGate1 = async () => {
    setError('')
    try {
      const result = await runGate1(game.gameId)
      setGate1(result)
      if (result.passed) await markStep(3, 'done', '门1五项检查全绿（零豁免）')
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const submitChecklist = async () => {
    if (!checklistComplete(checked)) {
      setError('13 条未全部勾选——语义歧义项请转用例，禁止直接勾选')
      return
    }
    await markStep(4, 'done', `13 条审核清单已确认：${checked.slice().sort((a, b) => a - b).join(',')}`)
  }

  const submitGate2 = async () => {
    // 玩具无游戏本体：如实标记"不适用"，不显示"未通过"（规则矩阵备注口径）
    await markStep(5, 'done', gate2Note.trim() || (game.gate2Applicable ? '门2已运行，见验证记录' : '门2不适用（无独立游戏本体），走人工降级路径'))
  }

  const submitGate3 = async () => {
    setError('')
    try {
      const result = await runGate3(game.gameId)
      setGate3(result)
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const promote = async (status: string) => {
    setError('')
    try {
      await setGameStatus(game.gameId, status)
      await markStep(6, 'done', `状态变更为 ${status}`)
      onStatusChanged()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const statusOf = (step: number) => tasks.find((t) => t.step === step)?.status ?? 'todo'
  const chip = (status: string) => `state-chip state-${status === 'done' ? 'passed' : status === 'blocked' ? 'failed' : status === 'doing' ? 'running' : 'pending'}`

  // 五件产物 + 规则矩阵的在场验收（③的另一半）
  const artifacts: { name: string; ok: boolean }[] = [
    { name: '视觉映射 visual-mapping', ok: !!detail.visualMapping },
    { name: '机制表 mechanics-table', ok: !!detail.mechanics },
    { name: '玩法规则书 game-brief', ok: detail.gameBrief.length > 0 },
    { name: '格式说明书 format-spec', ok: detail.formatSpec.length > 0 },
    { name: '规则矩阵 rule-matrix', ok: detail.ruleMatrix.length > 0 },
  ]

  return (
    <div className="onboarding">
      {message && <div className="ok-message">{message}</div>}
      {error && <div className="error">{error}</div>}

      <div className="ob-head">
        <h3>接入向导 · {game.displayName}</h3>
        <span className={chip(game.status === 'production' ? 'done' : 'doing')}>
          当前状态：{game.statusName}
        </span>
        {allStepsDone(tasks) && game.status !== 'production' && (
          <button className="primary" onClick={() => void promote('production')}>
            六步完成 · 转正上线（production）
          </button>
        )}
      </div>

      <div className="ob-steps">
        {/* ①登记 */}
        <section className={`ob-step ${statusOf(1) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(1)} <span className={chip(statusOf(1))}>{statusOf(1)}</span></h4>
          <div className="ob-facts">
            <div>关卡根目录：<code>{detail.levelsRoot}</code></div>
            <div>门2适用性：<b>{game.gate2Applicable ? '适用（需对拍）' : '不适用（无独立游戏本体）'}</b></div>
          </div>
          <div className="ob-form-row">
            <input placeholder="门2驱动命令（如 Unity.exe 或 cmd）" value={driverCommand} onChange={(e) => setDriverCommand(e.target.value)} />
            <input placeholder="参数（空格分隔，占位符 {levelFile} {tapeFile} {outputFile}）" value={driverArgs} onChange={(e) => setDriverArgs(e.target.value)} />
            <input type="number" min={5} value={driverTimeout} onChange={(e) => setDriverTimeout(Number(e.target.value) || 180)} style={{ width: 90 }} title="超时秒数" />
            <button onClick={() => void submitDriver()} disabled={!driverCommand.trim() || !driverArgs.trim()}>
              登记驱动
            </button>
          </div>
          {game.gate2Applicable === false && (
            <div className="ob-note">无游戏本体的玩法可跳过驱动登记（门2走不适用/人工降级），直接确认完成本步：</div>
          )}
          {statusOf(1) !== 'done' && game.gate2Applicable === false && (
            <button onClick={() => void markStep(1, 'done', '登记完成：门2不适用玩法')}>确认登记完成</button>
          )}
        </section>

        {/* ②资格评估 */}
        <section className={`ob-step ${statusOf(2) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(2)} <span className={chip(statusOf(2))}>{statusOf(2)}</span></h4>
          <p className="ob-note">资格四问（回合制/确定性/状态可枚举/持续产关）由编码 agent 带 skill 评估，此处粘贴报告结论存档：</p>
          <textarea value={report} onChange={(e) => setReport(e.target.value)} placeholder="四问结论 + 证据（文件:行号）+ 卫生度分级与工作量预估" />
          <button onClick={() => void submitReport()}>{report.trim() ? '存档并完成' : '标记受阻（未提供报告）'}</button>
        </section>

        {/* ③产物验收 + 门1 */}
        <section className={`ob-step ${statusOf(3) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(3)} <span className={chip(statusOf(3))}>{statusOf(3)}</span></h4>
          <ul className="artifact-list">
            {artifacts.map((a) => (
              <li key={a.name} className={a.ok ? 'ok' : 'missing'}>
                {a.ok ? '✓' : '✗'} {a.name}
              </li>
            ))}
          </ul>
          <button onClick={() => void submitGate1()}>运行门1（五项合规检查）</button>
          {gate1 && (
            <div className={`gate1-report ${gate1.passed ? 'ok' : 'bad'}`}>
              <div>{gate1.passed ? '门1全绿（零豁免）' : '门1未通过：'}</div>
              {Object.entries(gate1.report ?? {}).map(([name, result]) => (
                <div key={name} className="gate1-line">
                  {result.passed ? '✓' : '✗'} {name}
                  {result.issues.length > 0 && <span className="error-text"> {result.issues.join('；')}</span>}
                </div>
              ))}
            </div>
          )}
        </section>

        {/* ④审核清单 */}
        <section className={`ob-step ${statusOf(4) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(4)} <span className={chip(statusOf(4))}>{statusOf(4)}</span></h4>
          <div className="checklist">
            {REVIEW_CHECKLIST.map((item) => (
              <label key={item.index}>
                <input
                  type="checkbox"
                  checked={checked.includes(item.index)}
                  onChange={(e) =>
                    setChecked((prev) => (e.target.checked ? [...prev, item.index] : prev.filter((i) => i !== item.index)))
                  }
                />
                <span className="group-chip group-{item.group}">{item.group}</span> {item.index}. {item.text}
              </label>
            ))}
          </div>
          <button onClick={() => void submitChecklist()}>确认 {checked.length}/13 条</button>
        </section>

        {/* ⑤门2分诊 */}
        <section className={`ob-step ${statusOf(5) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(5)} <span className={chip(statusOf(5))}>{statusOf(5)}</span></h4>
          {!game.gate2Applicable ? (
            <>
              <div className="ob-note">门2对该玩法<b>不适用</b>（无独立游戏本体）——不显示"未通过"；基准预期由人工确认，执行确认走人工降级。</div>
              <textarea value={gate2Note} onChange={(e) => setGate2Note(e.target.value)} placeholder="降级说明（可选）：人工对拍 10 关 / 签认记录引用" />
            </>
          ) : (
            <div className="ob-note">
              门2运行入口在工作台关卡列表（或 batch CLI）：20 关对拍红灯后按四类分诊——适配层规则错/漏语义 → 修适配层；harness 绕过真实输入路径 → 修 harness；
              关卡数据错位 → 修解析；游戏本体问题 → 上报项目组。
            </div>
          )}
          <button onClick={() => void submitGate2()}>确认本步</button>
        </section>

        {/* ⑥门3 + 上线 */}
        <section className={`ob-step ${statusOf(6) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(6)} <span className={chip(statusOf(6))}>{statusOf(6)}</span></h4>
          <div className="ob-form-row">
            <button onClick={() => void submitGate3()}>运行门3（基准 diff）</button>
            {game.status !== 'trial' && game.status !== 'production' && (
              <button onClick={() => void promote('trial')}>先上试用档（trial，带"未经对拍"水印）</button>
            )}
            {allStepsDone(tasks) && game.status !== 'production' && (
              <button className="primary" onClick={() => void promote('production')}>转正上线（production）</button>
            )}
          </div>
          {gate3 && (
            <div className="gate3-summary">
              <div>
                共 {gate3.total} 关：match {gate3.matched} · <b className="bad-text">红灯 {gate3.red}</b> · 黄灯 {gate3.yellow} · 待确认 {gate3.newLevels}
              </div>
              {gate3.rows.filter((r) => r.verdict !== 'match').slice(0, 12).map((r) => (
                <div key={r.levelId} className={`gate3-row verdict-${r.verdict}`}>
                  {r.levelId}：{r.verdict === 'red' ? '红灯' : r.verdict === 'yellow' ? '黄灯' : '待确认'} — {r.notes[0] ?? ''}
                </div>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  )
}
