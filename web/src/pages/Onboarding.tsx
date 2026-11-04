// 接入向导（三轮 N12 项目化）：所有内容由项目扫描画像驱动——
// ①关卡目录候选预填（扫描产出，用户确认后真回填库）②机器事实区（扫描观察，不冒充判断）
// ③需求包内嵌扫描事实 ④审核清单按项目派生、可增删、按玩法持久化
// 平台仍不做适配层生成（冻结决策）：智能在需求包引导的外部 AI，页面负责喂足事实与验收编排
import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  deleteProject,
  getOnboarding,
  rescanProject,
  runGate1,
  runGate3,
  setGameStatus,
  setGate2Driver,
  setGate2NotApplicable,
  setLevelsRoot,
  updateOnboardingStep,
  type GameDetail,
  type GameSummary,
  type OnboardingTask,
} from '../api/client'
import {
  allStepsDone,
  checklistComplete,
  groupGate3,
  seedChecklist,
  stepTitle,
  type ChecklistItem,
} from '../state/onboarding'
import { ImportPanel } from '../components/ImportPanel'

export function Onboarding({ game, detail, onStatusChanged, onProjectDeleted }: {
  game: GameSummary
  detail: GameDetail
  onStatusChanged: () => void
  onProjectDeleted?: () => void
}) {
  const [tasks, setTasks] = useState<OnboardingTask[]>([])
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(false)
  const [copied, setCopied] = useState(false)

  const profile = detail.projectProfile

  // ①登记：关卡根目录（扫描候选或手填）+ 门2适用性
  const [chosenRoot, setChosenRoot] = useState('')
  const [manualRoot, setManualRoot] = useState('')
  const [gate2Choice, setGate2Choice] = useState<'unknown' | 'applicable' | 'not'>('unknown')
  const [driverCommand, setDriverCommand] = useState('')
  const [driverArgs, setDriverArgs] = useState('')
  const [driverTimeout, setDriverTimeout] = useState(180)

  // ②报告存档
  const [report, setReport] = useState('')

  // ③门1
  const [gate1, setGate1] = useState<{ passed: boolean; report: Record<string, { passed: boolean; issues: string[]; notes: string[] }> | null } | null>(null)

  // ④清单：按玩法持久化（step4 detail_json = {items:[...]}），模板+画像派生为初稿，可增删
  const [items, setItems] = useState<ChecklistItem[]>([])
  const [checked, setChecked] = useState<number[]>([])
  const [newItemText, setNewItemText] = useState('')
  const [initialized, setInitialized] = useState(false)

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

  // ①表单初值：detail 变化（导入/重扫后）时从画像回填
  useEffect(() => {
    setChosenRoot(detail.levelsRoot || profile?.levelDirCandidates?.[0]?.directory || '')
  }, [detail.gameId, detail.levelsRoot, profile])

  // ④清单初始化：已持久化的用持久化值；否则模板 + 画像派生
  useEffect(() => {
    if (initialized) return
    const step4 = tasks.find((t) => t.step === 4)
    if (step4?.detail) {
      try {
        const parsed = JSON.parse(step4.detail) as { items?: ChecklistItem[] }
        if (Array.isArray(parsed.items) && parsed.items.length > 0) {
          setItems(parsed.items)
          setInitialized(true)
          return
        }
      } catch {
        // 旧格式（纯文本存档）或损坏：走种子路径
      }
    }
    if (detail.projectProfile !== undefined) {   // 详情已加载（null=内置玩法）
      setItems(seedChecklist(profile))
      setInitialized(true)
    }
  }, [tasks, profile, detail.projectProfile, initialized])

  const markStep = async (step: number, status: string, detailText?: string) => {
    setError('')
    try {
      await updateOnboardingStep(game.gameId, step, status, detailText)
      await refresh()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  const doRescan = async () => {
    setBusy(true)
    setError('')
    try {
      await rescanProject(game.gameId)
      setMessage('已重新扫描项目')
      onStatusChanged()   // 详情（画像）随之刷新
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // ①：确认关卡根目录（真回填库；后续关卡导入面板以此为默认目录）
  const submitLevelsRoot = async () => {
    const root = manualRoot.trim() || chosenRoot
    if (!root.trim()) {
      setError('请先从扫描候选中选择，或手动填写关卡根目录')
      return
    }
    setBusy(true)
    setError('')
    try {
      await setLevelsRoot(game.gameId, root.trim())
      setMessage(`关卡根目录已保存：${root.trim()}`)
      onStatusChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // ①：门2适用（填驱动）
  const submitDriver = async () => {
    setBusy(true)
    setError('')
    try {
      const args = driverArgs.split(/\s+/).filter(Boolean)
      await setGate2Driver(game.gameId, driverCommand, args, driverTimeout)
      setGate2Choice('applicable')
      await markStep(1, 'done', `驱动：${driverCommand} ${driverArgs}`)
      setMessage('门2驱动配置已登记')
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // ①：门2不适用（无驱动命令的真登记）
  const submitNotApplicable = async () => {
    setBusy(true)
    setError('')
    try {
      await setGate2NotApplicable(game.gameId)
      setGate2Choice('not')
      await markStep(1, 'done', '登记完成：门2不适用（人工降级路径）')
      setMessage('已登记：门2不适用')
      onStatusChanged()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
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

  // ④：清单持久化（按玩法；items 全量保存，勾选完成才落 done）
  const persistChecklist = async (status: 'done' | 'todo') => {
    const payload = JSON.stringify({ items, updatedAt: new Date().toISOString() })
    await markStep(4, status, payload)
  }

  const submitChecklist = async () => {
    if (!checklistComplete(checked, items)) {
      setError('清单未全部勾选——语义歧义项请转用例，禁止直接勾选')
      return
    }
    await persistChecklist('done')
  }

  const addChecklistItem = () => {
    const text = newItemText.trim()
    if (!text) return
    const maxIndex = items.reduce((m, i) => Math.max(m, i.index), 0)
    setItems((prev) => [...prev, { group: 'A', index: maxIndex + 1, text, custom: true }])
    setNewItemText('')
  }

  const removeChecklistItem = (index: number) => {
    setItems((prev) => prev.filter((i) => i.index !== index))
    setChecked((prev) => prev.filter((i) => i !== index))
  }

  const submitGate2 = async () => {
    await markStep(5, 'done', gate2Note.trim() || (game.gate2Applicable ? '门2已运行，见验证记录' : '门2不适用，走人工降级路径'))
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

  // 产物验收：只在适配层产出后才有意义——未接入时这块整个不渲染（杜绝占位符假绿/假红）
  const artifacts: { name: string; ok: boolean }[] = [
    { name: '视觉映射 visual-mapping', ok: !!detail.visualMapping },
    { name: '机制表 mechanics-table', ok: !!detail.mechanics },
    { name: '玩法规则书 game-brief', ok: detail.gameBrief.length > 0 },
    { name: '格式说明书 format-spec', ok: detail.formatSpec.length > 0 },
    { name: '规则矩阵 rule-matrix', ok: detail.ruleMatrix.length > 0 },
  ]

  // 需求包：含扫描事实，外部 AI 不用从零探索
  const adapterPrompt = useMemo(() => {
    const lines = [
      `请为 LevelForge 平台制作「${game.displayName}」的求解器适配层。`,
      '',
      '# 项目信息',
      `- 引擎类型：${game.engineType ?? '未知'}`,
      `- 项目根目录：${detail.projectRoot ?? '（见下方说明，请向用户确认）'}`,
      `- 注册标识（gameId，必须一字不差）：${game.gameId}`,
      '- 工具仓库：LevelForge_glm',
    ]
    if (profile) {
      lines.push('', '# 项目扫描事实（平台已扫描，可直接采信；如与实际不符请重新扫描）')
      for (const o of profile.observations) lines.push(`- ${o}`)
      if (profile.levelDirCandidates.length > 0) {
        lines.push('- 关卡目录候选（按相似度排序）：' + profile.levelDirCandidates.map((c) => `${c.directory}/(${c.jsonCount}个,${c.namingHint})`).join('、'))
      }
      for (const s of profile.sampleFiles.filter((x) => x.parsed)) {
        lines.push(`- 抽样文件 ${s.path}（${s.bytes}B）顶层键：{${s.topLevelKeys}}`)
      }
    }
    lines.push(
      '',
      '# 任务步骤',
      '1. 阅读 LevelForge_glm/skills/onboarding-skill/（onboarding.md=接入总纲；deliverables.md=产物清单；contract.md=接入契约；reference/=实战参考）。',
      '2. 结合上方扫描事实核验关卡数据形态，产出资格评估结论（四问 + 证据），交由用户粘贴回向导②存档。',
      '3. 按 skill 产出一套适配层：关卡解析、状态/动作模型、求解适配，以及规则矩阵/格式说明书/机制表/视觉映射等接入产物。',
      `4. 把门面登记进 LevelForge_glm/src/LevelForge.Platform/Facades/GameRegistry.cs（GameId 必须是「${game.gameId}」，与已导入的玩法行绑定）。`,
      '5. 重新构建（dotnet build LevelForge_glm/LevelForge.slnx），提示用户重启平台，然后在平台「接入向导」运行门1 验收。',
      '',
      '# 约束',
      '- 严格遵循接入契约与诚实语义：非法数据必须抛异常，禁止静默修复；未知照实标注，不冒充结论。',
      '- 参考实现：src/Adapters/SokobanToy（最小示例）、src/Adapters/SlidePig（实战参考，含踩坑记录）。',
    )
    return lines.join('\n')
  }, [game.displayName, game.engineType, game.gameId, detail.projectRoot, profile])

  const copyPrompt = async () => {
    try {
      await navigator.clipboard.writeText(adapterPrompt)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('复制失败：请手动选择文本复制')
    }
  }

  const gate3Zones = gate3 ? groupGate3(gate3.rows) : null

  // ③"开始生成适配层"引导卡展开态
  const [launchOpen, setLaunchOpen] = useState(false)

  // 顺序解锁（一步接一步）：①完成→②解锁；②完成/受阻→③解锁；④⑤⑥需适配层就绪
  const step1Done = statusOf(1) === 'done'
  const step2Unlocked = step1Done
  const step3Unlocked = step2Unlocked && (statusOf(2) === 'done' || statusOf(2) === 'blocked')
  const verifyUnlocked = step3Unlocked && game.adapterReady   // ④⑤⑥：适配层产出后才有验收对象

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
        {detail.projectRoot && (
          <button
            className="cl-del"
            onClick={() => {
              if (window.confirm(`删除「${game.displayName}」在平台内的全部登记数据（玩法行/向导存档/关卡/验证记录）？不可恢复。`)) {
                void (async () => {
                  setBusy(true)
                  setError('')
                  try {
                    await deleteProject(game.gameId)
                    onProjectDeleted?.()
                  } catch (e) {
                    setError((e as Error).message)
                  } finally {
                    setBusy(false)
                  }
                })()
              }
            }}
            disabled={busy}
            title="删除该项目在平台内的全部数据（从0重测）"
          >
            删除本项目
          </button>
        )}
      </div>

      {/* 项目画像总览：向导里所有内容的数据源（有扫描才能个性化） */}
      {profile ? (
        <div className="profile-card">
          <div className="profile-head">
            <b>项目画像</b>
            <span className="dialog-note">
              扫描于 {new Date(profile.scannedAt).toLocaleString()} · {profile.engine} · {profile.projectRoot}
            </span>
            <button className="import-recent-item" onClick={() => void doRescan()} disabled={busy}>
              {busy ? '扫描中…' : '重新扫描'}
            </button>
          </div>
          <ul className="profile-obs">
            {profile.observations.map((o, i) => <li key={i}>{o}</li>)}
          </ul>
        </div>
      ) : (
        <div className="profile-card">
          <div className="profile-head">
            <b>项目画像</b>
            <span className="dialog-note">该玩法不是导入的项目（内置玩法），无扫描画像。</span>
          </div>
        </div>
      )}

      <div className="ob-steps">
        {/* ①登记：关卡根目录（扫描候选预填）+ 门2适用性 */}
        <section className={`ob-step ${statusOf(1) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(1)} <span className={chip(statusOf(1))}>{statusOf(1)}</span></h4>

          {profile && profile.levelDirCandidates.length > 0 ? (
            <>
              <div className="ob-note">关卡根目录候选（来自项目扫描，按 JSON 聚簇相似度排序）——选定后真回填平台库：</div>
              <div className="ob-options">
                {profile.levelDirCandidates.map((c) => (
                  <label key={c.directory} className="ob-option">
                    <input
                      type="radio"
                      name="level-root"
                      checked={chosenRoot === c.directory}
                      onChange={() => { setChosenRoot(c.directory); setManualRoot('') }}
                    />
                    <span className="ob-option-main"><code>{c.directory}/</code></span>
                    <span className="ob-option-meta">{c.jsonCount} 个 JSON · {c.namingHint}</span>
                  </label>
                ))}
              </div>
            </>
          ) : (
            <div className="ob-note">扫描未发现 JSON 聚簇目录——关卡可能不是 JSON 存储，请手动填写关卡数据目录（或先确认关卡格式）：</div>
          )}
          <div className="ob-form-row">
            {(chosenRoot || profile === null || profile.levelDirCandidates.length === 0) && (
              <input
                placeholder="关卡根目录（绝对路径或项目内相对路径）"
                value={manualRoot || chosenRoot}
                onChange={(e) => { setManualRoot(e.target.value); setChosenRoot('') }}
              />
            )}
            <button onClick={() => void submitLevelsRoot()} disabled={busy || !(manualRoot.trim() || chosenRoot.trim())}>
              保存关卡根目录
            </button>
          </div>
          {detail.levelsRoot && <div className="ob-note">当前已保存：<code>{detail.levelsRoot}</code></div>}

          <div className="ob-note" style={{ marginTop: 12 }}>门2（真机对拍）适用性——这是你的判断，工具不替你猜：</div>
          <div className="ob-options">
            <label className="ob-option">
              <input type="radio" name="gate2-choice" checked={gate2Choice === 'applicable'} onChange={() => setGate2Choice('applicable')} />
              <span className="ob-option-main">适用（我有游戏本体可对拍）</span>
            </label>
            <label className="ob-option">
              <input type="radio" name="gate2-choice" checked={gate2Choice === 'not'} onChange={() => setGate2Choice('not')} />
              <span className="ob-option-main">不适用（无独立游戏本体/暂不对拍）</span>
            </label>
          </div>
          {gate2Choice === 'applicable' && (
            <div className="ob-form-row">
              <input placeholder="门2驱动命令（如 Unity.exe 或 cmd）" value={driverCommand} onChange={(e) => setDriverCommand(e.target.value)} />
              <input placeholder="参数（空格分隔，占位符 {levelFile} {tapeFile} {outputFile}）" value={driverArgs} onChange={(e) => setDriverArgs(e.target.value)} />
              <input type="number" min={5} value={driverTimeout} onChange={(e) => setDriverTimeout(Number(e.target.value) || 180)} style={{ width: 90 }} title="超时秒数" />
              <button onClick={() => void submitDriver()} disabled={!driverCommand.trim() || !driverArgs.trim() || busy}>
                登记驱动并完成本步
              </button>
            </div>
          )}
          {gate2Choice === 'not' && (
            <button onClick={() => void submitNotApplicable()} disabled={busy}>登记为不适用并完成本步</button>
          )}
          {statusOf(1) !== 'done' && gate2Choice === 'unknown' && (
            <div className="ob-note">选择门2适用性后本步才能完成；关卡根目录可以先不填（适配层完成后仍可改）。</div>
          )}
        </section>

        {/* ②资格评估：机器事实（真扫描）+ 外部 AI 判断的存档；①完成后解锁 */}
        <section className={`ob-step ${statusOf(2) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(2)} <span className={chip(statusOf(2))}>{statusOf(2)}</span></h4>
          {!step2Unlocked ? (
            <div className="ob-locked">🔒 完成①登记后解锁本步。</div>
          ) : (
          <>
          {profile ? (
            <>
              <div className="ob-note">以下是<b>项目扫描的机器事实</b>（平台真实读取目录得出，不含语义判断）：</div>
              <table className="profile-table">
                <thead>
                  <tr><th>抽样文件</th><th>大小</th><th>可解析</th><th>顶层键</th></tr>
                </thead>
                <tbody>
                  {profile.sampleFiles.map((s) => (
                    <tr key={s.path}>
                      <td><code>{s.path}</code></td>
                      <td>{s.bytes}B</td>
                      <td>{s.parsed ? '✓' : '✗'}</td>
                      <td>{s.parsed ? `{${s.topLevelKeys}}` : (s.parseError ?? '-')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <div className="ob-note">
                资格四问（回合制/确定性/状态可枚举/持续产关）需要结合游戏代码做语义判断——把需求包交给你的 AI 编程助手评估，把它的结论粘贴到下面存档。平台不替 AI 冒充这份判断。
              </div>
            </>
          ) : (
            <div className="ob-note">内置玩法：资格评估已在接入时完成（存档见 detail_json）。</div>
          )}
          <textarea value={report} onChange={(e) => setReport(e.target.value)} placeholder="四问结论 + 证据（文件:行号）+ 卫生度分级与工作量预估" />
          <button onClick={() => void submitReport()}>{report.trim() ? '存档并完成' : '标记受阻（未提供报告）'}</button>
          </>
          )}
        </section>

        {/* ③开始生成适配层：②完成后解锁；醒目引导卡——复制需求包交给 AI，完成后验证 */}
        <section className={`ob-step ${statusOf(3) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(3)} <span className={chip(statusOf(3))}>{statusOf(3)}</span></h4>
          {!step3Unlocked ? (
            <div className="ob-locked">🔒 完成②资格评估（存档结论或标记受阻）后解锁本步。</div>
          ) : !game.adapterReady ? (
            <div className="adapter-launch">
              <h4>开始生成「{game.displayName}」的求解器适配层</h4>
              <div className="dialog-note">
                平台已备好带项目扫描事实的需求包。生成由你的 AI 编程助手在外部执行（平台冻结决策：不在平台内调模型产代码），
                本页负责把上下文喂足、并在它完成后验收。
              </div>
              {!launchOpen ? (
                <button className="primary" onClick={() => { setLaunchOpen(true); void copyPrompt() }}>
                  开始生成适配层（复制需求包给 AI 助手）
                </button>
              ) : (
                <div className="adapter-handoff">
                  <ol>
                    <li>需求包已复制到剪贴板——粘贴给你的 AI 编程助手（ZCode / Cursor 等）执行；<button className="import-recent-item" onClick={() => void copyPrompt()}>再次复制</button>{copied && <span className="ok-inline"> 已复制</span>}</li>
                    <li>它会按 skills/onboarding-skill 产出适配层，并把门面登记进 GameRegistry（gameId 已绑定本项目）。</li>
                    <li>完成后重新构建（<code>dotnet build LevelForge_glm/LevelForge.slnx</code>）并<b>重启平台</b>。</li>
                    <li>回到本页点下方「验证适配层」运行门1——全绿后即可在④⑤⑥完成验收与转正。</li>
                  </ol>
                  <details>
                    <summary className="import-recent-item" style={{ display: 'inline-block' }}>查看需求包全文</summary>
                    <pre className="adapter-guide-prompt">{adapterPrompt}</pre>
                  </details>
                </div>
              )}
            </div>
          ) : (
            <>
              <div className="ok-message">适配层已接入 ✓ ——下面逐项验收产物，然后运行门1。</div>
              <ul className="artifact-list">
                {artifacts.map((a) => (
                  <li key={a.name} className={a.ok ? 'ok' : 'missing'}>
                    {a.ok ? '✓' : '✗'} {a.name}
                  </li>
                ))}
              </ul>
              <ImportPanel gameId={game.gameId} defaultDirectory={detail.levelsRoot} />
              <button className="primary" onClick={() => void submitGate1()}>验证适配层（运行门1 五项合规检查）</button>
            </>
          )}
          {gate1 && (
            <div className="gate1-cards">
              {Object.entries(gate1.report ?? {}).map(([name, result]) => (
                <div key={name} className={`gate1-card ${result.passed ? 'ok' : 'bad'}`}>
                  <div className="gate1-card-head">
                    <span className="gate1-mark">{result.passed ? '✓' : '✗'}</span>
                    <span className="gate1-name">{name}</span>
                  </div>
                  {result.issues.length > 0 && (
                    <ul className="gate1-issues">
                      {result.issues.map((issue) => (
                        <li key={issue}>{issue}</li>
                      ))}
                    </ul>
                  )}
                  {result.notes.length > 0 && (
                    <div className="gate1-notes">{result.notes.join('；')}</div>
                  )}
                </div>
              ))}
              <div className={`gate1-verdict ${gate1.passed ? 'ok' : 'bad'}`}>
                {gate1.passed ? '门1 全绿（零豁免）' : '门1 未通过：上面 ✗ 的项就是待修的地方'}
              </div>
            </div>
          )}
        </section>

        {/* ④审核清单：模板种子 + 画像派生条目，可增删，按玩法持久化；适配层就绪后解锁 */}
        <section className={`ob-step ${statusOf(4) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(4)} <span className={chip(statusOf(4))}>{statusOf(4)}</span></h4>
          {!verifyUnlocked ? (
            <div className="ob-locked">🔒 本步验收的是适配层实现——完成③（适配层产出并通过门1）后解锁。</div>
          ) : (
          <>
          <div className="ob-note">
            清单 = 通用模板 + 本项目扫描派生条目（<span className="ok-inline">◆</span>）。可直接增删条目；
            语义歧义必须转用例，禁止直接勾选。
          </div>
          <div className="cl-list">
            {items.map((item) => (
              <label key={item.index} className="cl-item">
                <input
                  type="checkbox"
                  checked={checked.includes(item.index)}
                  onChange={(e) =>
                    setChecked((prev) => (e.target.checked ? [...prev, item.index] : prev.filter((i) => i !== item.index)))
                  }
                />
                <span className="group-chip">{item.group}</span>
                <span className="cl-text">{item.index}. {item.text}{item.custom && <span className="cl-badge"> ◆项目派生</span>}</span>
                <button className="cl-del" onClick={() => removeChecklistItem(item.index)} title="删除该条">删除</button>
              </label>
            ))}
          </div>
          <div className="ob-form-row">
            <input
              placeholder="追加本项目特有核对项（如：胡牌判定与本体一致）"
              value={newItemText}
              onChange={(e) => setNewItemText(e.target.value)}
            />
            <button onClick={addChecklistItem} disabled={!newItemText.trim()}>追加</button>
          </div>
          <div className="ob-form-row">
            <button onClick={() => void submitChecklist()}>确认 {checked.length}/{items.length} 条并保存</button>
            <button
              onClick={() => void persistChecklist('todo')}
              title="清单条目保存在该玩法的向导存档里，下次进入还在"
            >
              仅保存清单（不完成）
            </button>
          </div>
          </>
          )}
        </section>

        {/* ⑤门2分诊：适配层就绪后解锁 */}
        <section className={`ob-step ${statusOf(5) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(5)} <span className={chip(statusOf(5))}>{statusOf(5)}</span></h4>
          {!verifyUnlocked ? (
            <div className="ob-locked">🔒 本步处理适配层对拍分诊——完成③后解锁。</div>
          ) : (
          <>
          {!game.gate2Applicable ? (
            <>
              <div className="ob-note">门2对该玩法<b>不适用</b>（①步已登记）——基准预期由人工确认，执行确认走人工降级。</div>
              <textarea value={gate2Note} onChange={(e) => setGate2Note(e.target.value)} placeholder="降级说明（可选）：人工对拍 10 关 / 签认记录引用" />
            </>
          ) : (
            <div className="ob-note">
              门2运行入口在工作台关卡列表（或 batch CLI）：20 关对拍红灯后按四类分诊——适配层规则错/漏语义 → 修适配层；harness 绕过真实输入路径 → 修 harness；
              关卡数据错位 → 修解析；游戏本体问题 → 上报项目组。
            </div>
          )}
          <button onClick={() => void submitGate2()}>确认本步</button>
          </>
          )}
        </section>

        {/* ⑥门3 + 上线：适配层就绪后解锁 */}
        <section className={`ob-step ${statusOf(6) === 'done' ? 'step-done' : ''}`}>
          <h4>{stepTitle(6)} <span className={chip(statusOf(6))}>{statusOf(6)}</span></h4>
          {!verifyUnlocked ? (
            <div className="ob-locked">🔒 本步跑基准 diff 与上线——完成③后解锁。</div>
          ) : (
          <>
          <div className="ob-form-row">
            <button onClick={() => void submitGate3()}>运行门3（基准 diff）</button>
            {game.status !== 'trial' && game.status !== 'production' && (
              <button onClick={() => void promote('trial')}>先上试用档（trial，带"未经对拍"水印）</button>
            )}
            {allStepsDone(tasks) && game.status !== 'production' && (
              <button
                className="primary"
                disabled={gate3Zones?.blocked ?? false}
                title={gate3Zones?.blocked ? '门3 存在红灯，禁止转正' : undefined}
                onClick={() => void promote('production')}
              >
                转正上线（production）
              </button>
            )}
          </div>
          {gate3Zones?.blocked && (
            <div className="failure-advice">
              存在 {gate3Zones.red.length} 个红灯关（{gate3Zones.red.slice(0, 6).join('、')}
              {gate3Zones.red.length > 6 ? '…' : ''}）：结论发生变化，先修复规则差异或确认后更新基准，再重跑门3。
              红灯未清时平台会拒绝转正。
            </div>
          )}
          {gate3 && gate3Zones && (
            <div className="gate3-zones">
              <div className="gate3-zone zone-red">
                <div className="gate3-zone-head">红灯 · 阻断转正<span>{gate3Zones.red.length}</span></div>
                <ul>{gate3Zones.red.length === 0 ? <li className="note">无</li> : gate3Zones.red.map((id) => <li key={id}>{id}</li>)}</ul>
              </div>
              <div className="gate3-zone zone-yellow">
                <div className="gate3-zone-head">黄灯 · 需人判<span>{gate3Zones.yellow.length}</span></div>
                <ul>{gate3Zones.yellow.length === 0 ? <li className="note">无</li> : gate3Zones.yellow.map((id) => <li key={id}>{id}</li>)}</ul>
              </div>
              <div className="gate3-zone zone-new">
                <div className="gate3-zone-head">待确认 · 新关<span>{gate3Zones.fresh.length}</span></div>
                <ul>{gate3Zones.fresh.length === 0 ? <li className="note">无</li> : gate3Zones.fresh.map((id) => <li key={id}>{id}</li>)}</ul>
              </div>
              <div className="gate3-zone zone-match">
                <div className="gate3-zone-head">一致<span>{gate3Zones.match}</span></div>
                <ul><li className="note">结论与基准相同</li></ul>
              </div>
            </div>
          )}
          {gate3 && (
            <details className="gate3-notes">
              <summary>逐关说明（{gate3.rows.filter((r) => r.verdict !== 'match').length} 条非一致）</summary>
              {gate3.rows.filter((r) => r.verdict !== 'match').slice(0, 30).map((r) => (
                <div key={r.levelId} className={`gate3-row verdict-${r.verdict}`}>
                  {r.levelId}：{r.verdict === 'red' ? '红灯' : r.verdict === 'yellow' ? '黄灯' : '待确认'} — {r.notes[0] ?? ''}
                </div>
              ))}
            </details>
          )}
          </>
          )}
        </section>
      </div>
    </div>
  )
}
