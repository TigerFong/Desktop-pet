using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            // 🌸 設定 HttpClient 最多等待 12 秒，避免無限期卡住
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
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
        /// 🌸 發送純文字對話與工具呼叫
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

            // 🌸 整合 Payload 屬性（限制 max_tokens 為 250，大幅加快生成速度）
            var payload = new Dictionary<string, object>
            {
                { "model", TextModel },
                { "messages", messages },
                { "max_tokens", 400 }
            };

            if (tools != null && tools.Count > 0)
            {
                payload["tools"] = tools;
                payload["tool_choice"] = "auto";
            }

            string responseJson = await PostPayloadAsync(payload);
            return ParseGlmResponse(responseJson);
        }

        /// <summary>
        /// 🌸 發送截圖進行視覺分析
        /// </summary>
        public async Task<string> SendImageMessageAsync(string systemPrompt, string base64Image, string userText)
        {
            string cleanBase64 = base64Image;
            if (cleanBase64.Contains(","))
            {
                cleanBase64 = cleanBase64.Split(',')[1];
            }
            cleanBase64 = cleanBase64.Replace("\r", "").Replace("\n", "").Trim();

            var messages = new List<object>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }

            // 🌸 修正：確保多模態內容的格式完全符合 GLM 視覺模型規範
            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
            new
            {
                type = "text",
                text = string.IsNullOrEmpty(userText) ? "請幫我看看這張截圖並給予傲嬌的評論。" : userText
            },
            new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:image/jpeg;base64,{cleanBase64}"
                }
            }
                }
            });

            var payload = new
            {
                model = VisionModel,
                messages = messages,
                max_tokens = 512 // 稍微調大 token 讓模型有空間生成文字
            };

            string responseJson = await PostPayloadAsync(payload);
            var (responseText, _) = ParseGlmResponse(responseJson);

            return responseText;
        }

        private async Task<string> PostPayloadAsync(object payload)
        {
            int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    string jsonPayload = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.PostAsync(ApiUrl, content);
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
                        // 🌸 關鍵：把詳細的伺服器錯誤內容印到除錯視窗
                        System.Diagnostics.Debug.WriteLine($"[API 錯誤詳細內容]: Status: {response.StatusCode}, Body: {responseString}");
                        throw new HttpRequestException($"API 請求失敗 ({response.StatusCode}): {responseString}");
                    }

                    return responseString;
                }
                catch (TaskCanceledException)
                {
                    throw new Exception("請求逾時，伺服器反應過慢…哼，才、才不是本座故意不理你的！");
                }
                catch (Exception ex)
                {
                    // 🌸 確保能看到真實的例外訊息
                    System.Diagnostics.Debug.WriteLine($"[PostPayload 異常]: {ex.Message}");
                    if (retry == maxRetries - 1) throw;
                    await Task.Delay(1000);
                }
            }

            throw new Exception("伺服器繁忙，請稍後再試…真是的，別一直考驗本座的耐心！");
        }

        /// <summary>
        /// 🌸 自動搜尋桌面、我的文件、下載區與開始選單中的捷徑 (.lnk) 與常見檔案（支援遞迴子資料夾）
        /// </summary>
        private string FindFileOrShortcutPath(string targetName)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadsFolder = Path.Combine(userProfile, "Downloads");

            string[] searchFolders = new string[]
            {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),      // 個人桌面
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),// 公用桌面
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),           // 我的文件
        downloadsFolder,                                                             // 下載資料夾
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)              // 開始選單
            };

            // 🌸 支援的副檔名，把 .html 也加進去了
            string[] supportedExtensions = new string[] { "*.lnk", "*.docx", "*.xlsx", "*.pdf", "*.txt", "*.png", "*.jpg", "*.mp4", "*.zip", "*.html", "*.htm" };

            foreach (var folder in searchFolders)
            {
                if (!Directory.Exists(folder)) continue;

                foreach (var ext in supportedExtensions)
                {
                    try
                    {
                        // 🌸 改用 AllDirectories，這樣才會連同子資料夾一起翻找！
                        var files = Directory.GetFiles(folder, ext, SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

                            // 支援比對完整檔名含副檔名（例如使用者直接打 index.html）
                            string fullFileName = Path.GetFileName(file);

                            if (fileNameWithoutExt.ToLower().Contains(targetName.ToLower()) ||
                                fullFileName.ToLower().Contains(targetName.ToLower()))
                            {
                                return file;
                            }
                        }
                    }
                    catch
                    {
                        // 略過沒有權限存取的子資料夾錯誤
                    }
                }
            }

            return string.Empty;
        }

        private (string ResponseText, List<GlmToolCall>? ToolCalls) ParseGlmResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

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