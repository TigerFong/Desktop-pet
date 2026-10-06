using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartDesktopPet
{
    /// <summary>
    /// 🌸 用 Playwright 操控真實瀏覽器（可持續保留同一個分頁，讓 LLM 連續操作）
    /// </summary>
    public class PlaywrightBrowserService : IAsyncDisposable
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // 🌸 所有頁面操作的逾時都統一設 30 秒
        private const int PageTimeoutMs = 30000;

        /// <summary>
        /// 🌸 確保 Chromium 已安裝（第一次啟動時會下載，之後就秒開）
        /// </summary>
        public static void EnsureBrowsersInstalled()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string playwrightPath = Path.Combine(localAppData, "ms-playwright");

                bool hasChromium = Directory.Exists(playwrightPath) &&
                                   Directory.EnumerateDirectories(playwrightPath, "chromium-*").Any();

                if (!hasChromium)
                {
                    Debug.WriteLine("[Playwright] 未偵測到 Chromium，開始下載…");
                    Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Playwright 安裝失敗]: {ex.Message}");
            }
        }

        public async Task EnsureInitializedAsync()
        {
            if (_page != null && !_page.IsClosed) return;

            await _initLock.WaitAsync();
            try
            {
                if (_page != null && !_page.IsClosed) return;

                _playwright ??= await Playwright.CreateAsync();
                _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                    Args = new[] { "--start-maximized" }
                });
                _context ??= await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = ViewportSize.NoViewport,
                    Locale = "zh-TW"
                });
                _page = await _context.NewPageAsync();
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>開啟指定網址</summary>
        public async Task NavigateAsync(string url)
        {
            await EnsureInitializedAsync();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = PageTimeoutMs
            });
        }

        /// <summary>
        /// 🌸 用「網站名稱」智慧打開網站：
        ///   1. 若看起來像網址 → 直接開
        ///   2. 否則用 Google「手氣不錯」直接跳到第一個結果
        ///   3. 若卡在 Google 重導向頁 → 自動解析並跳轉
        ///   4. 若失敗 → 退回普通搜尋頁
        /// </summary>
        public async Task<bool> OpenSiteByNameAsync(string siteName)
        {
            await EnsureInitializedAsync();

            string trimmed = siteName.Trim();
            if (string.IsNullOrEmpty(trimmed)) return false;

            // 🌸 情況 1：看起來像網址（有 . 且沒有空白）
            if (trimmed.Contains('.') && !trimmed.Contains(' ') && !trimmed.Contains('　'))
            {
                await NavigateAsync(trimmed);
                return true;
            }

            // 🌸 情況 2：用 Google「手氣不錯」(btnI=1) 直接跳到第一個搜尋結果
            string luckyUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(trimmed)}&btnI=1&hl=zh-TW";
            try
            {
                await _page!.GotoAsync(luckyUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = PageTimeoutMs
                });

                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                string finalUrl = _page.Url;

                // 🌸 情況 3：卡在 Google 的「重新導向通知」頁面
                //    網址通常長這樣：https://www.google.com/url?q=https://www.louhau.edu.mo/&...
                if (finalUrl.Contains("google.com/url?q=") || finalUrl.Contains("google.com.tw/url?q="))
                {
                    // 🌸 用正規表達式從網址中抓出 q= 後面的目標網址
                    var match = System.Text.RegularExpressions.Regex.Match(
                        finalUrl,
                        @"[?&]q=([^&]+)"
                    );

                    if (match.Success)
                    {
                        string targetUrl = Uri.UnescapeDataString(match.Groups[1].Value);
                        Debug.WriteLine($"[OpenSiteByName] 從 Google 重導向頁解析出目標網址: {targetUrl}");

                        // 🌸 直接跳轉到真正的目標網址
                        await _page.GotoAsync(targetUrl, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = PageTimeoutMs
                        });
                        return true;
                    }
                }

                // 🌸 情況 4：若還停在 Google 搜尋頁，代表「手氣不錯」沒跳成功
                bool stillOnSearchPage =
                    finalUrl.Contains("google.com/search") &&
                    !finalUrl.Contains("btnI=1");

                if (stillOnSearchPage)
                {
                    Debug.WriteLine($"[OpenSiteByName] 手氣不錯失敗，停留在搜尋頁: {finalUrl}");
                    return false;
                }

                Debug.WriteLine($"[OpenSiteByName] 成功跳轉到: {finalUrl}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenSiteByName 失敗]: {ex.Message}");
                return false;
            }
        }

        /// <summary>搜尋引擎查詢（google / bing / youtube）</summary>
        public async Task SearchAsync(string query, string engine = "google")
        {
            await EnsureInitializedAsync();
            string url = engine.ToLowerInvariant() switch
            {
                "bing" => $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}",
                "youtube" => $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}",
                _ => $"https://www.google.com/search?q={Uri.EscapeDataString(query)}"
            };
            await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = PageTimeoutMs
            });
        }

        /// <summary>開啟 YouTube 搜尋並自動點擊第一支影片開始播放</summary>
        public async Task PlayYoutubeAsync(string songName)
        {
            await EnsureInitializedAsync();

            string searchUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(songName)}";
            await _page!.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = PageTimeoutMs
            });

            try
            {
                await _page.WaitForSelectorAsync("ytd-video-renderer a#video-title",
                    new PageWaitForSelectorOptions { Timeout = 15000 });

                var firstVideo = _page.Locator("ytd-video-renderer a#video-title").First;
                await firstVideo.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
                Debug.WriteLine($"[YouTube] 已點擊第一支影片：{songName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YouTube 自動點擊失敗，停留在搜尋頁]: {ex.Message}");
            }
        }

        /// <summary>點擊指定 CSS 選擇器</summary>
        public async Task ClickAsync(string selector)
        {
            await EnsureInitializedAsync();
            await _page!.Locator(selector).First.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        }

        /// <summary>在指定欄位輸入文字，可選擇按下 Enter</summary>
        public async Task TypeAsync(string selector, string text, bool submit)
        {
            await EnsureInitializedAsync();
            var locator = _page!.Locator(selector).First;
            await locator.FillAsync(text);
            if (submit)
                await _page.Keyboard.PressAsync("Enter");
        }

        /// <summary>取得目前頁面標題</summary>
        public async Task<string> GetPageTitleAsync()
        {
            await EnsureInitializedAsync();
            return await _page!.TitleAsync();
        }

        /// <summary>取得目前頁面網址</summary>
        public async Task<string> GetPageUrlAsync()
        {
            await EnsureInitializedAsync();
            return _page!.Url;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_page != null && !_page.IsClosed) await _page.CloseAsync();
                if (_context != null) await _context.CloseAsync();
                if (_browser != null) await _browser.CloseAsync();
                _playwright?.Dispose();
            }
            catch { /* 關閉階段忽略錯誤 */ }
        }
    }
}