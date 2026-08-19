# EasyLlama — Llama Vulkan 啟動器

以視覺化介面設定並啟動 [llama.cpp](https://github.com/ggml-org/llama.cpp) 的 `llama-server`，
專為 Windows + Vulkan 後端使用情境設計。不必再手動拼湊冗長的命令列參數，
所有設定都能存成設定檔重複使用。

[![Release](https://img.shields.io/github/v/release/den13501/EasyLlama)](https://github.com/den13501/EasyLlama/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

- 版本：**v1.2.0**
- 平台：Windows（.NET Framework 4.8.1）
- 授權：MIT

## 特色

### 完整的參數視覺化
把 `llama-server` 常用參數整理成五個分頁：

| 分頁 | 內容 |
| --- | --- |
| 路徑與檔案 | `llama-server.exe`、模型 GGUF、mmproj 視覺模型、對話模板 |
| 推論與裝置 | Vulkan 裝置、context 長度、GPU 層數、KV cache 型別、Flash Attention、ubatch、mmap |
| 本機最佳化 | 依實際 CPU/RAM/VRAM 給出建議值，一鍵套用 |
| 推測解碼 MTP | `--spec-type` 全系列（draft-mtp、ngram-cache 等）、draft 數量與門檻 |
| 伺服器與進階 | host/port、執行緒、行程優先權、額外自訂參數 |

### 隨執行檔更新自動調整
程式會解析 `llama-server --help` 的實際輸出，只送出這顆執行檔真正支援的旗標。
llama.cpp 改版（例如 `--no-mmap` 被 `--load-mode` 取代）時不會組出無效命令。

### Vulkan 偵測不到顯卡的自動修復
部分系統的 `HKLM\SOFTWARE\Khronos\Vulkan\Drivers` 註冊表遺失，
會導致明明有獨顯卻整個模型退回 CPU 執行。本程式會自動從顯示卡驅動註冊表找出 ICD 檔，
透過 `VK_ICD_FILENAMES` 傳給子行程，**不需修改系統設定，也不必重裝驅動**。

### 其他
- **設定檔管理**：多組設定命名保存、切換、另存、刪除
- **指令預覽**：即時顯示組好的命令列，可複製或匯出成 `.bat`
- **執行紀錄**：即時串接 `llama-server` 輸出，並偵測外部啟動的同名行程
- **綠色可攜**：設定存在執行檔旁的 `config\`，整個資料夾可直接搬移或放隨身碟
- **免重編譯客製**：介面文字與配色都可用外部 XML 覆寫
- **快捷鍵**：`F5` 啟動、`Shift+F5` 停止、`Ctrl+S` 儲存

## 系統需求

- Windows 10 / 11 64 位元
- [.NET Framework 4.8.1 執行階段](https://dotnet.microsoft.com/download/dotnet-framework/net481)
- 支援 Vulkan 的顯示卡與驅動
- llama.cpp 的 Vulkan 版 `llama-server.exe`（本專案**不含**，請自行下載）

## 快速開始

1. 於 [Releases](https://github.com/den13501/EasyLlama/releases) 下載 `LlamaVulkanLauncher-v1.2.0-win-x64.zip` 並解壓縮
2. 執行 `LlamaVulkanLauncher.exe`
3. 在「路徑與檔案」指定你的 `llama-server.exe` 與模型 GGUF
4. 切到「本機最佳化」按下建議按鈕套用適合本機的參數
5. 按「啟動」，狀態轉為運行中後即可點「開啟 Web UI」

## 自行編譯

需要 [.NET SDK 8.0 或更新版本](https://dotnet.microsoft.com/download/dotnet)（開發時使用 SDK 10.0.400）：

```powershell
git clone https://github.com/den13501/EasyLlama.git
cd EasyLlama
dotnet build -c Release
```

產物位於 `bin\Release\LlamaVulkanLauncher.exe`。

> 專案已引用 `Microsoft.NETFramework.ReferenceAssemblies.net481`，
> 因此**不需要另外安裝 .NET Framework 4.8.1 Developer Pack**，
> 只要有 .NET SDK 與可連線 nuget.org 的環境即可建置。

## 客製化

設定都放在執行檔旁的 `config\` 資料夾，改完重開程式即生效。

### 換介面語言
內建繁體中文。要翻成其他語言：

1. 複製 `config\lang\zh-TW.sample.xml` 成 `config\lang\en-US.xml`
2. 只修改每列的 `value`，`key` 保持不變
3. 在 `config\language.txt` 寫入 `en-US`

未指定語言時會嘗試比對系統語言。語言檔只覆蓋有寫到的項目，翻譯不完整也能正常使用。

### 換配色
複製 `config\theme.sample.xml` 成 `config\theme.xml` 後修改色值（`#RRGGBB` 或 `#AARRGGBB`）。

## 檔案說明

| 檔案 | 職責 |
| --- | --- |
| `MainForm.cs` | 主視窗與所有介面邏輯 |
| `CommandBuilder.cs` | 依設定組出 `llama-server` 命令列 |
| `ServerProcess.cs` | 子行程生命週期與輸出串接 |
| `ServerCapabilities.cs` | 解析 `--help` 判斷可用旗標 |
| `HardwareTuner.cs` | 依本機硬體推算建議參數 |
| `SystemResources.cs` | CPU / RAM / VRAM 偵測 |
| `VulkanFix.cs` | Vulkan ICD 自動修復 |
| `LaunchProfile.cs` / `ProfileStore.cs` | 設定檔資料結構與存取 |
| `Strings.cs` / `DefaultTexts.cs` | 多語系文字 |
| `Theme.cs` | 配色管理 |

## 注意事項

- 本專案僅為 `llama-server` 的啟動介面，不包含推論引擎與任何模型檔
- `config\settings.xml` 含個人本機路徑，分享設定前請自行檢查
- 與 llama.cpp 官方專案無隸屬關係

## 問題回報與貢獻

歡迎於 [Issues](https://github.com/den13501/EasyLlama/issues) 回報問題或提出建議，
也歡迎送出 Pull Request（特別是其他語言的翻譯檔）。

## 授權

MIT License，詳見 [LICENSE](LICENSE)。
