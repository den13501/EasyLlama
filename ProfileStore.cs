using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace LlamaVulkanLauncher
{
    internal static class ProfileStore
    {
        /// <summary>
        /// 設定檔放在執行檔旁的 config 資料夾，整個程式可以直接搬走或放隨身碟使用。
        /// </summary>
        public static string GetDirectory()
        {
            return Path.Combine(GetBaseDirectory(), "config");
        }

        public static string GetFilePath()
        {
            return Path.Combine(GetDirectory(), "settings.xml");
        }

        private static string GetBaseDirectory()
        {
            // BaseDirectory 不受目前工作目錄影響，比 Assembly.Location 穩定。
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir))
            {
                dir = Environment.CurrentDirectory;
            }
            return dir;
        }

        /// <summary>
        /// 舊版把設定放在使用者的漫遊設定檔資料夾，保留此路徑只為了做一次性搬移。
        /// </summary>
        private static string GetLegacyDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LlamaVulkanLauncher");
        }

        /// <summary>
        /// 搬移舊設定時給使用者看的訊息，沒有搬移動作時為空字串。
        /// </summary>
        public static string LastMigrationNote { get; private set; }

        /// <summary>
        /// 若新位置還沒有設定檔、而舊位置有，就把它搬過來並清掉舊資料夾。
        /// </summary>
        private static void MigrateLegacySettings()
        {
            LastMigrationNote = "";
            string target = GetFilePath();
            if (File.Exists(target))
            {
                return;
            }

            string legacyDir = GetLegacyDirectory();
            string legacyFile = Path.Combine(legacyDir, "settings.xml");
            if (!File.Exists(legacyFile))
            {
                return;
            }

            try
            {
                string dir = GetDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(legacyFile, target, false);
                LastMigrationNote = "已將設定從舊位置搬到程式資料夾：" + target;

                // 搬移成功才刪除舊資料，避免中途失敗造成設定遺失。
                try
                {
                    Directory.Delete(legacyDir, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("刪除舊設定資料夾失敗：" + ex.Message);
                    LastMigrationNote += "（舊資料夾未能刪除，可手動移除：" + legacyDir + "）";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("搬移舊設定失敗：" + ex.Message);
                LastMigrationNote = "搬移舊設定失敗：" + ex.Message;
            }
        }

        /// <summary>
        /// 上次載入時若設定檔損毀，這裡會記下備份檔路徑，供 UI 提示使用者；正常情況為空字串。
        /// </summary>
        public static string LastLoadError { get; private set; }

        public static AppState LoadOrCreate()
        {
            LastLoadError = "";
            MigrateLegacySettings();

            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return CreateDefault();
            }

            try
            {
                AppState loaded = Load(path);
                if (loaded != null && loaded.Profiles != null && loaded.Profiles.Length > 0)
                {
                    Normalize(loaded);
                    return loaded;
                }
                LastLoadError = "設定檔內容是空的，已改用預設設定。";
            }
            catch (Exception ex)
            {
                // 設定檔損毀時保留原檔備份，避免使用者的設定無聲消失。
                string backup = BackupCorruptFile(path);
                LastLoadError = string.IsNullOrEmpty(backup)
                    ? "設定檔無法讀取（" + ex.Message + "），已改用預設設定。"
                    : "設定檔無法讀取（" + ex.Message + "），原檔已備份為：" + backup;
            }

            return CreateDefault();
        }

        public static void Save(AppState state)
        {
            if (state == null)
            {
                return;
            }

            try
            {
                string dir = GetDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 先寫入暫存檔再置換，避免寫到一半中斷造成設定檔損毀。
                string path = GetFilePath();
                string temp = path + ".tmp";
                XmlSerializer serializer = new XmlSerializer(typeof(AppState));
                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        serializer.Serialize(writer, state);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, null, true);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // 程式放在 Program Files 這類受保護位置時會發生，明確告知使用者而不是無聲失敗。
                throw new IOException(
                    "無法寫入設定檔資料夾：" + GetDirectory() + Environment.NewLine
                    + "請把程式移到有寫入權限的位置（例如 C:\\ai-lab\\），或以系統管理員身分執行。", ex);
            }
        }

        private static string BackupCorruptFile(string path)
        {
            try
            {
                string backup = path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Copy(path, backup, true);
                return backup;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("備份損毀設定檔失敗：" + ex.Message);
                return "";
            }
        }

        private static AppState Load(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(AppState));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return serializer.Deserialize(stream) as AppState;
            }
        }

        public static AppState CreateDefault()
        {
            AppState state = new AppState();
            LaunchProfile q4 = LaunchProfile.CreateQ4();
            LaunchProfile q5 = LaunchProfile.CreateQ5();
            LaunchProfile official = LaunchProfile.CreateOfficialQ4();
            HardwareInfo hardware = SystemResources.Query();
            HardwareTuner.CopyTunable(HardwareTuner.Build(hardware, q4, true).Suggested, q4);
            HardwareTuner.CopyTunable(HardwareTuner.Build(hardware, q5, true).Suggested, q5);
            HardwareTuner.CopyTunable(HardwareTuner.Build(hardware, official, true).Suggested, official);
            state.Profiles = new LaunchProfile[] { q4, q5, official };
            state.ActiveProfileName = state.Profiles[0].Name;
            state.OptimizerOffered = false;
            return state;
        }

        private static void Normalize(AppState state)
        {
            List<LaunchProfile> list = new List<LaunchProfile>();
            if (state.Profiles != null)
            {
                for (int i = 0; i < state.Profiles.Length; i++)
                {
                    if (state.Profiles[i] != null)
                    {
                        ApplyDefaults(state.Profiles[i]);
                        list.Add(state.Profiles[i]);
                    }
                }
            }

            if (list.Count == 0)
            {
                list.Add(LaunchProfile.CreateQ4());
            }

            state.Profiles = list.ToArray();
            if (string.IsNullOrEmpty(state.ActiveProfileName))
            {
                state.ActiveProfileName = state.Profiles[0].Name;
            }
        }

        private static void ApplyDefaults(LaunchProfile p)
        {
            if (string.IsNullOrEmpty(p.Name))
            {
                p.Name = LaunchProfile.DefaultName;
            }
            if (string.IsNullOrEmpty(p.LlamaServerPath))
            {
                p.LlamaServerPath = LaunchProfile.DefaultLlamaServerPath;
            }
            if (string.IsNullOrEmpty(p.Host))
            {
                p.Host = LaunchProfile.DefaultHost;
            }
            if (p.Port <= 0 || p.Port > 65535)
            {
                p.Port = LaunchProfile.DefaultPort;
            }
            if (p.ContextSize <= 0)
            {
                p.ContextSize = LaunchProfile.DefaultContextSize;
            }
            if (string.IsNullOrEmpty(p.Device))
            {
                p.Device = LaunchProfile.DefaultDevice;
            }
            if (string.IsNullOrEmpty(p.KvCacheType))
            {
                p.KvCacheType = LaunchProfile.DefaultKvCacheType;
            }
            if (string.IsNullOrEmpty(p.Reasoning))
            {
                p.Reasoning = LaunchProfile.DefaultReasoning;
            }
            if (string.IsNullOrEmpty(p.FlashAttn))
            {
                p.FlashAttn = LaunchProfile.DefaultFlashAttn;
            }
            if (p.UbatchSize <= 0)
            {
                p.UbatchSize = LaunchProfile.DefaultUbatchSize;
            }
            if (string.IsNullOrEmpty(p.SpecType))
            {
                p.SpecType = LaunchProfile.DefaultSpecType;
            }
            if (string.IsNullOrEmpty(p.DraftKvType))
            {
                p.DraftKvType = LaunchProfile.DefaultDraftKvType;
            }
            if (p.ExtraArgs == null)
            {
                p.ExtraArgs = "";
            }
            if (p.ThreadsBatch == 0)
            {
                p.ThreadsBatch = -1;
            }
            if (p.ProcessPrio == null)
            {
                p.ProcessPrio = "";
            }
            // 舊版設定檔沒有這些欄位，反序列化後會是 0；補上建議值以維持相容。
            if (p.CacheRam == 0)
            {
                p.CacheRam = LaunchProfile.DefaultCacheRam;
            }
            if (p.Parallel <= 0)
            {
                p.Parallel = LaunchProfile.DefaultParallel;
            }
            if (string.IsNullOrEmpty(p.ChatTemplateFile))
            {
                p.ChatTemplateFile = LaunchProfile.DefaultChatTemplateFile;
            }
        }
    }
}
