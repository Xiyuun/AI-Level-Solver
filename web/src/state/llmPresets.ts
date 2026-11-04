// LLM 供应商预设：只填「OpenAI 兼容端点根地址 + 一个常见模型 id」
// 工具只会拼 {地址}/chat/completions，所以这里填的是根地址（不带 /chat/completions）
// 模型 id 以供应商控制台为准——各家都会改名，这里给的是"能跑起来的起点"，不是长期承诺
export interface LlmPreset {
  name: string
  baseUrl: string
  model: string
  note: string
}

export const LLM_PRESETS: LlmPreset[] = [
  {
    name: 'DeepSeek',
    baseUrl: 'https://api.deepseek.com',
    model: 'deepseek-chat',
    note: 'OpenAI 兼容；deepseek-chat 为非思考档，本工具的"单次出题"场景够用且延迟低',
  },
  {
    name: '智谱 GLM',
    baseUrl: 'https://open.bigmodel.cn/api/paas/v4',
    model: 'glm-4-flash',
    note: '本项目实测所用；flash 档便宜但在"精确计数"类格式上容易出错（坐标制已把这类错误挡掉大半）',
  },
  {
    name: '阿里百炼',
    baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1',
    model: 'qwen-plus',
    note: '百炼的 OpenAI 兼容模式',
  },
  {
    name: 'OpenAI',
    baseUrl: 'https://api.openai.com/v1',
    model: 'gpt-4o-mini',
    note: '注意 baseUrl 需要带 /v1',
  },
]

// 地址归一化：去掉尾部斜杠与误填的 /chat/completions，避免拼成 .../chat/completions/chat/completions
export function normalizeBaseUrl(raw: string): string {
  return raw
    .trim()
    .replace(/\/+$/, '')
    .replace(/\/chat\/completions$/i, '')
}
