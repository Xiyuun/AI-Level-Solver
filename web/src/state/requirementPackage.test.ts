import { describe, expect, it } from 'vitest'
import { buildRequirementPackage, type RequirementPackageInput } from './requirementPackage'
import type { ProjectAnalysis, ProjectProfile } from '../api/client'

// 需求包纯函数测试：保证"规格块"不会在后续改动里被删掉
// （历史问题：需求包只有项目事实，没有契约/内核/导入/门1 口径，外部助手产出"能编译但不可用"的插件）

const profile: ProjectProfile = {
  scannedAt: '2026-11-08T00:00:00Z',
  projectRoot: 'D:/game',
  engine: 'unity',
  levelDirCandidates: [{ directory: 'Assets/Main/LevelEditor/Luban~/Datas/Levels', jsonCount: 499, namingHint: 'numbered' }],
  sampleFiles: [{
    path: 'Assets/Main/LevelEditor/Luban~/Datas/Levels/000001.json',
    bytes: 1024,
    parsed: true,
    topLevelKeys: 'levelId,difficulty,mapWidth,pigs',
    parseError: null,
    preview: '{"levelId":1,"mapWidth":12,"pigs":[{"id":0}]}',
  }],
  observations: ['引擎识别：unity；项目内共扫描到 537 个 JSON 文件'],
  codeDirCandidates: [{ directory: 'Assets/Main/Game', sourceFileCount: 338 }],
}

const analysis: ProjectAnalysis = {
  analyzedAt: '2026-11-08T00:00:00Z',
  model: 'test',
  fourQuestions: [{ question: '回合制', answer: 'yes', evidence: 'NormalPig.cs:2528' }],
  mechanics: ['点击滑动'],
  checklistDraft: [{ group: 'A', text: '阻挡停位与本体一致' }],
  risks: ['物理耦合'],
  summary: '可接入',
}

function input(overrides: Partial<RequirementPackageInput> = {}): RequirementPackageInput {
  return {
    gameId: 'piggyfarm',
    displayName: 'PiggyFarm（经典模式）',
    engineType: 'unity',
    projectRoot: 'D:/game',
    codeRoot: 'Assets/Main/Game/Modules/ClassicMode/Script',
    profile,
    analysis,
    toolRepoRoot: 'D:/tools/AI-Level-Solver',
    ...overrides,
  }
}

describe('buildRequirementPackage', () => {
  it('包含工具仓库绝对路径与契约/内核职责', () => {
    const text = buildRequirementPackage(input())
    expect(text).toContain('D:/tools/AI-Level-Solver')
    expect(text).toContain('/skills/onboarding-skill/')
    expect(text).toContain('契约与内核职责')
    expect(text).toContain('ReplayEngine.RecordTape')
    expect(text).toContain('AdapterHosting.RunAll')
    expect(text).toContain('DifficultyModel.Evaluate')
    expect(text).toContain('IGameFacade')
  })

  it('包含导入/门1/子集/测试/交付口径', () => {
    const text = buildRequirementPackage(input())
    expect(text).toContain('v1 支持子集')
    expect(text).toContain('RunGate1Checks')
    expect(text).toContain('xUnit')
    expect(text).toContain('不要复制')
    expect(text).toContain('piggyfarm')
    expect(text).toContain('门2 未跑前不得宣称')
  })

  it('附上抽样关卡原文（≤2000 字符）供外部助手少读一次文件', () => {
    const text = buildRequirementPackage(input())
    expect(text).toContain('抽样关卡原文')
    expect(text).toContain('"levelId":1')
    expect(text).toContain('规则语义仍以玩法代码为准')
  })

  it('工具仓库路径缺失时给出可操作警告', () => {
    const text = buildRequirementPackage(input({ toolRepoRoot: null }))
    expect(text).toContain('未配置工具仓库路径')
    expect(text).not.toContain('仓库绝对路径：')
  })

  it('抽样是工具配置时明确警告不要当关卡', () => {
    const toolProfile: ProjectProfile = {
      ...profile,
      levelDirCandidates: [{ directory: 'Tools/Luban', jsonCount: 25, namingHint: 'mixed' }],
      sampleFiles: [{
        path: 'Tools/Luban/Luban.runtimeconfig.json',
        bytes: 328,
        parsed: true,
        topLevelKeys: 'runtimeOptions',
        parseError: null,
      }],
    }
    const text = buildRequirementPackage(input({ profile: toolProfile }))
    expect(text).toContain('工具配置')
    expect(text).toContain('不是关卡数据')
  })
})
