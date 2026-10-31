using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LevelForge.Platform.Database;

namespace LevelForge.Platform.Llm;

// 一条聊天消息（role: system/user/assistant，OpenAI 兼容协议）
public sealed record ChatMessage(string Role, string Content);

// LLM 测试结论：可达性 + 延迟 + 失败原因（设置页"测试连接"直接消费）
public sealed record LlmTestResult(bool Ok, long LatencyMs, string? Error, string? ReplyPreview);

// 聊天调用结论：Ok=false 表示 API 层失败（网络/HTTP/超时/结构缺失），与内容层失败（判卷）分开计数
public sealed record ChatResult(
    bool Ok, string Content, string? Error, long LatencyMs, int? PromptTokens, int? CompletionTokens);

// LLM 连接三件套：从设置解析（实例自带，明文本地）
public sealed record LlmConfig(string BaseUrl, string ApiKey, string Model)
{
    // 可用判定：地址与模型必填，key 视服务端要求可空
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);

    // 从设置读取（缺省空串）
    public static LlmConfig Resolve(SettingsStore settings)
    {
        return new LlmConfig(
            settings.Get<string>("llm.baseUrl", "") ?? "",
            settings.Get<string>("llm.apiKey", "") ?? "",
            settings.Get<string>("llm.model", "") ?? "");
    }
}

// LLM API 客户端：OpenAI 兼容 /chat/completions 协议（生成端出题分析 §5：实例自带地址/key/模型）
// 密钥只在本地 settings 明文存取；调用失败一律转为结论返回，不向调用方抛异常（用户取消除外）
public sealed class LlmClient
{
    private const int CallTimeoutSeconds = 30;   // 单次调用超时（生成端出题分析 §5）

    // 请求体序列化：宽松编码——中文提示词不转义成 \uXXXX（流量可读、日志可断言）
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _http;

    public LlmClient(HttpClient http)
    {
        _http = http;
    }

    // 测试连接：发一条最小消息验证 地址/key/模型 三件套
    public async Task<LlmTestResult> TestConnectionAsync(string baseUrl, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
        {
            return new LlmTestResult(false, 0, "未配置 LLM 地址或模型", null);
        }
        var result = await ChatAsync(
            new LlmConfig(baseUrl, apiKey, model),
            [new ChatMessage("user", "ping：请只回复 pong")],
            temperature: 0.2, maxTokens: 8);
        return new LlmTestResult(result.Ok, result.LatencyMs, result.Error,
            result.Ok ? result.Content : null);
    }

    // 聊天补全：一次无状态单问单答（生成端形态铁律：无 agent，循环由流水线代码驱动）
    public async Task<ChatResult> ChatAsync(
        LlmConfig config, IReadOnlyList<ChatMessage> messages,
        double temperature = 0.8, int maxTokens = 2048, CancellationToken ct = default)
    {
        if (!config.IsConfigured)
        {
            return new ChatResult(false, "", "未配置 LLM 地址或模型", 0, null, null);
        }
        var sw = Stopwatch.StartNew();
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = config.Model,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }),
                temperature,
                max_tokens = maxTokens,
                stream = false
            }, BodyOptions);
            using var request = new HttpRequestMessage(
                HttpMethod.Post, config.BaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(config.ApiKey))
            {
                request.Headers.Authorization = new("Bearer", config.ApiKey);
            }
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cancel.CancelAfter(TimeSpan.FromSeconds(CallTimeoutSeconds));
            using var response = await _http.SendAsync(request, cancel.Token);
            var latency = sw.ElapsedMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                return new ChatResult(false, "", $"HTTP {(int)response.StatusCode}：{Trim(detail)}", latency, null, null);
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // 结构性缺失按 API 层失败处理（拿不到内容无法判卷）
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.GetString() is not { } text)
            {
                return new ChatResult(false, "", "响应缺少 choices 内容", latency, null, null);
            }
            int? promptTokens = root.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
            int? completionTokens = root.TryGetProperty("usage", out usage)
                && usage.TryGetProperty("completion_tokens", out var cpt) ? cpt.GetInt32() : null;
            return new ChatResult(true, text, null, latency, promptTokens, completionTokens);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested)
            {
                throw;   // 用户取消向上传播，由流水线标记运行取消
            }
            return new ChatResult(false, "", $"调用超时（{CallTimeoutSeconds}s）", sw.ElapsedMilliseconds, null, null);
        }
        catch (Exception ex)
        {
            return new ChatResult(false, "", ex.Message, sw.ElapsedMilliseconds, null, null);
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
