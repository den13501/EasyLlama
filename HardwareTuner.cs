using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LlamaVulkanLauncher
{
    internal sealed class TuneChange
    {
        public string Field;
        public string Current;
        public string Suggested;
        public string Reason;
    }

    internal sealed class TunePlan
    {
        public bool Office;
        public LaunchProfile Suggested;
        public TuneChange[] Changes;
        public string[] Warnings;
        public string Summary;
        public bool UsedFallback;
    }

    internal static class HardwareTuner
    {
        private static readonly int[] ContextSteps = new int[]
        {
            4096, 8192, 16384, 32768, 65536, 131072
        };

        /// <summary>
        /// 辦公並行模式的上下文上限。
        /// 實測 24 GB 顯卡跑 27B Q4 模型時，ctx 65536 會越過顯存臨界點而掉到 9 t/s，
        /// 32768 則能穩定維持 53~63 t/s，因此上限設在 32768。
        /// </summary>
        private const int OfficeMaxContext = 32768;

        public static TunePlan Build(HardwareInfo hardware, LaunchProfile current, bool office)
        {
            if (current == null)
            {
                current = new LaunchProfile();
            }
            if (hardware == null)
            {
                hardware = SystemResources.Query();
            }

            LaunchProfile next = current.Clone();
            List<string> warnings = new List<string>();
            bool usedFallback = false;

            int threads = office
                ? SystemResources.SuggestOfficeThreads(hardware)
                : SystemResources.SuggestPerfThreads(hardware);
            next.Threads = threads;
            next.ThreadsBatch = threads;
            next.ProcessPrio = office ? "low" : "normal";

            string vulkanId = SystemResources.FirstVulkanId(hardware);
            if (!string.IsNullOrEmpty(vulkanId))
            {
                if (!SystemResources.HasVulkanId(hardware, next.Device))
                {
                    next.Device = vulkanId;
                }
            }

            long modelBytes = FileSizeOrZero(current.ModelPath);
            long mmprojBytes = 0;
            if (current.UseMmproj)
            {
                mmprojBytes = FileSizeOrZero(current.MmprojPath);
            }

            long vram = hardware.DedicatedVramBytes;
            long leftover = 0;
            if (vram <= 0)
            {
                usedFallback = true;
                leftover = -1;
                warnings.Add("尚未讀到顯存，上下文先用 16384。指定 llama-server 後再按一次可重估。");
            }
            else if (modelBytes <= 0)
            {
                usedFallback = true;
                leftover = -1;
                warnings.Add("尚未指定模型檔，上下文先用 16384。選好 GGUF 後再按一次會依檔案大小重估。");
            }
            else
            {
                // 視覺編碼在處理圖片時會有臨時峰值，多留一點空間比較保險。
                long overhead = 2L * 1024L * 1024L * 1024L;
                leftover = vram - modelBytes - mmprojBytes - overhead;
                if (modelBytes + mmprojBytes > (long)(vram * 0.9))
                {
                    warnings.Add("模型（加 mmproj）接近或超過專用顯存，可能載不進去。可改較小量化，或之後再降 GPU 層數。");
                }
            }

            int baseCtx = ContextFromLeftover(leftover);
            next.ContextSize = office ? baseCtx : StepUp(baseCtx);
            if (office && next.ContextSize > OfficeMaxContext)
            {
                // 辦公情境下 32K 已是甜蜜點，再往上容易越過顯存臨界點。
                next.ContextSize = OfficeMaxContext;
            }

            // 最後依顯示卡容量再壓一次：剩餘顯存的估算有誤差，
            // 而越過臨界點的代價（速度剩十分之一）遠大於少開一階。
            next.ContextSize = CapContextByVram(next.ContextSize, vram);

            // 24GB 顯卡扣掉 17GB 模型後大約剩 5~6GB，這個區間就足以支撐 q8_0 的 KV，
            // 因此門檻設在 5GB，而不是先前保守的 24GB。
            if (leftover < 0)
            {
                next.KvCacheType = LaunchProfile.DefaultKvCacheType;
            }
            else if (leftover < 1024L * 1024L * 1024L)
            {
                next.KvCacheType = "q4_0";
            }
            else if (leftover < 2L * 1024L * 1024L * 1024L)
            {
                next.KvCacheType = "q5_0";
            }
            else
            {
                next.KvCacheType = "q8_0";
            }

            next.GpuLayers = 99;
            next.Parallel = LaunchProfile.DefaultParallel;
            // --cache-ram 走系統記憶體，而且是「上限值」：長對話會慢慢長到這個數字。
            // 舊版只看記憶體總量，沒有扣掉模型本身佔用，
            // 在 32 GB 機器上會建議 16384，加上約 16 GB 的模型快取後正好塞爆，
            // 實測會讓 Pages Input/sec 飆到 1300 以上（系統瘋狂分頁）。
            // 因此改成先扣掉模型與系統保留，再從剩餘量分配。
            next.CacheRam = SuggestCacheRam(hardware, modelBytes, mmprojBytes, office);

            // ubatch 256 是建議值，只有顯存真的所剩無幾時才降到 128。
            // 舊門檻是 6 GB，但實測顯示 ctx 拉滿也只多吃 2 GB 左右，
            // 6 GB 餘裕其實非常寬鬆，不需要降批次。
            next.UbatchSize = (leftover >= 0 && leftover < 2L * 1024L * 1024L * 1024L)
                ? 128
                : 256;
            next.FlashAttn = "on";

            // 「不使用 mmap」會在啟動時把整份模型另外讀進系統記憶體，
            // 等於在權重之外再吃掉一份等同模型大小的 RAM。
            // 記憶體不夠時這會逼出分頁（paging），速度反而嚴重下滑，
            // 因此只有在記憶體明顯寬裕時才建議開啟。
            next.NoMmap = ShouldSuggestNoMmap(hardware, modelBytes);

            // MTP 推測解碼（draft-mtp）重用主模型的權重，額外開銷主要是 draft KV，
            // 以 q8_0 存放時只有數百 MB，不需要 8 GB 這種等級的餘裕。
            // 舊門檻會在顯存充足時就把加速功能關掉，反而讓生成變慢。
            bool specOk = leftover < 0 ? !office : leftover >= 2L * 1024L * 1024L * 1024L;
            if (specOk)
            {
                next.EnableSpeculative = true;
                if (string.IsNullOrEmpty(next.SpecType)
                    || string.Equals(next.SpecType, "none", StringComparison.OrdinalIgnoreCase))
                {
                    next.SpecType = "draft-mtp";
                }
                if (next.SpecDraftNMax <= 0)
                {
                    next.SpecDraftNMax = 2;
                }
            }
            else
            {
                next.EnableSpeculative = false;
            }

            // 記憶體不足以支撐「不使用 mmap」時，說明為什麼建議關掉它。
            if (!next.NoMmap && current != null && current.NoMmap)
            {
                warnings.Add("已建議取消「不使用 mmap」：本機記憶體不足以在權重之外"
                    + "再放一份完整模型，勾著容易觸發分頁而嚴重掉速。改用 mmap 由系統管理較穩定。");
            }

            TunePlan plan = new TunePlan();
            plan.Office = office;
            plan.Suggested = next;
            plan.Warnings = warnings.ToArray();
            plan.UsedFallback = usedFallback;
            plan.Changes = Diff(current, next, hardware, leftover, office);
            plan.Summary = BuildSummary(hardware, next, leftover, office, usedFallback);
            return plan;
        }

        /// <summary>
        /// 判斷是否建議勾選「不使用 mmap」。
        /// 這個選項會讓 llama.cpp 把整份模型讀進系統記憶體（--load-mode none），
        /// 在權重之外額外佔用一份等同模型大小的 RAM。
        /// 記憶體不足時會觸發分頁而嚴重掉速，因此只在明顯有餘裕時才建議開啟。
        /// </summary>
        /// <summary>
        /// 依「扣掉模型之後還剩多少系統記憶體」建議 --cache-ram（單位 MiB）。
        /// 這個參數是上限值：長對話會慢慢長到這個數字，因此不能只看記憶體總量。
        /// 使用 mmap 時模型檔本身也會佔用等量的檔案快取，必須一併扣除。
        /// </summary>
        private static int SuggestCacheRam(
            HardwareInfo hardware, long modelBytes, long mmprojBytes, bool office)
        {
            if (hardware == null || hardware.TotalMemoryBytes <= 0)
            {
                return LaunchProfile.DefaultCacheRam;
            }

            long reserve = SystemResources.SuggestOfficeReserveBytes(hardware);
            long spare = hardware.TotalMemoryBytes - reserve - modelBytes - mmprojBytes;
            double spareGb = SystemResources.BytesToGb(spare);

            if (spareGb <= 0)
            {
                // 連模型都快放不下，快取只能給最低限度。
                return 2048;
            }

            // 只拿剩餘量的一半來當快取，另一半留給突發需求，
            // 避免長對話跑久了才把記憶體吃光。
            int mib = (int)(spareGb * 1024 / 2);

            // 辦公情境再保守一些，把核心讓給其他程式。
            if (office)
            {
                mib = mib / 2;
            }

            if (mib < 2048) { return 2048; }
            if (mib > 32768) { return 32768; }

            // 對齊到 1024 的倍數，數字比較好讀。
            return (mib / 1024) * 1024;
        }

        /// <summary>依實際選出的 KV cache 型別給對應說明。</summary>
        private static string KvCacheReason(string kvType)
        {
            if (string.Equals(kvType, "q4_0", StringComparison.OrdinalIgnoreCase))
            {
                return "顯存所剩無幾，用 q4_0 省 KV";
            }
            if (string.Equals(kvType, "q5_0", StringComparison.OrdinalIgnoreCase))
            {
                return "顯存偏緊，用 q5_0 兼顧品質與容量";
            }
            return "顯存充足，用 q8_0 保留品質";
        }

        private static bool ShouldSuggestNoMmap(HardwareInfo hardware, long modelBytes)
        {
            // 資訊不足時一律不建議：mmap 是 llama.cpp 預設值，也是較安全的選擇。
            if (hardware == null || hardware.TotalMemoryBytes <= 0 || modelBytes <= 0)
            {
                return false;
            }

            // 除了模型本身，還要留給作業系統與其他辦公軟體使用。
            long reserve = SystemResources.SuggestOfficeReserveBytes(hardware);
            long usable = hardware.TotalMemoryBytes - reserve;

            // 需要能同時容納「模型副本」與「等量的檔案快取／工作區」才算寬裕，
            // 因此門檻設在模型大小的兩倍。
            return usable >= modelBytes * 2L;
        }

        public static void CopyTunable(LaunchProfile src, LaunchProfile dest)
        {
            if (src == null || dest == null)
            {
                return;
            }
            dest.Device = src.Device;
            dest.ContextSize = src.ContextSize;
            dest.GpuLayers = src.GpuLayers;
            dest.KvCacheType = src.KvCacheType;
            dest.FlashAttn = src.FlashAttn;
            dest.UbatchSize = src.UbatchSize;
            dest.NoMmap = src.NoMmap;
            dest.EnableSpeculative = src.EnableSpeculative;
            dest.SpecType = src.SpecType;
            dest.SpecDraftNMax = src.SpecDraftNMax;
            dest.CacheRam = src.CacheRam;
            dest.Parallel = src.Parallel;
            dest.Threads = src.Threads;
            dest.ThreadsBatch = src.ThreadsBatch;
            dest.ProcessPrio = src.ProcessPrio;
        }

        public static string FormatPreview(TunePlan plan)
        {
            if (plan == null)
            {
                return "";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(plan.Summary);
            sb.AppendLine();
            if (plan.Changes != null && plan.Changes.Length > 0)
            {
                sb.AppendLine("將變更：");
                for (int i = 0; i < plan.Changes.Length; i++)
                {
                    TuneChange c = plan.Changes[i];
                    sb.Append("• ");
                    sb.Append(c.Field);
                    sb.Append("　");
                    sb.Append(c.Current);
                    sb.Append("  →  ");
                    sb.Append(c.Suggested);
                    if (!string.IsNullOrEmpty(c.Reason))
                    {
                        sb.Append("　（");
                        sb.Append(c.Reason);
                        sb.Append("）");
                    }
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("目前設定已與此建議相同。");
            }

            if (plan.Warnings != null && plan.Warnings.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("提醒：");
                for (int i = 0; i < plan.Warnings.Length; i++)
                {
                    sb.Append("• ");
                    sb.AppendLine(plan.Warnings[i]);
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static TuneChange[] Diff(
            LaunchProfile current,
            LaunchProfile next,
            HardwareInfo hardware,
            long leftover,
            bool office)
        {
            List<TuneChange> list = new List<TuneChange>();
            int reserved = Math.Max(0, hardware.PhysicalCores - next.Threads);
            AddIfChanged(list, "CPU 執行緒 --threads",
                FormatThreads(current.Threads), FormatThreads(next.Threads),
                office
                    ? "預留 " + reserved.ToString() + " 實體核給系統 / 辦公"
                    : "用滿實體核心，不吃超執行緒");
            AddIfChanged(list, "批次執行緒 --threads-batch",
                FormatThreads(current.ThreadsBatch), FormatThreads(next.ThreadsBatch),
                "與生成執行緒相同，避免 prompt 處理吃滿");
            AddIfChanged(list, "行程優先權 --prio",
                FormatPrio(current.ProcessPrio), FormatPrio(next.ProcessPrio),
                office ? "low，避免搶 Office / 瀏覽器" : "normal，讓推論多拿一點 CPU");
            AddIfChanged(list, "裝置 --device",
                Blank(current.Device), Blank(next.Device),
                "使用偵測到的 Vulkan 裝置");
            AddIfChanged(list, "上下文 --ctx-size",
                current.ContextSize.ToString(), next.ContextSize.ToString(),
                leftover < 0
                    ? "顯存或模型尚未齊，先用保守值"
                    : "依剩顯存約 " + SystemResources.FormatGb(leftover) + " 分檔");
            // 理由要跟實際選出的型別一致，否則會出現「說 q4_0 卻填 q8_0」的矛盾。
            AddIfChanged(list, "KV cache",
                Blank(current.KvCacheType), Blank(next.KvCacheType),
                KvCacheReason(next.KvCacheType));
            AddIfChanged(list, "GPU 層數 --n-gpu-layers",
                current.GpuLayers.ToString(), next.GpuLayers.ToString(),
                "權重盡量上 GPU");
            AddIfChanged(list, "ubatch -ub",
                current.UbatchSize.ToString(), next.UbatchSize.ToString(),
                next.UbatchSize <= 128 ? "顯存所剩無幾，降低批次" : "維持建議值 256");
            AddIfChanged(list, "Flash Attention",
                Blank(current.FlashAttn), Blank(next.FlashAttn),
                "Vulkan 建議開啟");
            AddIfChanged(list, "不使用 mmap（整份讀進記憶體）",
                current.NoMmap ? "是" : "否", next.NoMmap ? "是" : "否",
                next.NoMmap
                    ? "記憶體充裕，載入後較穩定"
                    : "改用 mmap 省下一份模型大小的記憶體");
            AddIfChanged(list, "推測解碼",
                FormatSpec(current), FormatSpec(next),
                next.EnableSpeculative
                    ? "剩顯存足夠，維持 draft-mtp 以加速生成"
                    : "剩顯存不足約 2 GB，先關閉以免擠爆");
            return list.ToArray();
        }

        private static void AddIfChanged(
            List<TuneChange> list,
            string field,
            string current,
            string suggested,
            string reason)
        {
            if (string.Equals(current, suggested, StringComparison.Ordinal))
            {
                return;
            }
            TuneChange change = new TuneChange();
            change.Field = field;
            change.Current = current;
            change.Suggested = suggested;
            change.Reason = reason;
            list.Add(change);
        }

        private static string BuildSummary(
            HardwareInfo hardware,
            LaunchProfile next,
            long leftover,
            bool office,
            bool usedFallback)
        {
            string mode = office ? "辦公並行" : "效能優先";
            string gpu = string.IsNullOrEmpty(hardware.GpuName) ? "未偵測到獨立顯卡" : hardware.GpuName;
            string vram = hardware.DedicatedVramBytes > 0
                ? SystemResources.FormatGb(hardware.DedicatedVramBytes)
                : "未知";
            StringBuilder sb = new StringBuilder();
            sb.Append("【");
            sb.Append(mode);
            sb.Append("】");
            sb.Append(hardware.CpuName);
            sb.Append("，");
            sb.Append(hardware.PhysicalCores.ToString());
            sb.Append(" 核 / 記憶體 ");
            sb.Append(SystemResources.FormatGb(hardware.TotalMemoryBytes));
            sb.Append(" / ");
            sb.Append(gpu);
            sb.Append("（顯存 ");
            sb.Append(vram);
            sb.Append("）。");
            sb.AppendLine();
            sb.Append("建議 --threads ");
            sb.Append(next.Threads.ToString());
            sb.Append("、ctx ");
            sb.Append(next.ContextSize.ToString());
            sb.Append("、KV ");
            sb.Append(next.KvCacheType);
            sb.Append(next.EnableSpeculative ? "、推測解碼開。" : "、推測解碼關。");
            if (leftover >= 0)
            {
                sb.Append(" 估剩顯存約 ");
                sb.Append(SystemResources.FormatGb(leftover));
                sb.Append("。");
            }
            if (usedFallback)
            {
                sb.Append(" 部分項目用保守預設。");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 依顯示卡容量再壓一次上下文上限。
        /// 顯示卡不會讓單一程式用滿標示容量，實測 24 GB 的卡大約在 18.5 GB 就觸頂，
        /// 越過之後資料會被放到系統記憶體，速度直接掉到十分之一。
        /// 「剩餘顯存」的估算難免有誤差，因此這裡用顯存總量再設一道保險。
        /// </summary>
        private static int CapContextByVram(int ctx, long vramBytes)
        {
            if (vramBytes <= 0)
            {
                return ctx;
            }

            double vramGb = SystemResources.BytesToGb(vramBytes);
            int cap;
            if (vramGb >= 44) { cap = 131072; }
            else if (vramGb >= 30) { cap = 65536; }
            else if (vramGb >= 20) { cap = 32768; }   // 24 GB 卡實測 65536 會掉速
            else if (vramGb >= 14) { cap = 16384; }
            else { cap = 8192; }

            return ctx > cap ? cap : ctx;
        }

        private static int ContextFromLeftover(long leftover)
        {
            if (leftover < 0)
            {
                return 16384;
            }
            // 門檻依「長對話負載」實測校正
            // （RX 7900 XTX 24GB / Qwen3.8-27B Q4_K_M / KV q8_0 / -fa on / draft-mtp）：
            //
            //   ctx  16384 → 顯存 17.41 GB → 56~59 t/s   正常
            //   ctx  32768 → 顯存 17.92 GB → 53~63 t/s   正常
            //   ctx  65536 → 顯存 18.91 GB →  8.6~9 t/s  嚴重掉速
            //
            // 顯存只多用約 1 GB，速度卻掉到七分之一：超過顯示卡實際可用上限後，
            // 部分資料被迫放到系統記憶體，每次推論都要走 PCIe。
            // 注意這與「實際用到多深」無關 —— KV cache 在啟動時就依 ctx 上限
            // 全量配置，所以 ctx 65536 即使只用 2000 token 一樣只有 9 t/s。
            //
            // 因此門檻必須保守：寧可少開一階，也不要越過臨界點。
            // 另外顯示卡不會讓單一程式用滿標示容量（24 GB 卡實測約 18.5 GB 就觸頂），
            // 呼叫端還會再用 CapContextByVram 依顯存總量設上限。
            long gb1 = 1024L * 1024L * 1024L;
            if (leftover < 2L * gb1)
            {
                // 幾乎沒有餘裕，先求載得進去。
                return 4096;
            }
            if (leftover < 4L * gb1)
            {
                return 16384;
            }
            if (leftover < 8L * gb1)
            {
                return 32768;
            }
            if (leftover < 14L * gb1)
            {
                return 65536;
            }
            return 131072;
        }

        private static int StepUp(int ctx)
        {
            for (int i = 0; i < ContextSteps.Length; i++)
            {
                if (ContextSteps[i] == ctx)
                {
                    return i + 1 < ContextSteps.Length ? ContextSteps[i + 1] : ContextSteps[i];
                }
            }
            return ctx;
        }

        private static long FileSizeOrZero(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return 0;
            }
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string FormatThreads(int value)
        {
            return value < 0 ? "省略" : value.ToString();
        }

        private static string FormatPrio(string value)
        {
            string prio = SystemResources.NormalizePrio(value);
            return string.IsNullOrEmpty(prio) ? "省略" : prio;
        }

        private static string FormatSpec(LaunchProfile profile)
        {
            if (profile == null || !profile.EnableSpeculative
                || string.Equals(profile.SpecType, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "關";
            }
            return profile.SpecType + " / n=" + profile.SpecDraftNMax.ToString();
        }

        private static string Blank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（空）" : value.Trim();
        }
    }
}
