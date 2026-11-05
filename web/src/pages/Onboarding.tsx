// 接入向导（三轮 N12 项目化）：所有内容由项目扫描画像驱动——
// ①关卡目录候选预填（扫描产出，用户确认后真回填库）②机器事实区（扫描观察，不冒充判断）
// ③需求包内嵌扫描事实 ④审核清单按项目派生、可增删、按玩法持久化
// 平台仍不做适配层生成（冻结决策）：智能在需求包引导的外部 AI，页面负责喂足事实与验收编排
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  analyzeProject,
  deleteProject,
  detectAdapter,
  getAdapterGenJob,
  getOnboarding,
  rescanProject,
  runGate1,
  runGate3,
  setGameStatus,
  setGate2Driver,
  setGate2NotApplicable,
  setLevelsRoot,
  startAdapterGen,
  updateOnboardingStep,
  uploadPlugin,
  type AdapterDetectResult,
  type GameDetail,
  type GameSummary,
  type OnboardingTask,
  type ProjectAnalysis,
} from '../api/client'
import {
  allStepsDone,
  checklistComplete,
  groupGate3,
  seedChecklistFromAnalysis,
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

  // ②报告存档 + 深度分析
  const [report, setReport] = useState('')
  const [analysis, setAnalysis] = useState<ProjectAnalysis | null>(null)
  const [analyzing, setAnalyzing] = useState(false)

  // 深度分析结果：优先用本会话刚产出的；否则用库里已有的
  const effectiveAnalysis = analysis ?? detail.projectAnalysis

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

  // ④清单初始化：已持久化的用持久化值；否则按 深度分析初稿 > 画像派生 > 纯模板 优先级生成
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
      setItems(seedChecklistFromAnalysis(profile, detail.projectAnalysis))
      setInitialized(true)
    }
  }, [tasks, profile, detail.projectProfile, detail.projectAnalysis, initialized])

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

  // ②深度分析：平台内调设置页模型产出初稿——初稿必须经用户审核，不冒充已确认事实
  const doAnalyze = async () => {
    setAnalyzing(true)
    setError('')
    try {
      const result = await analyzeProject(game.gameId)
      setAnalysis(result)
      setReport((prev) => (prev.trim() ? prev : formatAnalysis(result)))
      setMessage(`深度分析完成（${result.model}）——以下为初稿，请逐条审核`)
      onStatusChanged()   // 库里的分析结论随之刷新
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setAnalyzing(false)
    }
  }

  // 深度分析初稿 → ④清单（与持久化联动）
  const applyChecklistDraft = () => {
    if (!effectiveAnalysis) return
    const next = seedChecklistFromAnalysis(profile, effectiveAnalysis)
    setItems(next)
    setChecked([])
    void persistChecklist('todo', next)
    setMessage('清单初稿已写入④（可在④继续增删）')
  }

  const formatAnalysis = (a: ProjectAnalysis) => [
    `深度分析初稿（${a.model}，${new Date(a.analyzedAt).toLocaleString()}）——请逐条审核后存档：`,
    '',
    ...a.fourQuestions.map((q) => `【${q.answer.toUpperCase()}】${q.question}\n    证据：${q.evidence}`),
    '',
    '机制清单：',
    ...a.mechanics.map((m) => `  - ${m}`),
    '风险点：',
    ...a.risks.map((r) => `  - ${r}`),
    '',
    `总结：${a.summary}`,
  ].join('\n')

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
  // listOverride：调用方刚 setItems 尚未反映到本次渲染闭包时，显式传入新列表（避免旧闭包值覆盖）
  const persistChecklist = async (status: 'done' | 'todo', listOverride?: ChecklistItem[]) => {
    const payload = JSON.stringify({ items: listOverride ?? items, updatedAt: new Date().toISOString() })
    await markStep(4, status, payload)
  }

  const submitChecklist = async () => {
    if (!checklistComplete(checked, items)) {
      setError('清单未全部勾选项请转用例，禁止直接勾选')
      return
    }
    await persistChecklist('done')
  }

  const addChecklistItem = () => {
    const text = newItemText.trim()
    if (!text) return
    const maxIndex = items.reduce((m, i) => Math.max(m, i.index), 0)
    const next = [...items, { group: 'A' as const, index: maxIndex + 1, text, custom: true }]
    setItems(next)
    setNewItemText('')
    void persistChecklist('todo', next)
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
    if (effectiveAnalysis) {
      lines.push('', '# 深度分析结论（平台内模型初稿，已经用户审核环节；仍请核验）')
      for (const q of effectiveAnalysis.fourQuestions) {
        lines.push(`- 四问｜${q.question}：${q.answer}（证据：${q.evidence}）`)
      }
      for (const m of effectiveAnalysis.mechanics) lines.push(`- 机制｜${m}`)
      for (const r of effectiveAnalysis.risks) lines.push(`- 风险｜${r}`)
      if (effectiveAnalysis.summary) lines.push(`- 总结｜${effectiveAnalysis.summary}`)
    }
    lines.push(
      '',
      '# 任务步骤',
      '1. 阅读 LevelForge_glm/skills/onboarding-skill/（onboarding.md=接入总纲；deliverables.md=产物清单；contract.md=接入契约；reference/=实战参考）。',
      '2. 结合上方扫描事实核验关卡数据形态，产出资格评估结论（四问 + 证据），交由用户粘贴回向导②存档。',
      '3. 按 skill 产出一套适配层：关卡解析、状态/动作模型、求解适配，以及规则矩阵/格式说明书/机制表/视觉映射等接入产物。',
      '4. 把门面与适配层编译为独立插件程序集：新建 net10.0 类库项目（放在工具仓库之外的任意目录），ProjectReference 引用仓库内 src/LevelForge.FacadeContracts/LevelForge.FacadeContracts.csproj 与 src/LevelForge.Kernel/LevelForge.Kernel.csproj；门面类实现 IGameFacade。',
      `5. Registration.GameId 必须一字不差为「${game.gameId}」（与已导入玩法行绑定）；编译产出 DLL（连同该插件的非契约依赖 DLL）交付给用户上传到平台向导③——平台检测时自动装载注册并跑门1。不要修改平台源码，不要重新构建平台。`,
      '',
      '# 约束',
      '- 严格遵循接入契约与诚实语义：非法数据必须抛异常，禁止静默修复；未知照实标注，不冒充结论。',
      '- 插件内不要复制 LevelForge.Kernel.dll / LevelForge.FacadeContracts.dll（平台加载器共享平台副本，复制会导致类型分裂）。',
      '- 参考实现：src/Adapters/SokobanToy（最小示例）、src/Adapters/SlidePig（实战参考，含踩坑记录）；门面参考 src/LevelForge.Platform/Facades 下两个内置门面源文件。',
    )
    return lines.join('\n')
  }, [game.displayName, game.engineType, game.gameId, detail.projectRoot, profile, effectiveAnalysis])

  const copyPrompt = async () => {
    try {
      await navigator.clipboard.writeText(adapterPrompt)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('复制失败：请手动选择文本复制')
    }
  }

  // ③步骤B：上传 AI 产出的适配层 DLL（面向策划：不要求会操作文件目录）
  const doUploadPlugin = async (file: File) => {
    setBusy(true)
    setError('')
    try {
      const r = await uploadPlugin(game.gameId, file)
      setUploadedName(r.saved)
      setMessage(`适配层文件已接收：${r.saved}——点「检测并验收」继续`)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // ③步骤C：检测适配层（装载+绑定校验+自动门1）
  const doDetect = async () => {
    setBusy(true)
    setError('')
    try {
      const r = await detectAdapter(game.gameId)
      setDetectResult(r)
      if (r.gate1Passed) {
        await markStep(3, 'done', `适配层已接入并过门1：${r.displayName}（v${r.adapterVersion}）`)
        onStatusChanged()
      }
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // 双入口之一（面向策划）：平台内自动生成——后台任务 + 2s 轮询进度；成功即完成③（自动门1）
  const genTimer = useRef<number | null>(null)
  const doPlatformGen = async () => {
    setBusy(true)
    setError('')
    setGenError('')
    setGenSummary('')
    try {
      const { jobId } = await startAdapterGen(game.gameId)
      setGenStatus('running')
      setGenStage('准备')
      const timer = window.setInterval(async () => {
        try {
          const snap = await getAdapterGenJob(jobId)
          setGenStage(snap.stage)
          setGenRound(snap.round)
          if (snap.status !== 'running') {
            window.clearInterval(timer)
            genTimer.current = null
            setGenStatus(snap.status === 'succeeded' ? 'done' : 'failed')
            if (snap.status === 'failed') setGenError(snap.error ?? snap.report?.summary ?? '生成失败')
            else setGenSummary(snap.report?.summary ?? '')
            if (snap.status === 'succeeded' && snap.report?.gate1Passed) {
              await markStep(3, 'done', `平台内生成适配层并过门1（${snap.report.rounds} 轮）`)
              onStatusChanged()
            }
          }
        } catch { /* 轮询失败下轮重试 */ }
      }, 2000)
      genTimer.current = timer
    } catch (e) {
      setGenStatus('failed')
      setGenError((e as Error).message)
      setBusy(false)
    }
  }
  useEffect(() => () => { if (genTimer.current !== null) window.clearInterval(genTimer.current) }, [])

  const gate3Zones = gate3 ? groupGate3(gate3.rows) : null

  // ③"开始生成适配层"引导卡展开态
  const [launchOpen, setLaunchOpen] = useState(false)
  const [pluginFile, setPluginFile] = useState<File | null>(null)
  const [uploadedName, setUploadedName] = useState('')
  const [detectResult, setDetectResult] = useState<AdapterDetectResult | null>(null)
  // 平台内自动生成（双入口之一，面向策划）：后台任务 + 轮询进度
  const [genStatus, setGenStatus] = useState<'idle' | 'running' | 'done' | 'failed'>('idle')
  const [genStage, setGenStage] = useState('')
  const [genRound, setGenRound] = useState(0)
  const [genError, setGenError] = useState('')
  const [genSummary, setGenSummary] = useState('')

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
                资格四问需要结合游戏代码做语义判断——点下方按钮让平台调用你配置的模型产出<b>初稿</b>，逐条审核后存档；
                也可以把需求包交给外部 AI 评估后粘贴回来。
              </div>
              {!effectiveAnalysis && (
                <button className="primary" onClick={() => void doAnalyze()} disabled={analyzing}>
                  {analyzing ? '深度分析中…（模型推理约需 30~120 秒，请勿关闭页面）' : '开始深度分析（调用设置页的模型）'}
                </button>
              )}
              {effectiveAnalysis && (
                <div className="gate1-cards">
                  {effectiveAnalysis.fourQuestions.map((q) => (
                    <div key={q.question} className={`gate1-card ${q.answer === 'yes' ? 'ok' : q.answer === 'no' ? 'bad' : ''}`}>
                      <div className="gate1-card-head">
                        <span className="gate1-mark">{q.answer === 'yes' ? '✓' : q.answer === 'no' ? '✗' : '?'}</span>
                        <span className="gate1-name">{q.question}</span>
                        <span className={`state-chip ${q.answer === 'yes' ? 'state-passed' : q.answer === 'no' ? 'state-failed' : 'state-pending'}`}>
                          {q.answer === 'yes' ? '满足' : q.answer === 'no' ? '不满足' : '不确定'}
                        </span>
                      </div>
                      <div className="gate1-notes">证据：{q.evidence}</div>
                    </div>
                  ))}
                  {effectiveAnalysis.mechanics.length > 0 && (
                    <div className="gate1-card">
                      <div className="gate1-card-head"><span className="gate1-name">机制清单（{effectiveAnalysis.mechanics.length}）</span></div>
                      <ul className="gate1-issues">{effectiveAnalysis.mechanics.map((m, i) => <li key={i}>{m}</li>)}</ul>
                    </div>
                  )}
                  {effectiveAnalysis.risks.length > 0 && (
                    <div className="gate1-card">
                      <div className="gate1-card-head"><span className="gate1-name">适配层风险点</span></div>
                      <ul className="gate1-issues">{effectiveAnalysis.risks.map((r, i) => <li key={i}>{r}</li>)}</ul>
                    </div>
                  )}
                  <div className="gate1-verdict">{effectiveAnalysis.summary}</div>
                  <button onClick={applyChecklistDraft}>把清单初稿写入④审核清单</button>
                </div>
              )}
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
              <div className="dialog-note">两条路径任选其一，产物都经过同一套验收（自动门1 + ④人工审核）：</div>

              {/* 入口一：平台内自动生成（面向策划——一个按钮闭环） */}
              <div className="gen-entry">
                <div className="gen-entry-head">
                  <b>路径 A · 平台内自动生成</b>
                  <span className="dialog-note">推荐：使用设置页配置的模型，全程本页完成，无需任何外部工具</span>
                </div>
                {genStatus === 'running' ? (
                  <div className="gen-progress">
                    <div>深度生成中…（第 {genRound || 1}/{4} 轮 · {genStage}）</div>
                    <div className="gen-progress-sub">模型推理 + 编译验证需要几分钟，请勿关闭页面；失败会给出转外部路径的建议</div>
                  </div>
                ) : (
                  <button className="primary" onClick={() => void doPlatformGen()} disabled={busy}>
                    {genStatus === 'failed' ? '重新尝试平台内生成' : '生成适配层（平台内自动）'}
                  </button>
                )}
                {genError && <div className="error">{genError}</div>}
                {genSummary && <div className="gate1-notes">总结：{genSummary}</div>}
                {genStatus === 'done' && (
                  <div className="ok-message">适配层已生成并通过门1 ✓——可直接继续④⑤⑥验收；建议检查④清单中 AI 初稿条目。</div>
                )}
              </div>

              <div className="dialog-note" style={{ textAlign: 'center', margin: '4px 0' }}>—— 或者 ——</div>

              {/* 入口二：需求包 → 外部编程助手（面向有编程助手的用户，保上限） */}
              <div className="gen-entry">
                <div className="gen-entry-head">
                  <b>路径 B · 需求包（给外部 AI 编程助手）</b>
                  <span className="dialog-note">适合：有 ZCode / Cursor 等编程助手的用户——用更强的模型与多轮协作拉高适配层质量上限</span>
                </div>
                {!launchOpen ? (
                  <button onClick={() => { setLaunchOpen(true); void copyPrompt() }}>
                    第①步 · 复制需求包给外部编程助手
                  </button>
                ) : (
                  <div className="adapter-handoff">
                    <ol>
                      <li>需求包已复制到剪贴板——粘贴给你的 AI 编程助手执行；<button className="import-recent-item" onClick={() => void copyPrompt()}>再次复制</button>{copied && <span className="ok-inline"> 已复制</span>}</li>
                      <li>它会按需求包产出适配层插件文件（.dll）。</li>
                    </ol>
                    <details>
                      <summary className="import-recent-item" style={{ display: 'inline-block' }}>查看需求包全文</summary>
                      <pre className="adapter-guide-prompt">{adapterPrompt}</pre>
                    </details>
                  </div>
                )}
                <div className="ob-form-row" style={{ marginTop: 8 }}>
                  <input
                    type="file"
                    accept=".dll"
                    onChange={(e) => setPluginFile(e.target.files?.[0] ?? null)}
                    title="选择外部 AI 助手产出的适配层 .dll 文件"
                  />
                  <button onClick={() => pluginFile && void doUploadPlugin(pluginFile)} disabled={!pluginFile || busy}>
                    {busy ? '上传中…' : '第②步 · 上传适配层文件'}
                  </button>
                  <button className="primary" onClick={() => void doDetect()} disabled={busy}>
                    {busy ? '检测中…' : '第③步 · 检测并验收（自动跑门1）'}
                  </button>
                </div>
                {uploadedName && <div className="ok-message">已接收适配层文件：{uploadedName}</div>}
                {detectResult && (
                  <div className={`gate1-verdict ${detectResult.gate1Passed ? 'ok' : 'bad'}`} style={{ marginTop: 10 }}>
                    {detectResult.gate1Passed
                      ? `适配层已接入并通过门1 ✓（${detectResult.displayName} v${detectResult.adapterVersion}）`
                      : `适配层已装载但门1未通过——见下方检查卡；修复后重新上传并检测。${detectResult.gate1Message}`}
                  </div>
                )}
                {detectResult?.gate1Report && (
                  <div className="gate1-cards">
                    {Object.entries(detectResult.gate1Report).map(([name, result]) => (
                      <div key={name} className={`gate1-card ${result.passed ? 'ok' : 'bad'}`}>
                        <div className="gate1-card-head">
                          <span className="gate1-mark">{result.passed ? '✓' : '✗'}</span>
                          <span className="gate1-name">{name}</span>
                        </div>
                        {result.issues.length > 0 && (
                          <ul className="gate1-issues">{result.issues.map((issue) => <li key={issue}>{issue}</li>)}</ul>
                        )}
                        {result.notes.length > 0 && <div className="gate1-notes">{result.notes.join('；')}</div>}
                      </div>
                    ))}
                  </div>
                )}
              </div>
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
