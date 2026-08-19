using System;

namespace LlamaVulkanLauncher
{
    public sealed class LaunchProfile
    {
        public const string DefaultName = "未命名";
        public const string DefaultLlamaServerPath = @"C:\ai-lab\llama-vulkan\llama-server.exe";
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 8080;
        // 64K 是日常問答／Agent 的甜蜜點：Q4_K_M(16.8GB) + q8_0 KV(64K) 約 21GB，可完整閉環在 24GB 顯存內。
        public const int DefaultContextSize = 65536;
        public const int DefaultGpuLayers = 99;
        public const string DefaultDevice = "Vulkan0";
        // 24GB 顯卡在 64K context 下有餘裕，K/V 都給 q8_0 品質較佳。
        public const string DefaultKvCacheType = "q8_0";
        public const string DefaultReasoning = "off";
        public const string DefaultFlashAttn = "on";
        public const int DefaultUbatchSize = 256;
        public const int DefaultImageMinTokens = 1024;
        public const string DefaultSpecType = "draft-mtp";
        public const int DefaultSpecDraftNMax = 2;
        public const string DefaultDraftKvType = "q8_0";
        // --cache-ram 官方預設僅 8192 MiB，長對話會被清掉導致每輪重算；辦公機建議 16 GB。
        public const int DefaultCacheRam = 16384;
        // --parallel 官方預設 -1（自動）；單人使用固定 1 可獨享完整 context。
        public const int DefaultParallel = 1;
        public const string DefaultChatTemplateFile = @"C:\ai-lab\models\chat_template.jinja";

