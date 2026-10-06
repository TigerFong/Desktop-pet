using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SmartDesktopPet
{
    public class ApiConfig
    {
        public string ApiKey { get; set; } = "";
        public string ApiUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
        public string TextModel { get; set; } = "glm-4.5-flash";
        public string VisionModel { get; set; } = "glm-4.6v-flash";
    }

    public class ApiConfigWrapper
    {
        public ApiConfig ZhipuApi { get; set; } = new ApiConfig();
    }

    public class GlmApiService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public ApiConfig Config { get; private set; } = new ApiConfig();

        public string ApiKey => Config.ApiKey;
        public string ApiUrl => Config.ApiUrl;
        public string TextModel => Config.TextModel;
        public string VisionModel => Config.VisionModel;

        public GlmApiService()
        {
            // 🌸 HttpClient 本身設一個寬鬆的天花板；
            //    實際每次呼叫的逾時由 CancellationTokenSource 動態控制
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
            LoadConfig();
        }

        public void LoadConfig()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    var wrapper = JsonSerializer.Deserialize<ApiConfigWrapper>(json);
                    if (wrapper?.ZhipuApi != null)
                    {
                        Config = wrapper.ZhipuApi;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[讀取 appsettings.json 失敗]: {ex.Message}");
                }
            }
            else
            {
                SaveDefaultConfig(configPath);
            }

            UpdateAuthHeader();
        }

        private void UpdateAuthHeader()
        {
            if (!string.IsNullOrEmpty(ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }
        }

        private void SaveDefaultConfig(string path)
        {
            try
            {
                var wrapper = new ApiConfigWrapper();
                string json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[建立預設 appsettings.json 失敗]: {ex.Message}");
            }
        }

        /// <summary>
        /// 🌸 發送純文字對話與工具呼叫（帶工具時給 60 秒逾時）
        /// </summary>
        public async Task<(string ResponseText, List<GlmToolCall>? ToolCalls)> SendMessageWithToolsAsync(
            string systemPrompt,
            List<GlmMessage> history,
            string userText,
            List<GlmTool>? tools = null)
        {
            var messages = new List<object>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }

            foreach (var msg in history)
            {
                if (msg.Content is string textContent)
                {
                    messages.Add(new { role = msg.Role, content = textContent });
                }
                else
                {
                    messages.Add(new { role = msg.Role, content = msg.Content?.ToString() ?? "" });
                }
            }

            messages.Add(new { role = "user", content = userText });

            var payload = new Dictionary<string, object>
            {
                { "model", TextModel },
                { "messages", messages },
                { "max_tokens", 400 }
            };

            bool hasTools = tools != null && tools.Count > 0;
            if (hasTools)
            {
                payload["tools"] = tools!;
                payload["tool_choice"] = "auto";
            }

            // 🌸 有工具時給 60 秒（模型要讀工具定義並思考）；
            //    沒工具時只要 20 秒純聊天就夠了
            int timeoutSeconds = hasTools ? 60 : 20;

            string responseJson = await PostPayloadAsync(payload, timeoutSeconds);
            return ParseGlmResponse(responseJson);
        }

        /// <summary>
        /// 🌸 發送截圖進行視覺分析（給 30 秒，因為圖片較大）
        /// </summary>
        public async Task<string> SendImageMessageAsync(string systemPrompt, string base64Image, string userText)
        {
            string cleanBase64 = base64Image;
            if (cleanBase64.Contains(","))
            {
                cleanBase64 = cleanBase64.Split(',')[1];
            }
            cleanBase64 = cleanBase64.Replace("\r", "").Replace("\n", "").Trim();

            string visionInstruction = "你是一個桌寵，請用古風傲嬌的口吻，精準描述這張螢幕截圖裡看到的軟體、畫面或內容，並進行吐槽。若畫面是程式碼或網頁，請直接說出你看到的關鍵字。";

            var messages = new List<object>
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = $"data:image/jpeg;base64,{cleanBase64}"
                            }
                        },
                        new
                        {
                            type = "text",
                            text = $"{visionInstruction}\n\n【使用者當下指示】: {userText}"
                        }
                    }
                }
            };

            var payload = new
            {
                model = VisionModel,
                messages = messages,
                max_tokens = 2048
            };

            string responseJson = await PostPayloadAsync(payload, timeoutSeconds: 30);
            var (responseText, _) = ParseGlmResponse(responseJson);
            return responseText;
        }

        /// <summary>
        /// 🌸 送出 Payload，逾時由 CancellationTokenSource 動態控制
        /// </summary>
        private async Task<string> PostPayloadAsync(object payload, int timeoutSeconds)
        {
            int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    string jsonPayload = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // 🌸 每次呼叫用獨立的 CancellationTokenSource 控制逾時
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                    HttpResponseMessage response = await _httpClient.PostAsync(ApiUrl, content, cts.Token);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if ((int)response.StatusCode == 429 || responseString.Contains("1305"))
                    {
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(1500 * (retry + 1));
                            continue;
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"API 請求失敗 ({response.StatusCode}): {responseString}");
                    }

                    return responseString;
                }
                catch (TaskCanceledException)
                {
                    // 🌸 超時直接拋出（不重試，避免使用者等更久）
                    throw new Exception($"請求逾時（超過 {timeoutSeconds} 秒），伺服器反應過慢。");
                }
                catch (Exception)
                {
                    if (retry == maxRetries - 1) throw;
                    await Task.Delay(1000);
                }
            }

            throw new Exception("伺服器繁忙，請稍後再試。");
        }

        private (string ResponseText, List<GlmToolCall>? ToolCalls) ParseGlmResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement))
                {
                    string errMsg = errorElement.TryGetProperty("message", out var msgEl)
                        ? msgEl.GetString() ?? "未知錯誤"
                        : "未知錯誤";
                    System.Diagnostics.Debug.WriteLine($"[GLM API 錯誤]: {errMsg}");
                    return ($"（本座的眼睛出錯了：{errMsg}）", null);
                }

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");

                    List<GlmToolCall>? toolCalls = null;
                    if (message.TryGetProperty("tool_calls", out var toolCallsElement))
                    {
                        toolCalls = JsonSerializer.Deserialize<List<GlmToolCall>>(toolCallsElement.GetRawText());
                    }

                    string text = "";
                    if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind != JsonValueKind.Null)
                    {
                        text = contentElement.GetString() ?? "";
                    }

                    return (text, toolCalls);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[解析 JSON 失敗]: {ex.Message}");
            }

            return ("（本座暫時無法回應…）", null);
        }
    }

    public class GlmMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public object Content { get; set; } = "";
    }

    public class GlmTool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public GlmFunction Function { get; set; } = new GlmFunction();
    }

    public class GlmFunction
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("parameters")]
        public object Parameters { get; set; } = new { };
    }

    public class GlmToolCall
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public GlmFunctionCall Function { get; set; } = new GlmFunctionCall();
    }

    public class GlmFunctionCall
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("arguments")]
        public string Arguments { get; set; } = "";
    }
}