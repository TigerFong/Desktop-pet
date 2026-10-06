# 叢雨桌寵 (SmartDesktopPet)

一個基於 WPF 的 AI 桌面寵物，整合智譜 GLM 大模型，能夠與你對話、執行系統指令、操作瀏覽器、進行語音輸入，並擁有豐富的互動功能。

## 📖 專案簡介

「叢雨」是一位古風傲嬌的桌面寵物，她不只是陪你聊天，還能幫你：

- 打開網站、搜尋資料、播放 YouTube 音樂
- 開啟電腦中的軟體、遊戲或檔案
- 截取螢幕畫面並進行視覺吐槽
- 透過語音輸入與你互動
- 記錄待辦事項並準時提醒
- 好感度系統，與你培養感情（且防止篡改）

本專案使用 .NET 10 (Windows) + WPF 開發，結合 Playwright 實現瀏覽器自動化，並使用 Whisper.net 進行高準確度的離線語音辨識。

## ✨ 功能特性

- **AI 對話**：基於智譜 GLM 模型，支援工具呼叫（Function Calling）
- **瀏覽器自動化**：使用 Playwright 操作真實 Chromium，可開啟網站、搜尋、播放 YouTube、點擊與輸入
- **系統指令**：開啟本機軟體、遊戲、檔案（自動搜尋桌面、文件、下載區等）
- **螢幕視覺分析**：截取全螢幕並發送給視覺模型進行描述與吐槽
- **語音輸入**：整合 Whisper.net 進行離線中文語音辨識（需下載模型）
- **好感度系統**：
  - 每日簽到、對話增加好感度
  - 好感度里程碑解鎖特殊對話
  - **HMAC-SHA256 簽章防篡改**，手動修改檔案將導致好感度歸零
- **待辦事項**：設定提醒時間，時間到會彈出通知
- **服裝切換**：多套服裝（神道、制服、浴衣、便服）
- **表情系統**：根據對話內容自動切換表情（開心、生氣、害羞、驚訝、嫌棄）
- **系統監控**：監控 CPU、記憶體、電池電量，過高時會提醒你
- **縮放模式**：按住 Shift + 滾輪即可調整寵物大小
- **拖曳移動**：按住寵物即可拖動位置

## 🛠️ 技術棧

| 類別 | 技術 |
|------|------|
| 框架 | .NET 10 (Windows), WPF |
| AI 模型 | 智譜 GLM（文字與視覺模型） |
| 瀏覽器自動化 | Microsoft.Playwright |
| 語音辨識 | Whisper.net + NAudio |
| 語音合成 | System.Speech（舊版，可選） |
| 影像處理 | System.Drawing.Common |
| GIF 播放 | WpfAnimatedGif |
| 加密 | HMAC-SHA256（System.Security.Cryptography） |

## 📋 環境需求

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（或將專案目標框架改為 `net8.0-windows`）
- Visual Studio 2022（建議）
- 網路連線（用於呼叫 GLM API）
- 麥克風（若需語音輸入）

## 🚀 快速開始

### 1. 取得 API Key

