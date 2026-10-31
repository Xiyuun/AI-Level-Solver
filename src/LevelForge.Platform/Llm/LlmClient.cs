using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LevelForge.Platform.Llm;

// LLM 测试结论：可达性 + 延迟 + 失败原因（设置页"测试连接"直接消费）
public sealed record LlmTestResult(bool Ok, long LatencyMs, string? Error, string? ReplyPreview);

// LLM API 客户端：OpenAI 兼容 /chat/completions 协议（生成端出题分析 §5：实例自带地址/key/模型）
// 密钥只在本地 settings 明文存取；调用失败一律转为结论返回，不向调用方抛异常
public sealed class LlmClient
{
    private readonly HttpClient _http;

    public LlmClient(HttpClient http)
    {
        _http = http;
    }

    // 测试连接：发一条最小消息验证 地址/key/模型 三件套；10 秒超时
    public async Task<LlmTestResult> TestConnectionAsync(string baseUrl, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            return new LlmTestResult(false, 0, "未配置 LLM 地址或模型", null);
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var url = baseUrl.TrimEnd('/') + "/chat/completions";
            var body = JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = "ping：请只回复 pong" } },
                max_tokens = 8,
                stream = false
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new("Bearer", apiKey);
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, cancel.Token);
            var latency = sw.ElapsedMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancel.Token);
                return new LlmTestResult(false, latency, $"HTTP {(int)response.StatusCode}：{Trim(detail)}", null);
            }
            var json = await response.Content.ReadAsStringAsync(cancel.Token);
            // 成功路径额外校验返回结构里真有回复内容
            using var doc = JsonDocument.Parse(json);
            string? preview = null;
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                preview = content.GetString();
            }
            return new LlmTestResult(true, latency, null, preview ?? "(响应无 choices 内容)");
        }
        catch (Exception ex)
        {
            return new LlmTestResult(false, sw.ElapsedMilliseconds, ex.Message, null);
        }
    }

    #region 内部工具

    // 错误详情截断：错误信息进 UI 与日志，过长截断
    private static string Trim(string text)
    {
        return text.Length <= 200 ? text : text[..200] + "…";
    }

    #endregion
}
