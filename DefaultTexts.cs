using System.Collections.Generic;

namespace LlamaVulkanLauncher
{
    /// <summary>
    /// 內建的繁體中文文字表，同時作為其他語言的翻譯範本來源。
    /// 加入新字串時請一併在這裡登錄，才能被匯出的語言範本收錄。
    /// </summary>
    internal static class DefaultTexts
    {
        public static void Fill(Dictionary<string, string> map)
        {
            FillGeneral(map);
            FillTabs(map);
            FillHints(map);
            FillMessages(map);
        }

        private static void FillGeneral(Dictionary<string, string> map)
        {
            map["App.Title"] = "EasyLlama - Llama Vulkan 啟動器";
            map["App.Subtitle"] = "視覺化設定 llama-server 路徑、模型與啟動參數";
            map["App.Status.Stopped"] = "已停止";
            map["App.Status.Running"] = "運行中";
            map["App.Status.Loading"] = "載入中…";
            map["App.Status.External"] = "外部服務執行中  PID {0}";

            map["Button.Start"] = "啟動";
            map["Button.Stop"] = "停止";
            map["Button.WebUi"] = "開啟 Web UI";
            map["Button.Save"] = "儲存";
            map["Button.SaveAs"] = "另存";
            map["Button.Delete"] = "刪除";
            map["Button.Optimize"] = "依本機最佳";
            map["Button.Browse"] = "瀏覽";
            map["Button.Detect"] = "偵測裝置";
            map["Button.Refresh"] = "重新偵測";
            map["Button.CopyCommand"] = "複製指令";
            map["Button.ExportBat"] = "匯出 BAT";
            map["Button.Clear"] = "清除";
            map["Button.Office"] = "辦公並行（建議）";
            map["Button.Performance"] = "效能優先";
            map["Button.ApplyOffice"] = "套用辦公建議";
            map["Button.Later"] = "稍後自己調";

            map["Label.Profile"] = "設定檔";
            map["Label.Shortcuts"] = "F5 啟動    Shift+F5 停止    Ctrl+S 儲存";
            map["Label.CommandPreview"] = "指令預覽";
            map["Label.Log"] = "執行紀錄";
        }

        private static void FillTabs(Dictionary<string, string> map)
        {
            map["Tab.Paths"] = "路徑與檔案";
            map["Tab.Inference"] = "推論與裝置";
            map["Tab.Optimize"] = "本機最佳化";
            map["Tab.Spec"] = "推測解碼 MTP";
            map["Tab.Server"] = "伺服器與進階";

            map["Field.LlamaServer"] = "llama-server.exe";
            map["Field.Model"] = "模型 GGUF";
            map["Field.Mmproj"] = "視覺投影";
            map["Field.MmprojFile"] = "mmproj 檔案";
            map["Field.ChatTemplate"] = "聊天模板";
            map["Field.ChatTemplateFile"] = "模板檔案";
            map["Field.ReasoningPreserve"] = "思考保留";
            map["Field.Device"] = "裝置 --device";
            map["Field.ContextSize"] = "上下文 --ctx-size";
            map["Field.GpuLayers"] = "GPU 層數 --n-gpu-layers";
            map["Field.KvCache"] = "KV cache";
            map["Field.FlashAttn"] = "Flash Attention -fa";
            map["Field.Reasoning"] = "思考 --reasoning";
            map["Field.Ubatch"] = "ubatch -ub";
            map["Field.LoadMode"] = "載入方式";
            map["Field.ImageMinTokens"] = "影像最小 token";
            map["Field.CacheRam"] = "提示快取 --cache-ram";
            map["Field.Parallel"] = "並行 slot --parallel";
            map["Field.SpecEnable"] = "開關";
            map["Field.SpecType"] = "--spec-type";
            map["Field.SpecDraftNMax"] = "--spec-draft-n-max";
            map["Field.SpecDraftPMin"] = "--spec-draft-p-min";
            map["Field.DraftKv"] = "Draft KV --spec-draft-type-k/v";
            map["Field.HardwareInfo"] = "本機偵測";
            map["Field.FirstAdvice"] = "初次建議";
            map["Field.QuickApply"] = "快速套用";
            map["Field.Threads"] = "CPU 執行緒 --threads";
            map["Field.ThreadsBatch"] = "批次執行緒 --threads-batch";
            map["Field.Priority"] = "行程優先權 --prio";
            map["Field.Host"] = "監聽位址 --host";
            map["Field.Port"] = "連接埠 --port";
            map["Field.Console"] = "主控台";
            map["Field.ExtraArgs"] = "額外參數";

            map["Check.UseMmproj"] = "啟用視覺 mmproj";
            map["Check.UseChatTemplate"] = "外掛聊天模板 --chat-template-file";
            map["Check.ReasoningPreserve"] = "保留思考內容 --reasoning-preserve";
            map["Check.NoMmap"] = "停用 mmap（新版自動改用 --load-mode none）";
            map["Check.EnableSpec"] = "啟用推測解碼（建議 draft-mtp）";
            map["Check.ShowConsole"] = "另外開啟主控台視窗（不擷取輸出到下方紀錄）";
        }

