using Microsoft.VisualBasic.Devices; // 🌸 用於輕鬆獲取記憶體資訊
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics; // 🌸 效能計數器命名空間
using System.IO;
using System.Net.Http; // 🌸 用於背景 HTTP 請求抓取 YouTube 網址
using System.Speech.Recognition; // 🌸 語音命名空間
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FormsMessageBox = System.Windows.Forms.MessageBox;
using FormsPoint = System.Drawing.Point;
using Keyboard = System.Windows.Input.Keyboard;
using WpfMessageBox = System.Windows.MessageBox;
// 🌸 解決 WPF 與 WinForms 命名空間衝突的別名設定
using WpfPoint = System.Windows.Point;
using System.Drawing; // 🌸 需要引用 System.Drawing 命名空間
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace SmartDesktopPet
{
    public partial class MainWindow : Window
    {
        private readonly GlmApiService _glmService = new GlmApiService();
        private readonly List<GlmMessage> _chatHistory = new List<GlmMessage>();
        private string _systemPrompt = string.Empty;

        private bool _isThinking = false;
        private bool _isTalking = false;

        private bool _isMathMode = false;

        private bool _isDragging = false;
        private WpfPoint _dragStartPoint; // 🌸 使用 WpfPoint 避免衝突
        private SpeechRecognitionEngine? _speechRecognizer;
        private bool _isListening = false;
        private int _favorability = 0;
        // 🌸 好感度防篡改密鑰（這串要記住，開發者工具要用一樣的）
        private const string FavorabilitySecret = "C0ngYu_2024_S3cr3t_K3y_!@#$%^&*";

        // 🌸 好感度存到 AppData，避免躺在遊戲目錄
        private readonly string _favorabilityFilePath = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory,
    "favorability.dat");
        private readonly string _lastCheckInFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_checkin.txt");
        private WhisperSpeechService? _whisperService;
        private bool _isRecording = false;

        // 🌸 電腦狀態監控變數
        private DispatcherTimer _systemMonitorTimer = null!;
        private PerformanceCounter? _cpuCounter;
        private bool _isMemoryWarned = false;
        private bool _isCpuWarned = false;
        private bool _isBatteryWarned = false;
        private bool _isScaleMode = false;
        // 🌸 Playwright 瀏覽器服務（LLM 專用）
        private readonly PlaywrightBrowserService _browserService = new();

        // 🌸 待辦事項變數
        private List<TodoItem> _todoList = new List<TodoItem>();
        private DispatcherTimer _todoTimer = null!;
        private readonly string _todoFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todos.json");

        public MainWindow()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                // 🌸 首次啟動會自動下載 Chromium（若已安裝就秒過）
                PlaywrightBrowserService.EnsureBrowsersInstalled();

                _glmService = new GlmApiService();
                LoadFavorability();
                CheckDailyCheckIn();
                SetPetExpression(PetExpression.Normal);
                InitSpeechRecognizer();
                InitSystemMonitor();
                InitTodoSystem();
                LoadSystemPrompt();
                // 🌸 初始化 Whisper 語音服務（模型路徑請根據實際位置調整）
                string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ggml-small.bin");
                if (File.Exists(modelPath))
                {
                    _whisperService = new WhisperSpeechService(modelPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Whisper] 未找到模型文件，語音功能停用");
                }
            }
        }

        // 🌸 待辦事項資料結構 (改用 string 儲存自訂格式時間)
        public class TodoItem
        {
            public string Task { get; set; } = "";
            public string ReminderTimeStr { get; set; } = ""; // 格式：yyyy/MM/dd/HH/mm
            public bool IsCompleted { get; set; } = false;
        }

        public enum PetExpression
        {
            Normal,
            Happy,
            Angry,
            Shy,
            Surprise,
            Dislike
        }

        /// <summary>
        /// 🌸 擷取全螢幕、縮放至最大 1024 像素以內，並確保 JPEG 檔案 &lt; 5MB 後轉 Base64
        /// </summary>
        private string CaptureScreenAsBase64()
        {
            try
            {
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                int screenWidth = primaryScreen.Bounds.Width;
                int screenHeight = primaryScreen.Bounds.Height;

                // 🌸 1. 擷取原始全螢幕
                using (Bitmap fullBitmap = new Bitmap(screenWidth, screenHeight))
                {
                    using (Graphics g = Graphics.FromImage(fullBitmap))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
                    }

                    // 🌸 2. 計算等比例縮放尺寸（長或寬最大不超過 1024 像素）
                    int targetWidth = screenWidth;
                    int targetHeight = screenHeight;
                    const int maxDimension = 1024;

                    if (screenWidth > maxDimension || screenHeight > maxDimension)
                    {
                        if (screenWidth > screenHeight)
                        {
                            targetWidth = maxDimension;
                            targetHeight = (int)((double)screenHeight / screenWidth * maxDimension);
                        }
                        else
                        {
                            targetHeight = maxDimension;
                            targetWidth = (int)((double)screenWidth / screenHeight * maxDimension);
                        }
                    }

                    // 🌸 3. 縮放到目標尺寸
                    using (Bitmap resizedBitmap = new Bitmap(targetWidth, targetHeight))
                    {
                        using (Graphics g = Graphics.FromImage(resizedBitmap))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(fullBitmap, 0, 0, targetWidth, targetHeight);
                        }

                        ImageCodecInfo? jpegEncoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
                        if (jpegEncoder == null) return string.Empty;

                        // 🌸 4. 從品質 85 開始，若超過 5MB 就逐步降 10，最低降到 20
                        const long maxBytes = 5L * 1024 * 1024;   // 5 MB
                        long quality = 85L;
                        byte[] imageBytes = Array.Empty<byte>();

                        while (true)
                        {
                            using (MemoryStream ms = new MemoryStream())
                            using (EncoderParameters ep = new EncoderParameters(1))
                            {
                                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                                resizedBitmap.Save(ms, jpegEncoder, ep);
                                imageBytes = ms.ToArray();
                            }

                            if (imageBytes.Length <= maxBytes || quality <= 20L)
                                break;

                            quality -= 10L;
                        }

                        string base64 = Convert.ToBase64String(imageBytes);
                        System.Diagnostics.Debug.WriteLine(
                            $"[截圖成功] 尺寸: {targetWidth}x{targetHeight}, " +
                            $"檔案大小: {imageBytes.Length / 1024.0:F1} KB, " +
                            $"品質: {quality}, Base64 長度: {base64.Length}"
                        );

                        return base64;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[截圖失敗]: {ex.Message}");
                return string.Empty;
            }
        }
        private ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        /// <summary>
        /// 🌸 定義叢雨可以使用的智能電腦工具
        /// </summary>
        private List<GlmTool> GetPetTools()
        {
            return new List<GlmTool>
    {
        // 🌸 工具 1：用「名稱」打開網站（最常用，不知道網址也能用）
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "browser_open_site",
                Description =
                    "【打開網站專用】當使用者說「打開 XX 網站」、「去 XX 官網」、「進入 XX」、" +
                    "「幫我開 XX 的網頁」時呼叫。傳入網站名稱即可，不需要完整網址。" +
                    "例如：'打開勞校中學'、'去 YouTube'、'開 Google'、'進入巴哈姆特'。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        site_name = new { type = "string", description = "網站名稱，例如「勞校中學」「YouTube」「巴哈姆特」" }
                    },
                    required = new[] { "site_name" }
                }
            }
        },
        // 🌸 工具 2：已知完整網址時直接打開（少用）
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "browser_open_url",
                Description = "【開啟完整網址專用】僅當使用者直接說出完整網址（含 .com、.org、http 等）時呼叫。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        url = new { type = "string", description = "完整網址，例如 https://www.google.com" }
                    },
                    required = new[] { "url" }
                }
            }
        },
        // 🌸 工具 3：搜尋引擎（收緊觸發條件）
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "browser_search",
                Description =
                    "【搜尋引擎專用】僅當使用者明確說「搜尋」、「查」、「找資料」、「Google 一下」時才呼叫。" +
                    "⚠️ 若使用者說的是「打開 XX 網站」、「去 XX」，請改用 browser_open_site，不要用這個工具！",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "搜尋關鍵字" },
                        engine = new { type = "string", description = "google / bing / youtube，預設 google" }
                    },
                    required = new[] { "query" }
                }
            }
        },
        // 🌸 工具 4：YouTube 點歌
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "browser_play_youtube",
                Description = "【播放音樂專用】當使用者說「幫我放歌」、「點歌」、「播放 [歌曲/歌手]」、「聽 [歌名]」時呼叫。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        song_name = new { type = "string", description = "歌曲名稱或歌手關鍵字" }
                    },
                    required = new[] { "song_name" }
                }
            }
        },
        // 🌸 工具 5：通用網頁操作
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "browser_action",
                Description = "【操作目前瀏覽器分頁】當使用者要求在目前頁面點擊按鈕、填寫表單、或問「現在在哪一頁」時呼叫。",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", description = "click / type / get_title / get_url" },
                        selector = new { type = "string", description = "CSS 選擇器（click / type 時需要）" },
                        text = new { type = "string", description = "要輸入的文字（type 時需要）" },
                        submit = new { type = "boolean", description = "輸入後是否按 Enter（type 時可選）" }
                    },
                    required = new[] { "action" }
                }
            }
        },
        // 🌸 原本保留的工具
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "execute_system_command",
                Description = "【開啟電腦軟體或檔案專用】當使用者要開啟電腦程式、遊戲、檔案時呼叫！",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        command = new { type = "string", description = "要開啟的軟體名稱、遊戲或檔案名稱" }
                    },
                    required = new[] { "command" }
                }
            }
        },
        new GlmTool
        {
            Type = "function",
            Function = new GlmFunction
            {
                Name = "capture_screen_and_see",
                Description = "【觀看螢幕畫面專用】當使用者發出「看看我的螢幕」、「幫我看這個」時呼叫！",
                Parameters = new
                {
                    type = "object",
                    properties = new { },
                    required = new string[] { }
                }
            }
        }
    };
        }

        /// <summary>
        /// 🌸 執行 AI 決定的工具指令 (改為 async)
        /// </summary>
        private async Task ExecuteToolCallAsync(string functionName, string argumentsJson)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
                var root = doc.RootElement;

                // 🌸 1. 用「名稱」智慧打開網站（主要入口）
                if (functionName == "browser_open_site" && root.TryGetProperty("site_name", out var siteEl))
                {
                    string siteName = siteEl.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(siteName))
                    {
                        ShowBubbleMessage($"（叢雨幫你打開「{siteName}」…）");

                        // 🌸 給 Playwright 45 秒，超過就放棄並提示
                        var openTask = _browserService.OpenSiteByNameAsync(siteName);
                        var completed = await Task.WhenAny(openTask, Task.Delay(TimeSpan.FromSeconds(45)));

                        if (completed == openTask)
                        {
                            bool ok = await openTask;
                            if (ok)
                                ShowBubbleMessage($"（「{siteName}」已經打開囉，主人請看！）");
                            else
                                ShowBubbleMessage($"（唔…叢雨找不到「{siteName}」的官網，幫你搜尋了一下，請自己點進去吧。）");
                        }
                        else
                        {
                            ShowBubbleMessage($"（瀏覽器那邊好像卡住了，主人稍等一下再看看網頁吧。）");
                        }
                    }
                }
                // 🌸 2. 直接開完整網址
                else if (functionName == "browser_open_url" && root.TryGetProperty("url", out var urlElement))
                {
                    string url = urlElement.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        await _browserService.NavigateAsync(url);
                        ShowBubbleMessage($"（叢雨已幫你開啟：{url}）");
                    }
                }
                // 🌸 2. 搜尋引擎查詢
                else if (functionName == "browser_search" && root.TryGetProperty("query", out var queryElement))
                {
                    string query = queryElement.GetString()?.Trim() ?? "";
                    string engine = root.TryGetProperty("engine", out var engineElement)
                        ? engineElement.GetString() ?? "google"
                        : "google";

                    if (!string.IsNullOrEmpty(query))
                    {
                        await _browserService.SearchAsync(query, engine);
                        ShowBubbleMessage($"（叢雨在 {engine} 上幫你搜尋「{query}」囉！）");
                    }
                }
                // 🌸 3. YouTube 點歌
                else if (functionName == "browser_play_youtube" && root.TryGetProperty("song_name", out var songElement))
                {
                    string songName = songElement.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(songName))
                    {
                        ShowBubbleMessage($"（叢雨正在 YouTube 找「{songName}」…）");
                        await _browserService.PlayYoutubeAsync(songName);
                        ShowBubbleMessage($"（找到了！開始播放「{songName}」囉，主人好好享受吧～）");
                    }
                }
                // 🌸 4. 通用網頁操作
                else if (functionName == "browser_action" && root.TryGetProperty("action", out var actionElement))
                {
                    string action = actionElement.GetString()?.ToLowerInvariant() ?? "";
                    string selector = root.TryGetProperty("selector", out var selEl) ? selEl.GetString() ?? "" : "";
                    string text = root.TryGetProperty("text", out var txtEl) ? txtEl.GetString() ?? "" : "";
                    bool submit = root.TryGetProperty("submit", out var subEl) && subEl.GetBoolean();

                    switch (action)
                    {
                        case "click":
                            if (!string.IsNullOrEmpty(selector))
                            {
                                await _browserService.ClickAsync(selector);
                                ShowBubbleMessage($"（叢雨點擊了 {selector}）");
                            }
                            break;

                        case "type":
                            if (!string.IsNullOrEmpty(selector))
                            {
                                await _browserService.TypeAsync(selector, text, submit);
                                ShowBubbleMessage($"（叢雨在 {selector} 輸入了「{text}」{(submit ? " 並送出！" : "")}）");
                            }
                            break;

                        case "get_title":
                            {
                                string title = await _browserService.GetPageTitleAsync();
                                ShowBubbleMessage($"（目前頁面標題：{title}）");
                                break;
                            }

                        case "get_url":
                            {
                                string url = await _browserService.GetPageUrlAsync();
                                ShowBubbleMessage($"（目前頁面網址：{url}）");
                                break;
                            }
                    }
                }
                // 🌸 3. 處理開啟系統軟體 / 遊戲 / 檔案
                else if (functionName == "execute_system_command" && root.TryGetProperty("command", out var cmdElement))
                {
                    string cmd = cmdElement.GetString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(cmd)) return;

                    if (TryStartProcess(cmd)) return;

                    string filePath = FindFileOrShortcutPath(cmd);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        TryStartProcess(filePath);
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"[開啟失敗]: 找不到名為 '{cmd}' 的應用程式或檔案");
                }
                else if (functionName == "capture_screen_and_see")
                {
                    string base64Image = CaptureScreenAsBase64();

                    if (!string.IsNullOrEmpty(base64Image))
                    {
                        ShowBubbleMessage("（叢雨正在認真看你的螢幕…）");

                        string visionPrompt = "這是使用者目前的螢幕畫面，請精準描述你看到的軟體或內容並進行吐槽。";

                        // 🌸 1. 發送圖片並取得視覺分析結果
                        string visionResponse = await _glmService.SendImageMessageAsync(_systemPrompt, base64Image, visionPrompt);

                        // 🌸 2. 清理情緒標籤並過濾格式
                        string cleanText = Regex.Replace(visionResponse, @"\[EMOTION:\w+\]", "").Trim();
                        if (string.IsNullOrWhiteSpace(cleanText))
                        {
                            cleanText = "（叢雨盯著螢幕看了半天，什麼都沒看出…）";
                        }

                        // 🌸 3. 將視覺分析結果顯示在氣泡對話框中！
                        BubbleText.Text = FormatMathText(cleanText);
                        BubbleScrollViewer.ScrollToEnd();

                        // 🌸 4. 更新叢雨表情與紀錄歷史
                        UpdateExpressionFromText(visionResponse);
                        _chatHistory.Add(new GlmMessage { Role = "assistant", Content = visionResponse });
                    }
                    else
                    {
                        ShowBubbleMessage("（叢雨嘗試看螢幕，但擷取畫面失敗了…）");
                    }
                }
            }
            catch (Exception ex)
            {
                // 🌸 針對超時與 1305 伺服器繁忙進行美化處理
                if (ex.Message.Contains("1305") || ex.Message.Contains("TooManyRequests") || ex.Message.Contains("访问量过大"))
                {
                    SetPetExpression(PetExpression.Surprise);
                    ShowBubbleMessage("（現在找叢雨的人太多了，本座的大腦稍微卡住了一下，請等幾秒再試試吧！）");
                }
                else if (ex.Message.Contains("逾時") || ex.Message.Contains("Timeout") || ex.Message.Contains("TaskCanceledException"))
                {
                    SetPetExpression(PetExpression.Surprise);
                    ShowBubbleMessage("（叢雨剛才想得太入神發呆了…再跟本座說一次吧！）");
                }
                else
                {
                    SetPetExpression(PetExpression.Dislike);
                    ShowBubbleMessage($"（本座出錯了：{ex.Message}）");
                }
            }
            finally
            {
                _isThinking = false;
                SendButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 🌸 啟動進程的輔助方法
        /// </summary>
        private bool TryStartProcess(string targetPath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 🌸 自動搜尋桌面、我的文件、下載區與開始選單中的捷徑 (.lnk) 與常見檔案
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

            string[] supportedExtensions = new string[] { "*.lnk", "*.docx", "*.xlsx", "*.pdf", "*.txt", "*.png", "*.jpg", "*.mp4", "*.zip" };

            foreach (var folder in searchFolders)
            {
                if (!Directory.Exists(folder)) continue;

                foreach (var ext in supportedExtensions)
                {
                    try
                    {
                        var files = Directory.GetFiles(folder, ext, SearchOption.TopDirectoryOnly);
                        foreach (var file in files)
                        {
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

                            if (fileNameWithoutExt.ToLower().Contains(targetName.ToLower()))
                            {
                                return file;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return string.Empty;
        }

        private void LoadSystemPrompt()
        {
            try
            {
                string promptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SystemPrompt.txt");

                if (File.Exists(promptPath))
                {
                    _systemPrompt = File.ReadAllText(promptPath, Encoding.UTF8);
                }
                else
                {
                    _systemPrompt = "你現在是桌寵「丛雨」，請以古風傲嬌口吻回答主人。每次回答最開頭請附帶 [EMOTION:情緒] 標籤。";
                    WpfMessageBox.Show("未找到 SystemPrompt.txt，已載入預設提示詞。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"讀取 SystemPrompt.txt 失敗: {ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuToggleScaleMode_Click(object sender, RoutedEventArgs e)
        {
            _isScaleMode = MenuToggleScaleMode.IsChecked;

            if (_isScaleMode)
            {
                SetPetExpression(PetExpression.Happy);
                ShowBubbleMessage("【縮放模式已開啟】現在按住 Shift + 滾輪 即可在視窗任意位置調整我的大小囉！");
            }
            else
            {
                SetPetExpression(PetExpression.Normal);
                ShowBubbleMessage("【縮放模式已關閉】");
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (_isScaleMode && isShiftPressed)
            {
                double currentScale = UiScaleTransform.ScaleX;

                if (e.Delta > 0)
                    currentScale += 0.05;
                else
                    currentScale -= 0.05;

                currentScale = Math.Max(0.7, Math.Min(1.15, currentScale));

                UiScaleTransform.ScaleX = currentScale;
                UiScaleTransform.ScaleY = currentScale;

                e.Handled = true;
            }
        }

        private void InitTodoSystem()
        {
            LoadTodos();

            _todoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _todoTimer.Tick += CheckTodoList;
            _todoTimer.Start();
        }

        private void LoadTodos()
        {
            try
            {
                if (File.Exists(_todoFilePath))
                {
                    string json = File.ReadAllText(_todoFilePath);
                    var items = System.Text.Json.JsonSerializer.Deserialize<List<TodoItem>>(json);
                    if (items != null) _todoList = items;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[讀取待辦失敗] {ex.Message}");
            }
        }

        private void MenuScale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag != null)
            {
                if (double.TryParse(menuItem.Tag.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double scale))
                {
                    UiScaleTransform.ScaleX = scale;
                    UiScaleTransform.ScaleY = scale;
                }
            }
        }

        private void SaveTodos()
        {
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(_todoList);
                File.WriteAllText(_todoFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[儲存待辦失敗] {ex.Message}");
            }
        }

        private void CheckTodoList(object? sender, EventArgs e)
        {
            if (_isThinking || SpeechBubble.Visibility == Visibility.Visible) return;

            DateTime now = DateTime.Now;
            bool needSave = false;

            for (int i = _todoList.Count - 1; i >= 0; i--)
            {
                var todo = _todoList[i];
                if (DateTime.TryParseExact(todo.ReminderTimeStr, "yyyy/MM/dd/HH/mm",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime targetTime))
                {
                    if (now >= targetTime)
                    {
                        SetPetExpression(PetExpression.Surprise);
                        ShowBubbleMessage($"叮咚！時間到了！主人你交代的事情：「{todo.Task}」已經到期囉，趕快去處理！");

                        _todoList.RemoveAt(i);
                        needSave = true;
                        break;
                    }
                }
            }

            if (needSave)
            {
                SaveTodos();
            }
        }

        private void MenuTodo_Click(object sender, RoutedEventArgs e)
        {
            var todoWindow = new TodoWindow();
            if (todoWindow.ShowDialog() == true)
            {
                string taskName = todoWindow.TaskName;
                string timeStr = todoWindow.ReminderTimeStr;

                _todoList.Add(new TodoItem { Task = taskName, ReminderTimeStr = timeStr });
                SaveTodos();

                SetPetExpression(PetExpression.Happy);
                ShowBubbleMessage($"哼哼，記下了！主人你交代的事情：「{taskName}」會在 {timeStr} 準時提醒你，這次可別想賴帳喔！");
            }
        }

        private void LoadFavorability()
        {
            System.Diagnostics.Debug.WriteLine($"[好感度路徑] {_favorabilityFilePath}");
            try
            {
                // 🌸 舊檔遷移：若 AppData 沒有檔案，但遊戲目錄有舊的 favorability.txt
                string oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favorability.txt");
                if (!File.Exists(_favorabilityFilePath) && File.Exists(oldPath))
                {
                    try
                    {
                        string oldContent = File.ReadAllText(oldPath).Trim();
                        if (int.TryParse(oldContent, out int oldValue))
                        {
                            _favorability = oldValue;
                            SaveFavorability();
                            File.Delete(oldPath);
                            System.Diagnostics.Debug.WriteLine($"[好感度遷移] 從舊檔讀取 {oldValue} 並轉存到 AppData");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[好感度遷移失敗] {ex.Message}");
                    }
                }

                // 🌸 檔案不存在 → 預設 0
                if (!File.Exists(_favorabilityFilePath))
                {
                    _favorability = 0;
                    return;
                }

                string content = File.ReadAllText(_favorabilityFilePath, Encoding.UTF8).Trim();

                // 🌸 格式必須是：數值|簽章
                var parts = content.Split('|');
                if (parts.Length != 2)
                {
                    _favorability = 0;
                    SaveFavorability();
                    return;
                }

                string valueStr = parts[0];
                string savedSign = parts[1];
                string expectedSign = ComputeHmac(valueStr);

                // 🌸 簽章對不上 → 判定為篡改 → 歸零
                if (savedSign != expectedSign)
                {
                    _favorability = 0;

                    SetPetExpression(PetExpression.Angry);
                    ShowBubbleMessage("哼！你竟敢偷改本座的好感度？既然如此，一切歸零，從頭來過吧！");

                    SaveFavorability();
                    return;
                }

                // 🌸 簽章正確 → 讀取數值
                if (int.TryParse(valueStr, out int savedScore))
                {
                    _favorability = savedScore;
                }
                else
                {
                    _favorability = 0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[好感度讀取失敗] {ex.Message}");
                _favorability = 0;
            }
        }

        private void CheckDailyCheckIn()
        {
            try
            {
                string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
                string lastCheckIn = "";

                if (File.Exists(_lastCheckInFilePath))
                {
                    lastCheckIn = File.ReadAllText(_lastCheckInFilePath).Trim();
                }

                if (lastCheckIn != todayStr)
                {
                    File.WriteAllText(_lastCheckInFilePath, todayStr);
                    AddFavorability(5);

                    SetPetExpression(PetExpression.Happy);
                    ShowBubbleMessage($"啊，主人今天也有來看本座呢！這是今天的每日簽到獎勵，好感度 +5 點！");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[每日簽到失敗] {ex.Message}");
            }
        }

        private void SaveFavorability()
        {
            try
            {
                string? dir = Path.GetDirectoryName(_favorabilityFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string value = _favorability.ToString();
                string sign = ComputeHmac(value);

                File.WriteAllText(_favorabilityFilePath, $"{value}|{sign}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[好感度儲存失敗] {ex.Message}");
            }
        }

        /// <summary>
        /// 🌸 用密鑰計算 HMAC-SHA256 簽章
        /// </summary>
        private static string ComputeHmac(string value)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(FavorabilitySecret));
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToBase64String(hash);
        }

        private void AddFavorability(int amount)
        {
            _favorability += amount;

            // 🌸 設定好感度上限為 999
            if (_favorability > 999)
            {
                _favorability = 999;
            }

            SaveFavorability();
            CheckFavorabilityUnlock();
        }

        private void CheckFavorabilityUnlock()
        {
            if (_favorability == 10)
            {
                SetPetExpression(PetExpression.Normal);
                ShowBubbleMessage("哼，好感度累積到 10 點了。勉強算你有些毅力，繼續努力吧！");
            }
            else if (_favorability == 30)
            {
                SetPetExpression(PetExpression.Happy);
                ShowBubbleMessage("好感度達到 30 點了呢！跟本座相處得還算愉快吧？嘻嘻～");
            }
            else if (_favorability == 50)
            {
                SetPetExpression(PetExpression.Shy);
                ShowBubbleMessage("哼…看在主人最近這麼勤勞陪本座的份上，勉強允許你叫本座的名字好了！");
            }
            else if (_favorability == 80)
            {
                SetPetExpression(PetExpression.Surprise);
                ShowBubbleMessage("欸？不知不覺好感度已經到 80 點了……才、才不是特地等你來找本座呢！");
            }
            else if (_favorability == 100)
            {
                SetPetExpression(PetExpression.Happy);
                ShowBubbleMessage("好啦好啦！本座承認已經有點習慣有主人在旁邊碎唸了…這、這才不是害羞！");
            }
            else if (_favorability == 150)
            {
                SetPetExpression(PetExpression.Shy);
                ShowBubbleMessage("哼哼，150 點解鎖！主人現在對本座來說，已經是無可取代的重要存在了唷……");
            }
        }

        private void InitSystemMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[效能計數器初始化失敗] {ex.Message}");
            }

            _systemMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _systemMonitorTimer.Tick += CheckSystemStatus;
            _systemMonitorTimer.Start();
        }

        private void CheckSystemStatus(object? sender, EventArgs e)
        {
            if (_isThinking || SpeechBubble.Visibility == Visibility.Visible) return;

            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                float batteryLifePercent = powerStatus.BatteryLifePercent;
                var batteryChargeStatus = powerStatus.BatteryChargeStatus;

                if (!batteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging) &&
                    batteryLifePercent <= 0.2f && batteryLifePercent > 0)
                {
                    if (!_isBatteryWarned)
                    {
                        _isBatteryWarned = true;
                        int percentInt = (int)(batteryLifePercent * 100);
                        SetPetExpression(PetExpression.Surprise);
                        ShowBubbleMessage($"欸！主人的電腦快沒電啦（剩 {percentInt}%），趕快去接電源，聽到沒有！");
                    }
                    return;
                }
                else if (batteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging) || batteryLifePercent > 0.25f)
                {
                    _isBatteryWarned = false;
                }

                var computerInfo = new ComputerInfo();
                ulong totalMemory = computerInfo.TotalPhysicalMemory;
                ulong freeMemory = computerInfo.AvailablePhysicalMemory;
                double usedMemoryPercent = (double)(totalMemory - freeMemory) / totalMemory * 100;

                if (usedMemoryPercent > 85.0)
                {
                    if (!_isMemoryWarned)
                    {
                        _isMemoryWarned = true;
                        SetPetExpression(PetExpression.Surprise);
                        ShowBubbleMessage($"喂！主人的記憶體都被吃掉 {(int)usedMemoryPercent}% 了！到底是開了多少分頁或是奇怪的程式，快去清理一下啦！");
                    }
                    return;
                }
                else if (usedMemoryPercent < 75.0)
                {
                    _isMemoryWarned = false;
                }

                if (_cpuCounter != null)
                {
                    float cpuUsage = _cpuCounter.NextValue();
                    if (cpuUsage > 85.0f)
                    {
                        if (!_isCpuWarned)
                        {
                            _isCpuWarned = true;
                            SetPetExpression(PetExpression.Angry);
                            ShowBubbleMessage($"哼！CPU 燒成這樣（{(int)cpuUsage}%），主人到底在背後偷偷執行什麼大工程？小心電腦過熱壞掉喔！");
                        }
                        return;
                    }
                    else if (cpuUsage < 70.0f)
                    {
                        _isCpuWarned = false;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void MenuShowFavorability_Click(object sender, RoutedEventArgs e)
        {
            string message = _favorability switch
            {
                < 10 => $"目前好感度是 {_favorability} 點。哼，還早得很呢，快點多陪本座聊聊天！",
                < 30 => $"目前好感度是 {_favorability} 點。勉強算你合格啦，繼續保持，聽到沒有？",
                < 50 => $"目前好感度是 {_favorability} 點。看在你這麼努力的份上，本座就稍微稱讚你一下吧。",
                < 80 => $"目前好感度是 {_favorability} 點。不知不覺已經這麼熟了啊……咳咳，沒事！",
                < 150 => $"哇…好感度已經累積到 {_favorability} 點了。本座的意思是……有你陪著挺好的！",
                < 999 => $"好感度已經達到 {_favorability} 點了！主人現在已經是本座最信任的人了呢……",
                _ => $"好感度已經達到 {_favorability} 點的極限了！笨蛋主人，本座已經完全離不開你了啦……！"
            };

            SetPetExpression(PetExpression.Shy);
            ShowBubbleMessage(message);
        }

        private void InitSpeechRecognizer()
        {
            try
            {
                _speechRecognizer = new SpeechRecognitionEngine();
                _speechRecognizer.LoadGrammar(new DictationGrammar());

                _speechRecognizer.SpeechRecognized += (s, e) =>
                {
                    if (e.Result != null && !string.IsNullOrEmpty(e.Result.Text))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            InputTextBox.Text += e.Result.Text;
                            InputTextBox.CaretIndex = InputTextBox.Text.Length;
                        });
                    }
                };

                _speechRecognizer.SetInputToDefaultAudioDevice();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[語音初始化失敗] {ex.Message}");
            }
        }

        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_whisperService == null)
            {
                FormsMessageBox.Show("語音模型未載入，請確認 ggml-small.bin 是否存在。", "語音提示");
                return;
            }

            if (!_isRecording)
            {
                // 🌸 開始錄音
                try
                {
                    _whisperService.StartRecording();
                    _isRecording = true;
                    MicButton.Background = System.Windows.Media.Brushes.Red;
                    MicButton.ToolTip = "正在錄音... 點擊停止";
                    BubbleText.Text = "（請開始說話，本座在聽呢...）";
                    SpeechBubble.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    FormsMessageBox.Show($"無法啟動麥克風: {ex.Message}", "錯誤");
                }
            }
            else
            {
                // 🌸 停止錄音並辨識
                _isRecording = false;
                MicButton.IsEnabled = false; // 辨識期間禁用按鈕
                MicButton.Background = new System.Windows.Media.BrushConverter().ConvertFrom("#CC4CAF50") as System.Windows.Media.Brush;
                MicButton.ToolTip = "點擊開始語音輸入";
                BubbleText.Text = "（本座正在理解你的話語…）";

                try
                {
                    string recognizedText = await _whisperService.StopRecordingAndTranscribeAsync();

                    if (!string.IsNullOrWhiteSpace(recognizedText))
                    {
                        InputTextBox.Text = recognizedText;
                        InputTextBox.CaretIndex = InputTextBox.Text.Length;

                        // 🌸 可選：自動發送
                        // await SendMessageAsync();
                    }
                    else
                    {
                        BubbleText.Text = "（本座沒聽清楚，你再說一次吧。）";
                    }
                }
                catch (Exception ex)
                {
                    BubbleText.Text = $"（辨識出錯了：{ex.Message}）";
                }
                finally
                {
                    MicButton.IsEnabled = true;
                    _isRecording = false;
                }
            }
        }

        private void StopListening()
        {
            if (_speechRecognizer != null && _isListening)
            {
                _speechRecognizer.RecognizeAsyncStop();
                _isListening = false;
                MicButton.Background = new System.Windows.Media.BrushConverter().ConvertFrom("#CC4CAF50") as System.Windows.Media.Brush;
                MicButton.ToolTip = "點擊開始語音輸入";
                SpeechBubble.Visibility = Visibility.Collapsed;
            }
        }

        private void SetPetExpression(PetExpression expression)
        {
            string fileName = expression switch
            {
                PetExpression.Happy => "happy.png",
                PetExpression.Angry => "angry.png",
                PetExpression.Shy => "shy.png",
                PetExpression.Surprise => "surprise.png",
                PetExpression.Dislike => "dislike.png",
                _ => "normal.png"
            };

            SetPetImage(fileName);
        }

        private void SetPetImage(string fileName)
        {
            try
            {
                string cleanName = Path.GetFileName(fileName);
                string packUri = $"pack://application:,,,/Images/{_currentCostume}/{cleanName}";

                var bitmap = new BitmapImage(new Uri(packUri, UriKind.Absolute));
                PetImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[換裝失敗] 套裝: {_currentCostume}, 檔名: {fileName}, 錯誤: {ex.Message}");

                if (_currentCostume != "Shendao")
                {
                    _currentCostume = "Shendao";
                    SetPetImage("normal.png");
                }
            }
        }

        private void UpdateExpressionFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var match = Regex.Match(text, @"\[EMOTION:(Happy|Angry|Shy|Surprise|Dislike|Normal)\]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string emotionStr = match.Groups[1].Value;
                if (Enum.TryParse<PetExpression>(emotionStr, true, out var parsedExpression))
                {
                    SetPetExpression(parsedExpression);
                    return;
                }
            }

            if (text.Contains("笨蛋") || text.Contains("討厭") || text.Contains("少來"))
                SetPetExpression(PetExpression.Angry);
            else if (text.Contains("謝謝") || text.Contains("太好了") || text.Contains("嘻嘻") || text.Contains("真厲害"))
                SetPetExpression(PetExpression.Happy);
            else if (text.Contains("…") || text.Contains("呀") || text.Contains("臉紅") || text.Contains("人家"))
                SetPetExpression(PetExpression.Shy);
            else if (text.Contains("欸") || text.Contains("真的嗎") || text.Contains("什麼") || text.Contains("？！"))
                SetPetExpression(PetExpression.Surprise);
            else if (text.Contains("略") || text.Contains("才不要") || text.Contains("走開"))
                SetPetExpression(PetExpression.Dislike);
        }

        private string _currentCostume = "Shendao";

        private void MenuCostume_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string costumeName)
            {
                _currentCostume = costumeName;
                SetPetExpression(PetExpression.Normal);

                string costumeDisplayName = menuItem.Header.ToString() ?? "新衣服";
                ShowBubbleMessage($"成功換上{costumeDisplayName}囉！適合本座嗎？");
            }
        }

        private void PetImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (_isThinking) return;
                SwitchToInputMode();
            }
            else
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                PetImage.CaptureMouse();
            }
        }

        private void RootGrid_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (_isScaleMode || isShiftPressed)
            {
                double currentScale = UiScaleTransform.ScaleX;

                if (e.Delta > 0)
                    currentScale += 0.05;
                else
                    currentScale -= 0.05;

                currentScale = Math.Max(0.3, Math.Min(2.5, currentScale));

                UiScaleTransform.ScaleX = currentScale;
                UiScaleTransform.ScaleY = currentScale;

                e.Handled = true;
            }
        }

        private void BubbleScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (_isScaleMode || isShiftPressed)
            {
                return;
            }

            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void InputPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            if (_isScaleMode || isShiftPressed)
            {
                return;
            }
        }

        private void PetImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                WpfPoint currentPoint = e.GetPosition(this);
                double offsetX = currentPoint.X - _dragStartPoint.X;
                this.Left += offsetX;
            }
        }

        private void PetImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                PetImage.ReleaseMouseCapture();
            }
        }

        private void ShowBubbleMessage(string text)
        {
            SpeechBubble.Visibility = Visibility.Visible;
            InputPanel.Visibility = Visibility.Collapsed;
            BubbleText.Text = text;
        }

        private void SwitchToInputMode()
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            SetPetExpression(PetExpression.Normal);
            InputPanel.Visibility = Visibility.Visible;
            InputTextBox.Clear();
            InputTextBox.Focus();
        }

        private void CloseBubbleButton_Click(object sender, RoutedEventArgs e)
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            SetPetExpression(PetExpression.Normal);
        }

        private void MenuClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void InputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = SendMessageAsync();
                e.Handled = true;
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async Task SendMessageAsync()
        {
            string userText = InputTextBox.Text.Trim();
            if (string.IsNullOrEmpty(userText) || _isThinking) return;

            _isThinking = true;
            SendButton.IsEnabled = false;
            InputPanel.Visibility = Visibility.Collapsed;

            BubbleText.Text = "（叢雨思考中…）";
            SpeechBubble.Visibility = Visibility.Visible;

            try
            {
                bool shouldProvideTools = IsCommandIntent(userText);
                List<GlmTool>? activeTools = shouldProvideTools ? GetPetTools() : null;

                var (responseText, toolCalls) = await _glmService.SendMessageWithToolsAsync(
                    _systemPrompt,
                    _chatHistory,
                    userText,
                    activeTools
                );

                AddFavorability(2);

                if (toolCalls != null && toolCalls.Count > 0)
                {
                    foreach (var call in toolCalls)
                    {
                        if (call.Function != null && !string.IsNullOrEmpty(call.Function.Name))
                        {
                            await ExecuteToolCallAsync(call.Function.Name, call.Function.Arguments);
                            _chatHistory.Add(new GlmMessage { Role = "user", Content = userText });
                            _chatHistory.Add(new GlmMessage { Role = "assistant", Content = $"[已執行指令: {call.Function.Name}]" });
                            return;
                        }
                    }
                }

                string cleanText = Regex.Replace(responseText, @"\[EMOTION:\w+\]", "").Trim();
                if (string.IsNullOrWhiteSpace(cleanText))
                {
                    cleanText = "（叢雨正在想事情…）";
                }

                BubbleText.Text = FormatMathText(cleanText);
                BubbleScrollViewer.ScrollToEnd();

                _chatHistory.Add(new GlmMessage { Role = "user", Content = userText });
                _chatHistory.Add(new GlmMessage { Role = "assistant", Content = responseText });

                UpdateExpressionFromText(responseText);
            }
            catch (OperationCanceledException)
            {
                SetPetExpression(PetExpression.Surprise);
                ShowBubbleMessage("（回應時間過長，叢雨剛才發呆卡住了…請再試一次吧！）");
            }
            catch (Exception ex)
            {
                SetPetExpression(PetExpression.Dislike);
                ShowBubbleMessage($"（執行錯誤: {ex.Message}）");
            }
            finally
            {
                // 🌸 無論成功或失敗，一定要確保重置思考狀態與按鈕啟用
                _isThinking = false;
                SendButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 🌸 檢查使用者輸入是否包含「執行指令」或「看螢幕」的意圖關鍵字
        /// </summary>
        private bool IsCommandIntent(string text)
        {
            string lowerText = text.ToLower().Trim();

            string[] commonGreetings = { "你好", "您好", "哈囉", "hello", "hi", "在嗎", "你是誰", "早安", "午安", "晚安", "笨蛋" };
            foreach (var greeting in commonGreetings)
            {
                if (lowerText == greeting) return false;
            }

            // 🌸 1. 看螢幕 / 識圖意圖（放最前面優先判斷，避免被其他關鍵字誤導）
            string[] visionKeywords = {
        "看螢幕", "看畫面", "看屏幕", "看下螢幕", "看下畫面",
        "螢幕畫面", "螢幕上", "畫面中", "屏幕上", "螢幕內容", "畫面內容",
        "幫我看", "幫我瞧", "瞧瞧", "看一下", "看看我", "看我螢幕", "看我畫面",
        "識圖", "認圖", "這是什麼", "這是啥", "這在寫什麼", "這在幹嘛",
        "看這個", "看下這個", "看我這個", "幫我看看", "看一下螢幕"
    };
            foreach (var keyword in visionKeywords)
            {
                if (lowerText.Contains(keyword)) return true;
            }

            // 🌸 2. 一般指令關鍵字（開程式、搜尋、放歌…）
            string[] commandKeywords = {
                "開", "開啟", "打開", "搜尋", "查", "找", "播放", "啟動", "點歌", "聽歌", "聽", "放",
                "官網", "網站", "網頁", "頁面", "進入", "去",
                "run", "launch", ".html", ".js", ".css", ".cs"
            };
            foreach (var keyword in commandKeywords)
            {
                if (lowerText.Contains(keyword)) return true;
            }

            return false;
        }

        private string FormatMathText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            string formatted = text;

            formatted = Regex.Replace(formatted, @"\\frac\{([^}]+)\}\{([^}]+)\}", "($1 / $2)");

            formatted = Regex.Replace(formatted, @"\^\{?([0-9\+\-\=]+)\}?", m =>
            {
                string digits = m.Groups[1].Value;
                string superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼";
                string normal = "0123456789+-=";
                char[] result = new char[digits.Length];
                for (int i = 0; i < digits.Length; i++)
                {
                    int idx = normal.IndexOf(digits[i]);
                    result[i] = idx >= 0 ? superscripts[idx] : digits[i];
                }
                return new string(result);
            });

            formatted = Regex.Replace(formatted, @"_\{?([0-9\+\-\=]+)\}?", m =>
            {
                string digits = m.Groups[1].Value;
                string subscripts = "₀₁₂₃₄⁵⁶₇₈₉₊₋₌";
                string normal = "0123456789+-=";
                char[] result = new char[digits.Length];
                for (int i = 0; i < digits.Length; i++)
                {
                    int idx = normal.IndexOf(digits[i]);
                    result[i] = idx >= 0 ? subscripts[idx] : digits[i];
                }
                return new string(result);
            });

            formatted = formatted
                .Replace("###", "").Replace("##", "").Replace("**", "")
                .Replace("\\[", "").Replace("\\]", "")
                .Replace("\\(", "").Replace("\\)", "")
                .Replace("$", "");

            var symbolMap = new Dictionary<string, string>
            {
                { "\\times", "×" }, { "\\div", "÷" }, { "\\pm", "±" },
                { "\\le", "≤" }, { "\\ge", "≥" }, { "\\neq", "≠" },
                { "\\approx", "≈" }, { "\\infty", "∞" }, { "\\sqrt", "√" },
                { "\\alpha", "α" }, { "\\beta", "β" }, { "\\gamma", "γ" },
                { "\\theta", "θ" }, { "\\pi", "π" }, { "\\Delta", "Δ" },
                { "\\cdot", "·" }, { "\\sum", "∑</" }, { "\\int", "∫" }
            };

            foreach (var pair in symbolMap)
            {
                formatted = formatted.Replace(pair.Key, pair.Value);
            }

            return formatted;
        }
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _browserService.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
                _whisperService?.Dispose(); // 🌸 釋放 Whisper
            }
            catch { }

            base.OnClosed(e);
        }
    }
}