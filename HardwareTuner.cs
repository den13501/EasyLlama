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

        /// <summary>辦公並行模式的上下文上限，超過對日常使用沒有好處。</summary>
        private const int OfficeMaxContext = 65536;

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
                // 辦公情境下 64K 已是甜蜜點，再往上只會拖慢首字延遲。
                next.ContextSize = OfficeMaxContext;
            }

            // 24GB 顯卡扣掉 17GB 模型後大約剩 5~6GB，這個區間就足以支撐 q8_0 的 KV，
            // 因此門檻設在 5GB，而不是先前保守的 24GB。
            if (leftover < 0)
            {
                next.KvCacheType = LaunchProfile.DefaultKvCacheType;
            }
            else if (leftover < 3L * 1024L * 1024L * 1024L)
            {
                next.KvCacheType = "q4_0";
            }
            else if (leftover < 5L * 1024L * 1024L * 1024L)
            {
                next.KvCacheType = "q5_0";
            }
            else
            {
                next.KvCacheType = "q8_0";
            }

            next.GpuLayers = 99;
            next.Parallel = LaunchProfile.DefaultParallel;
            // --cache-ram 走系統記憶體：32GB 機器辦公時給 16GB，效能模式或更大記憶體再往上加。
            double totalGb = SystemResources.BytesToGb(hardware.TotalMemoryBytes);
            if (totalGb <= 0)
            {
                next.CacheRam = LaunchProfile.DefaultCacheRam;
            }
            else if (totalGb >= 48)
            {
                next.CacheRam = office ? 24576 : 32768;
            }
            else if (totalGb >= 24)
            {
                next.CacheRam = office ? 16384 : 24576;
            }
            else
            {
                next.CacheRam = 8192;
            }
            next.UbatchSize = (office && leftover >= 0 && leftover < 6L * 1024L * 1024L * 1024L)
                ? 128
                : 256;
            next.FlashAttn = "on";
            next.NoMmap = true;

            bool specOk = leftover < 0 ? !office : leftover >= 8L * 1024L * 1024L * 1024L;
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

            long reserve = SystemResources.SuggestOfficeReserveBytes(hardware);
            if (next.NoMmap && modelBytes > 0 && hardware.TotalMemoryBytes > 0
                && modelBytes > hardware.TotalMemoryBytes - reserve)
            {
                warnings.Add("已開 --no-mmap，載入瞬間會先把整份模型讀進 RAM。請先關大型程式再啟動。");
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
            AddIfChanged(list, "KV cache",
                Blank(current.KvCacheType), Blank(next.KvCacheType),
                leftover >= 0 && leftover < 6L * 1024L * 1024L * 1024L
                    ? "顯存較緊，用 q4_0 省 KV"
                    : "一般情況用 q5_0，兼顧品質與顯存");
            AddIfChanged(list, "GPU 層數 --n-gpu-layers",
                current.GpuLayers.ToString(), next.GpuLayers.ToString(),
                "權重盡量上 GPU");
            AddIfChanged(list, "ubatch -ub",
                current.UbatchSize.ToString(), next.UbatchSize.ToString(),
                next.UbatchSize <= 128 ? "顯存較緊，降低批次" : "維持預設 256");
            AddIfChanged(list, "Flash Attention",
                Blank(current.FlashAttn), Blank(next.FlashAttn),
                "Vulkan 建議開啟");
            AddIfChanged(list, "停用 mmap",
                current.NoMmap ? "是" : "否", next.NoMmap ? "是" : "否",
                "與既有 BAT 預設相同");
            AddIfChanged(list, "推測解碼",
                FormatSpec(current), FormatSpec(next),
                next.EnableSpeculative
                    ? "剩顯存足夠，維持 draft-mtp"
                    : "剩顯存不足約 8 GB，先關閉以免擠爆");
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

        private static int ContextFromLeftover(long leftover)
        {
            if (leftover < 0)
            {
                return 16384;
            }
            long gb3 = 3L * 1024L * 1024L * 1024L;
            long gb6 = 6L * 1024L * 1024L * 1024L;
            long gb10 = 10L * 1024L * 1024L * 1024L;
            long gb16 = 16L * 1024L * 1024L * 1024L;
            long gb24 = 24L * 1024L * 1024L * 1024L;
            if (leftover < gb3)
            {
                return 4096;
            }
            if (leftover < gb6)
            {
                return 8192;
            }
            if (leftover < gb10)
            {
                return 16384;
            }
            if (leftover < gb16)
            {
                return 32768;
            }
            if (leftover < gb24)
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