前往 [智譜 AI 開放平台](https://open.bigmodel.cn/) 註冊並生成 API Key。

### 2. 配置 `appsettings.json`

在專案根目錄（或輸出目錄）建立 `appsettings.json`：

```json
{
  "ZhipuApi": {
    "ApiKey": "你的_API_Key",
    "ApiUrl": "https://open.bigmodel.cn/api/paas/v4/chat/completions",
    "TextModel": "glm-4.7-flash",
    "VisionModel": "glm-5.3-flash"
  }
}
```

> **模型更換說明**：只需修改 `TextModel` 與 `VisionModel` 欄位，不需更動任何程式碼。
>
> - 文字模型推薦：`glm-4.7-flash`（輕量免費）、`glm-5.3`（旗艦最強）
> - 視覺模型推薦：`glm-5.3-flash`（原生多模態）、`glm-5.3-flashx`（速度更快）

### 3. 下載 Whisper 模型（可選）

若要使用語音輸入，請下載 `ggml-small.bin` 模型：

- [下載連結](https://huggingface.co/sandrohanea/whisper.net/resolve/main/classic/ggml-small.bin)
- 將檔案放在輸出目錄（與 `.exe` 同層）

### 4. 構建與執行

```bash
git clone <你的倉庫網址>
cd SmartDesktopPet
dotnet restore
dotnet build
dotnet run
```

首次啟動時，Playwright 會自動下載 Chromium（約 150MB），請耐心等待。

## ⚙️ 配置說明

### `appsettings.json`

| 欄位 | 說明 |
|------|------|
| `ApiKey` | 智譜 API 金鑰 |
| `ApiUrl` | API 端點，預設為 `https://open.bigmodel.cn/api/paas/v4/chat/completions` |
| `TextModel` | 文字對話模型，預設 `glm-4.5-flash` |
| `VisionModel` | 視覺模型，預設 `glm-4.6v-flash` |

### `SystemPrompt.txt`

自訂叢雨的系統提示詞（角色設定、語氣規則等）。若檔案不存在，會載入預設提示詞。

## 🎮 使用說明

| 操作 | 功能 |
|------|------|
| 雙擊寵物 | 開啟輸入框 |
| 右鍵點擊寵物 | 開啟選單（換裝、縮放、好感度、待辦事項、關閉） |
| 拖曳寵物 | 移動視窗 |
| Shift + 滾輪 | 調整寵物大小（需先在選單開啟縮放模式） |
| 點擊麥克風按鈕 | 開始/停止語音錄音（需 Whisper 模型） |
| 輸入文字後按 Enter | 發送訊息 |

### 支援的語音/文字指令範例

- 「打開柚子社官網」
- 「幫我播放周杰倫的歌」
- 「看看我的螢幕」
- 「打開記事本」
- 「搜尋天氣」

## 🔐 好感度防篡改機制

好感度存檔位於程式目錄下的 `favorability.dat`，內容格式為：

```
<數值>|<HMAC-SHA256簽章>
```

例如：`999|wLXUFxvUrpcyVp0MqVbu6mClz6bVB2B0b/+Vfu4qDyY=`

- **若手動修改數值**，簽章將無法匹配，程式啟動時會自動歸零並顯示警告。
- **開發者**可使用獨立的 `PetDevTool.exe` 合法修改好感度（自動計算正確簽章）。

### 開發者工具：PetDevTool

專案中包含一個獨立的 WinForms 工具 `PetDevTool`，用於合法修改好感度。

**使用方式**：

1. 將 `PetDevTool.exe` 複製到與桌寵 `.exe` 相同的資料夾。
2. 關閉桌寵。
3. 執行 `PetDevTool.exe`，輸入新的好感度數值（0~999）。
4. 重新啟動桌寵。

> 注意：`PetDevTool` 中的密鑰 `FavorabilitySecret` 必須與主程式完全一致。
> 路徑 `FavorabilityFilePath` 也必須與主程式的 `_favorabilityFilePath` 一致（均使用 `AppDomain.CurrentDomain.BaseDirectory`）。

## 📁 專案結構

```
SmartDesktopPet/
├── AI桌寵.csproj                # 主專案檔
├── App.xaml / App.xaml.cs       # 應用程式入口
├── MainWindow.xaml              # 主視窗 UI
├── MainWindow.xaml.cs           # 主邏輯（對話、好感度、系統監控等）
├── GlmApiService.cs             # GLM API 封裝
├── PlaywrightBrowserService.cs  # Playwright 瀏覽器操作
├── WhisperSpeechService.cs      # Whisper 語音辨識
├── TodoWindow.xaml(.cs)         # 待辦事項視窗
├── appsettings.json             # API 配置
├── SystemPrompt.txt             # 系統提示詞
├── Images/                      # 寵物圖片資源
│   ├── Shendao/                 # 神道服裝
│   ├── Uniform/                 # 制服
│   ├── Yukata/                  # 浴衣
│   └── Casual/                  # 便服
└── PetDevTool/                  # 開發者工具（獨立專案）
    ├── PetDevTool.csproj
    └── Program.cs
```

## ⚠️ 注意事項

1. **API Key 請勿外流**，避免被他人盜用。
2. **首次啟動 Playwright 會下載 Chromium**，需保持網路暢通。
3. **Whisper 模型檔案較大**（約 460MB），請確認硬碟空間。
4. **修改好感度請使用 `PetDevTool`**，手動編輯檔案會觸發防篡改機制。
5. 本專案僅供學習與個人使用，請遵守智譜 AI 的服務條款。

## 📄 授權條款

本專案採用 MIT 授權條款，詳見 [LICENSE](LICENSE) 檔案。

## 🙏 致謝

- [智譜 AI](https://open.bigmodel.cn/) 提供強大的 GLM 模型
- [Microsoft Playwright](https://playwright.dev/dotnet/) 實現瀏覽器自動化
- [Whisper.net](https://github.com/sandrohanea/whisper.net) 提供離線語音辨識
- [NAudio](https://github.com/naudio/NAudio) 音訊處理
- [WpfAnimatedGif](https://github.com/XamlAnimatedGif/WpfAnimatedGif) GIF 播放

---

**享受與叢雨的每一天吧！** 🌸