        public string Name { get; set; }
        public string LlamaServerPath { get; set; }
        public string ModelPath { get; set; }
        public string MmprojPath { get; set; }
        public bool UseMmproj { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public int ContextSize { get; set; }
        public int GpuLayers { get; set; }
        public string Device { get; set; }
        public string KvCacheType { get; set; }
        public string Reasoning { get; set; }
        public string FlashAttn { get; set; }
        public int UbatchSize { get; set; }
        public bool NoMmap { get; set; }
        public int ImageMinTokens { get; set; }
        public bool EnableSpeculative { get; set; }
        public string SpecType { get; set; }
        public int SpecDraftNMax { get; set; }
        public decimal SpecDraftPMin { get; set; }
        public string DraftKvType { get; set; }
        public int Threads { get; set; }
        public int ThreadsBatch { get; set; }
        public string ProcessPrio { get; set; }
        public string ExtraArgs { get; set; }
        public bool ShowConsole { get; set; }
        /// <summary>--cache-ram（MiB）。-1 = 不限制，0 = 停用，其他值為上限。</summary>
        public int CacheRam { get; set; }
        /// <summary>--parallel 伺服器 slot 數。0 以下代表省略此參數（交給 llama-server 自動判斷）。</summary>
        public int Parallel { get; set; }
        /// <summary>是否輸出 --chat-template-file（僅官方版模型適用，Uncensored 版內建模板不可外掛）。</summary>
        public bool UseChatTemplate { get; set; }
        public string ChatTemplateFile { get; set; }
        /// <summary>--reasoning-preserve：保留思考內容，讓 prefix cache 命中率維持 100%。</summary>
        public bool ReasoningPreserve { get; set; }

        public LaunchProfile()
        {
            Name = DefaultName;
            LlamaServerPath = DefaultLlamaServerPath;
            ModelPath = "";
            MmprojPath = "";
            UseMmproj = true;
            Host = DefaultHost;
            Port = DefaultPort;
            ContextSize = DefaultContextSize;
            GpuLayers = DefaultGpuLayers;
            Device = DefaultDevice;
            KvCacheType = DefaultKvCacheType;
            Reasoning = DefaultReasoning;
            FlashAttn = DefaultFlashAttn;
            UbatchSize = DefaultUbatchSize;
            // 預設交給 llama.cpp 自行決定（實際會使用 mmap），記憶體佔用較低也較安全。
            // 記憶體確實寬裕時，可在「本機最佳化」由程式判斷後建議開啟。
            NoMmap = false;
            ImageMinTokens = DefaultImageMinTokens;
            EnableSpeculative = true;
            SpecType = DefaultSpecType;
            SpecDraftNMax = DefaultSpecDraftNMax;
            SpecDraftPMin = 0.1m;
            DraftKvType = DefaultDraftKvType;
            Threads = -1;
            ThreadsBatch = -1;
            ProcessPrio = "";
            ExtraArgs = "";
            ShowConsole = false;
            CacheRam = DefaultCacheRam;
            Parallel = DefaultParallel;
            UseChatTemplate = false;
            ChatTemplateFile = DefaultChatTemplateFile;
            ReasoningPreserve = false;
        }

        /// <summary>
        /// 複製整份設定檔（含名稱）。所有欄位皆為實值型別或字串，淺層複製即可。
        /// </summary>
        public LaunchProfile Clone()
        {
            return (LaunchProfile)MemberwiseClone();
        }

        /// <summary>
        /// 把來源的所有設定欄位複製到目標，但保留目標原本的名稱（名稱是設定檔的識別鍵）。
        /// </summary>
        public void CopySettingsTo(LaunchProfile dest)
        {
            if (dest == null)
            {
                return;
            }

            string keepName = dest.Name;
            CopyAllFieldsTo(dest);
            dest.Name = keepName;
        }

        private void CopyAllFieldsTo(LaunchProfile dest)
        {
            dest.Name = Name;
            dest.LlamaServerPath = LlamaServerPath;
            dest.ModelPath = ModelPath;
            dest.MmprojPath = MmprojPath;
            dest.UseMmproj = UseMmproj;
            dest.Host = Host;
            dest.Port = Port;
            dest.ContextSize = ContextSize;
            dest.GpuLayers = GpuLayers;
            dest.Device = Device;
            dest.KvCacheType = KvCacheType;
            dest.Reasoning = Reasoning;
            dest.FlashAttn = FlashAttn;
            dest.UbatchSize = UbatchSize;
            dest.NoMmap = NoMmap;
            dest.ImageMinTokens = ImageMinTokens;
            dest.EnableSpeculative = EnableSpeculative;
            dest.SpecType = SpecType;
            dest.SpecDraftNMax = SpecDraftNMax;
            dest.SpecDraftPMin = SpecDraftPMin;
            dest.DraftKvType = DraftKvType;
            dest.Threads = Threads;
            dest.ThreadsBatch = ThreadsBatch;
            dest.ProcessPrio = ProcessPrio;
            dest.ExtraArgs = ExtraArgs;
            dest.ShowConsole = ShowConsole;
            dest.CacheRam = CacheRam;
            dest.Parallel = Parallel;
            dest.UseChatTemplate = UseChatTemplate;
            dest.ChatTemplateFile = ChatTemplateFile;
            dest.ReasoningPreserve = ReasoningPreserve;
        }

        public static LaunchProfile CreateQ4()
        {
            LaunchProfile p = CreateBase7900Xtx();
            p.Name = "Qwen3.8-27B Uncensored Q4_K_M by JonathanColetti";
            p.ModelPath = @"C:\ai-lab\models\qwen\Qwen3.8-27B-Uncensored\Qwen3.8-27B-Uncensored-Q4_K_M.gguf";
            p.MmprojPath = @"C:\ai-lab\models\qwen\Qwen3.8-27B-Uncensored\Qwen3.8-27B-Uncensored-vision-f16.gguf";
            return p;
        }

        public static LaunchProfile CreateQ5()
        {
            LaunchProfile p = CreateBase7900Xtx();
            p.Name = "Qwen3.8-27B Uncensored Q5_K_M by JonathanColetti";
            p.ModelPath = @"C:\ai-lab\models\Qwen3.8-27B-Uncensored-Q5_K_M.gguf";
            p.MmprojPath = @"C:\ai-lab\models\Qwen3.8-27B-Uncensored-vision-f16.gguf";
            return p;
        }

        /// <summary>
        /// 官方版 Qwen3.8-27B 搭配 froggeric v22.1 修正模板。
        /// 官方版模型才需要外掛模板；JonathanColetti 的 Uncensored 版內建自己的模板，掛上去會吐出模板原始碼。
        /// </summary>
        public static LaunchProfile CreateOfficialQ4()
        {
            LaunchProfile p = CreateBase7900Xtx();
            p.Name = "Qwen3.8-27B 官方 Q4_K_M + froggeric 模板";
            p.ModelPath = @"C:\ai-lab\models\Qwen3.8-27B-Q4_K_M.gguf";
            p.MmprojPath = @"C:\ai-lab\models\mmproj-Qwen3.8-27B-F16.gguf";
            p.UseChatTemplate = true;
            p.ChatTemplateFile = DefaultChatTemplateFile;
            p.ReasoningPreserve = true;
            // 思考等級改由 client 端 reasoning_effort 控制，這裡不強制關閉思考。
            p.Reasoning = "auto";
            return p;
        }

        private static LaunchProfile CreateBase7900Xtx()
        {
            // 其餘欄位沿用建構子的預設值（見上方 Default* 常數）。
            LaunchProfile p = new LaunchProfile();
            p.Threads = SystemResources.SuggestOfficeThreads(SystemResources.Query());
            p.ThreadsBatch = p.Threads;
            p.ProcessPrio = "low";
            p.ExtraArgs = "";
            p.ShowConsole = false;
            return p;
        }

        public string GetApiUrl()
        {
            string host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim();
            if (host == "0.0.0.0")
            {
                host = "127.0.0.1";
            }
            return "http://" + host + ":" + Port.ToString();
        }
    }

    public sealed class AppState
    {
        public string ActiveProfileName { get; set; }
        public LaunchProfile[] Profiles { get; set; }
        public bool OptimizerOffered { get; set; }

        public AppState()
        {
            ActiveProfileName = "";
            Profiles = new LaunchProfile[0];
            OptimizerOffered = false;
        }
    }
}
