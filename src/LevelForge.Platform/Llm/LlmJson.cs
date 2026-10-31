namespace LevelForge.Platform.Llm;

// LLM 输出的 JSON 提取：模型可能带 markdown 围栏或前后解释文字，判卷前先取出 JSON 原文
// 这里只做"提取"不做修复——内容对不对由校验器裁决，禁止静默改写模型输出
public static class LlmJson
{
    // 从模型输出提取 JSON 对象原文；找不到返回 null
    public static string? ExtractJson(string llmOutput)
    {
        var text = llmOutput.Trim();
        // 去掉 ```json ... ``` 围栏（只剥一层外壳，不动内容）
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
                var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fenceEnd >= 0)
                {
                    text = text[..fenceEnd];
                }
            }
            text = text.Trim();
        }
        // 截取首个 { 到最后一个 }：模型前后加解释文字时的宽容提取
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }
        return text[start..(end + 1)];
    }
}
