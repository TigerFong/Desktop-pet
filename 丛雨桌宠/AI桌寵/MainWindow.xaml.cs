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
        private readonly string _favorabilityFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favorability.txt");
        private readonly string _lastCheckInFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_checkin.txt");

        // 🌸 電腦狀態監控變數
        private DispatcherTimer _systemMonitorTimer = null!;
        private PerformanceCounter? _cpuCounter;
        private bool _isMemoryWarned = false;
        private bool _isCpuWarned = false;
        private bool _isBatteryWarned = false;
        private bool _isScaleMode = false;

        // 🌸 待辦事項變數
        private List<TodoItem> _todoList = new List<TodoItem>();
        private DispatcherTimer _todoTimer = null!;
        private readonly string _todoFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todos.json");

        public MainWindow()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _glmService = new GlmApiService();
                LoadFavorability();
                CheckDailyCheckIn();
                SetPetExpression(PetExpression.Normal);
                InitSpeechRecognizer();
                InitSystemMonitor();// 🌸 初始化效能監控
                InitTodoSystem(); // 🌸 初始化待辦事項系統
                LoadSystemPrompt();
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
        /// 🌸 擷取全螢幕並轉為 Base64 字串傳給 AI
        /// </summary>
        private string CaptureScreenAsBase64()
        {
            try
            {
                // 🌸 使用 SystemInformation / Screen 獲取最準確的螢幕實體解析度
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                int screenWidth = primaryScreen.Bounds.Width;
                int screenHeight = primaryScreen.Bounds.Height;

                using (Bitmap fullBitmap = new Bitmap(screenWidth, screenHeight))
                {
                    using (Graphics g = Graphics.FromImage(fullBitmap))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        fullBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        byte[] imageBytes = ms.ToArray();

                        string base64 = Convert.ToBase64String(imageBytes);
                        System.Diagnostics.Debug.WriteLine($"[截圖成功] Base64 長度: {base64.Length}");

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

        /// <summary>
        /// 🌸 定義叢雨可以使用的智能電腦工具
        /// </summary>
        private List<GlmTool> GetPetTools()
        {
            return new List<GlmTool>
            {
                new GlmTool
                {
                    Type = "function",
                    Function = new GlmFunction
                    {
                        Name = "open_url",
                        Description = "【極度嚴格條件】：僅在使用者明確要求開啟特定網址（如 youtube.com, google.com）或明確發出指令如'搜尋/查/找 [關鍵字]'時呼叫！絕對禁止將軟體名稱（如 Unity, 異環, Steam）當成網址！",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                url = new { type = "string", description = "完整網址或 Google 搜尋連結" }
                            },
                            required = new[] { "url" }
                        }
                    }
                },
                new GlmTool
                {
                    Type = "function",
                    Function = new GlmFunction
                    {
                        Name = "play_youtube_music",
                        Description = "【播放音樂專用】：當使用者發出指令如'幫我放歌'、'點歌'、'播放 [歌曲/歌手名稱]'、'聽 [歌名]' 時呼叫！",
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
                new GlmTool
                {
                    Type = "function",
                    Function = new GlmFunction
                    {
                        Name = "execute_system_command",
                        Description = "【開啟電腦軟體或檔案專用】：當使用者要開啟電腦程式、遊戲、檔案（例如：打開報告.docx、開啟計算機、打開 Unity Hub、打開異環）時呼叫！",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                command = new { type = "string", description = "要開啟的軟體名稱、遊戲或檔案名稱（例如：報告, unity, 異環, calc, notepad）" }
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
                        Description = "【觀看螢幕畫面專用】：當使用者發出指令如'看看我的螢幕'、'幫我看這個'、'這是在寫什麼'、'看我畫面' 時呼叫！",
                        Parameters = new
                        {
                            type = "object",
                            properties = new { }, // 不需要額外參數
                            required = new string[] { }
                        }
                    }
                },
                new GlmTool
                {
                    Type = "function",
                    Function = new GlmFunction
                    {
                        Name = "execute_system_command",
                        Description = "【開啟電腦軟體、遊戲或本機檔案專用】：當使用者要開啟電腦程式、遊戲、或任何檔案（例如：打開報告.docx、開計算機、打開 index.html、script.js、程式碼檔案）時必呼叫此工具！",
                        Parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                command = new { type = "string", description = "要開啟的軟體名稱、遊戲或檔案名稱（例如：index.html, script.js, 報告, calc）" }
                            },
                            required = new[] { "command" }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// 🌸 在背景偷偷抓取 YouTube 搜尋頁面的第一個影片 ID 並組合播放網址
        /// </summary>
        private async Task<string> GetFirstYoutubeVideoUrlAsync(string keyword)
        {
            try
            {
                using var client = new HttpClient();
                // 模擬真實瀏覽器 User-Agent 避免被擋
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7");

                string searchUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(keyword)}&sp=EgIQAQ%253D%253D";
                string html = await client.GetStringAsync(searchUrl);

                // 使用正規表達式搜尋 HTML 中的第一個影片 ID (/watch?v=xxxxxxxxxxx)
                var match = Regex.Match(html, @"/watch\?v=([a-zA-Z0-9_-]{11})");
                if (match.Success)
                {
                    string videoId = match.Groups[1].Value;
                    // 回傳帶有 autoplay=1 的影片直接播放頁面
                    return $"https://www.youtube.com/watch?v={videoId}&autoplay=1";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[抓取 YouTube 第一首失敗]: {ex.Message}");
            }

            // 若抓取失敗則自動降級為標準搜尋結果網址
            return $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(keyword)}";
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

                // 🌸 1. 處理專屬 YouTube 點歌指令 (免外掛自動播第一首)
                if (functionName == "play_youtube_music" && root.TryGetProperty("song_name", out var songElement))
                {
                    string songName = songElement.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(songName))
                    {
                        // 背景分析找出第一首歌曲的直接播放網址
                        string targetUrl = await GetFirstYoutubeVideoUrlAsync(songName);

                        bool success = OpenWithChrome(targetUrl);
                        if (!success)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = targetUrl,
                                UseShellExecute = true
                            });
                        }
                    }
                }
                // 🌸 2. 處理開啟一般網址 / 搜尋
                else if (functionName == "open_url" && root.TryGetProperty("url", out var targetUrlElement))
                {
                    string targetUrl = targetUrlElement.GetString() ?? "";
                    if (!string.IsNullOrEmpty(targetUrl))
                    {
                        bool success = OpenWithChrome(targetUrl);
                        if (!success)
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = targetUrl,
                                UseShellExecute = true
                            });
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
        /// 🌸 強制使用 Chrome 開啟指定網址
        /// </summary>
        private bool OpenWithChrome(string url)
        {
            string[] possiblePaths = new string[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };

            string chromePath = string.Empty;
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    chromePath = path;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(chromePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = chromePath,
                        Arguments = $"\"{url}\"",
                        UseShellExecute = true
                    });
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
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
            try
            {
                if (File.Exists(_favorabilityFilePath))
                {
                    string content = File.ReadAllText(_favorabilityFilePath);
                    if (int.TryParse(content, out int savedScore))
                    {
                        _favorability = savedScore;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[好感度讀取失敗] {ex.Message}");
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
                File.WriteAllText(_favorabilityFilePath, _favorability.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[好感度儲存失敗] {ex.Message}");
            }
        }

        private void AddFavorability(int amount)
        {
            _favorability += amount;
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
                _ => $"好感度已經達到 {_favorability} 點的頂峰啦！主人現在已經是本座最信任的人了呢……"
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

        private void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_speechRecognizer == null)
            {
                FormsMessageBox.Show("系統未偵測到相容的語音辨識裝置或麥克風。", "語音提示");
                return;
            }

            if (!_isListening)
            {
                try
                {
                    _speechRecognizer.RecognizeAsync(RecognizeMode.Multiple);
                    _isListening = true;
                    MicButton.Background = System.Windows.Media.Brushes.Red;
                    MicButton.ToolTip = "正在聆聽中... 點擊停止";
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
                StopListening();
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
                // 🌸 設定 CancellationTokenSource，若 15 秒內 AI 沒吐完字就強制中斷，避免永遠卡住
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));

                bool shouldProvideTools = IsCommandIntent(userText);
                List<GlmTool>? activeTools = shouldProvideTools ? GetPetTools() : null;

                var (responseText, toolCalls) = await _glmService.SendMessageWithToolsAsync(
                    _systemPrompt,
                    _chatHistory,
                    userText,
                    activeTools
                );

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
        /// 🌸 檢查使用者輸入是否包含「執行指令」的意圖關鍵字
        /// </summary>
        private bool IsCommandIntent(string text)
        {
            string lowerText = text.ToLower().Trim();

            string[] commonGreetings = { "你好", "您好", "哈囉", "hello", "hi", "在嗎", "你是誰", "早安", "午安", "晚安", "笨蛋" };
            foreach (var greeting in commonGreetings)
            {
                if (lowerText == greeting) return false;
            }

            // 🌸 把 .js, .html, 程式碼副檔名或常用動作也納入意圖判斷
            string[] commandKeywords = {
                "開", "開啟", "打開", "搜尋", "查", "找", "播放", "啟動", "點歌", "聽歌", "聽", "放", "run", "launch", ".html", ".js", ".css", ".cs"
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
    }
}