        private static void FillHints(Dictionary<string, string> map)
        {
            map["Hint.ContextSize"] = "日常與 Agent 甜蜜點 65536；盲開 131072 會拖慢首字並多吃顯存";
            map["Hint.GpuLayers"] = "99 = 全部丟給 GPU；顯存不足時才調降";
            map["Hint.KvCache"] = "24GB 顯卡建議 q8_0；顯存吃緊再降 q5_0 / q4_0";
            map["Hint.FlashAttn"] = "建議 on；KV 用量化型別時必須開啟";
            map["Hint.Reasoning"] = "Uncensored 版建議 off 省 token；官方版 + 修正模板用 auto";
            map["Hint.Ubatch"] = "256 為建議值；顯存吃緊可降 128";
            map["Hint.ImageMinTokens"] = "Qwen-VL 看圖建議 1024";
            map["Hint.CacheRam"] = "單位 MiB。官方預設只有 8192，長對話會被清掉導致每輪重算；辦公建議 16384，火力全開 32768（-1 不限制）";
            map["Hint.Parallel"] = "單人使用填 1，可獨享完整上下文；多人共用才調高";
            map["Hint.ChatTemplate"] = "⚠ 只有官方版模型需要（froggeric 修正模板）。JonathanColetti 版內建模板，勾了會吐出模板原始碼且看圖 400";
            map["Hint.ReasoningPreserve"] = "搭配官方版 + 修正模板使用，可讓多輪對話的 prefix cache 100% 命中";
            map["Hint.SpecType"] = "Qwen3.8 支援 MTP，選 draft-mtp 即可";
            map["Hint.SpecDraftNMax"] = "實測：日常對話 1 最快、寫程式 2~3，超過 3 反而比不開更慢";
            map["Hint.SpecDraftPMin"] = "建議 0.1";
            map["Hint.DraftKv"] = "建議 q8_0，draft 模型很小不佔顯存";
            map["Hint.Threads"] = "−1 省略由 llama 決定；辦公共存建議 2~3，保留核心給 Office";
            map["Hint.ThreadsBatch"] = "通常與 --threads 相同即可";
        }

        private static void FillMessages(Dictionary<string, string> map)
        {
            map["Msg.SettingsPath"] = "設定檔位置：{0}";
            map["Msg.Loaded"] = "設定已載入。裝置清單請按「偵測裝置」；初次建議在「本機最佳化」。";
            map["Msg.CapabilitiesRead"] = "已讀取 llama-server 支援的參數（{0} 個）。";
            map["Msg.UsesLoadMode"] = "此版本使用 --load-mode 取代 --no-mmap。";
            map["Msg.UsesNoMmap"] = "此版本仍使用 --no-mmap。";
            map["Msg.DetectingDevices"] = "正在偵測裝置（最多 5 秒）…";
            map["Msg.DetectingBusy"] = "裝置偵測進行中，請稍候…";
            map["Msg.DevicesFound"] = "偵測到裝置：{0}";
            map["Msg.NoDevices"] = "未解析到裝置，可手動輸入 Vulkan0。";
            map["Msg.NoDevicesDialog"] = "沒有偵測到裝置（逾時或輸出無法解析）。可手動輸入 Vulkan0。";
            map["Msg.NeedServerPath"] = "請先指定有效的 llama-server.exe。";
            map["Msg.CannotStart"] = "無法啟動";
            map["Msg.SettingsMayBeWrong"] = "設定可能有問題";
            map["Msg.ContinueAnyway"] = "仍要繼續啟動嗎？";
            map["Msg.PortInUse"] = "連接埠 {0} 已被其他程式占用。";
            map["Msg.PortInUseDetail"] = "可能是上一個 llama-server 還沒結束，或換一個連接埠再試。";
            map["Msg.Starting"] = "[啟動] {0}";
            map["Msg.StartedConsole"] = "已在獨立主控台啟動。關閉該視窗或按停止即可結束。";
            map["Msg.Stopping"] = "[停止] 已送出結束。";
            map["Msg.StoppedExternal"] = "[停止] 已結束 {0} 個先前殘留的 llama-server。";
            map["Msg.NothingToStop"] = "沒有正在執行的 llama-server。";
            map["Msg.ExternalDetected"] = "偵測到先前殘留的 llama-server（PID {0}），可直接按「停止」結束它。";
            map["Msg.ConfirmStopExternal"] = "偵測到先前殘留的 llama-server 仍在執行，可能占用連接埠。要先結束它嗎？";
            map["Msg.ApiReady"] = "[OK] API 已就緒 {0}";
            map["Msg.SavedProfile"] = "已儲存設定檔：{0}";
            map["Msg.SavedAsProfile"] = "已另存設定檔：{0}";
            map["Msg.DeletedProfile"] = "已刪除設定檔。";
            map["Msg.ConfirmDelete"] = "確定刪除設定檔「{0}」？";
            map["Msg.DuplicateName"] = "已有同名設定檔。";
            map["Msg.CommandCopied"] = "已複製指令。";
            map["Msg.Exported"] = "已匯出：{0}";
            map["Msg.ConfirmExitRunning"] = "llama-server 仍在執行，要一併停止並關閉嗎？";
            map["Msg.SaveFailed"] = "設定沒有存檔成功：";
            map["Msg.MemoryUnavailable"] = "無法讀取系統記憶體資訊。";

            map["Msg.VulkanFixed"] = "偵測到系統缺少 Vulkan 註冊資訊，已自動指向顯示卡驅動的 ICD，GPU 可正常使用。";
            map["Msg.VulkanNoDevice"] = "警告：llama-server 找不到任何 Vulkan 裝置，模型將改用 CPU 執行，速度會慢很多。請確認顯示卡驅動與外接連線。";

            map["Status.NotSpecified"] = "未指定";
            map["Status.NotUsed"] = "未使用";
            map["Status.FileMissing"] = "找不到檔案";
        }
    }
}