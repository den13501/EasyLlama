using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LlamaVulkanLauncher
{
    internal static class CommandBuilder
    {
        public static string BuildArguments(LaunchProfile profile)
        {
            return BuildArguments(profile, null);
        }

        public static string BuildArguments(LaunchProfile profile, ServerCapabilities caps)
        {
            return string.Join(" ", CollectTokens(profile, caps).ToArray());
        }

        private static System.Collections.Generic.List<string> CollectTokens(LaunchProfile profile)
        {
            return CollectTokens(profile, null);
        }

        private static System.Collections.Generic.List<string> CollectTokens(LaunchProfile profile, ServerCapabilities caps)
        {
            System.Collections.Generic.List<string> tokens = new System.Collections.Generic.List<string>();
            if (profile == null)
            {
                return tokens;
            }

            AddPair(tokens, "--model", Quote(profile.ModelPath));

            if (profile.UseMmproj && !string.IsNullOrWhiteSpace(profile.MmprojPath))
            {
                AddPair(tokens, "--mmproj", Quote(profile.MmprojPath));
            }

            if (profile.UseChatTemplate && !string.IsNullOrWhiteSpace(profile.ChatTemplateFile))
            {
                AddPair(tokens, "--chat-template-file", Quote(profile.ChatTemplateFile));
            }

            if (!string.IsNullOrWhiteSpace(profile.Device))
            {
                AddPair(tokens, "--device", profile.Device.Trim());
            }

            AddPair(tokens, "--ctx-size", profile.ContextSize.ToString(CultureInfo.InvariantCulture));
            AddPair(tokens, "--n-gpu-layers", profile.GpuLayers.ToString(CultureInfo.InvariantCulture));

            if (profile.EnableSpeculative && !string.IsNullOrWhiteSpace(profile.SpecType)
                && !string.Equals(profile.SpecType, "none", StringComparison.OrdinalIgnoreCase))
            {
                AddPair(tokens, "--spec-type", profile.SpecType.Trim());
                AddPair(tokens, "--spec-draft-n-max", profile.SpecDraftNMax.ToString(CultureInfo.InvariantCulture));
                AddPair(tokens, "--spec-draft-p-min", profile.SpecDraftPMin.ToString("0.##", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(profile.DraftKvType))
                {
                    AddPair(tokens, "--spec-draft-type-k", profile.DraftKvType.Trim());
                    AddPair(tokens, "--spec-draft-type-v", profile.DraftKvType.Trim());
                }
            }

            AddPair(tokens, "-ub", profile.UbatchSize.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(profile.FlashAttn))
            {
                AddPair(tokens, "-fa", NormalizeFlashAttn(profile.FlashAttn));
            }

            if (profile.NoMmap)
            {
                // 新版 llama.cpp 已把 --no-mmap 標記為 DEPRECATED，改用 --load-mode。
                // 偵測得到新旗標就用新的，否則沿用舊寫法以相容舊版執行檔。
                if (Supports(caps, "--load-mode"))
                {
                    AddPair(tokens, "--load-mode", "none");
                }
                else
                {
                    tokens.Add("--no-mmap");
                }
            }

            if (!string.IsNullOrWhiteSpace(profile.Reasoning))
            {
                AddPair(tokens, "--reasoning", profile.Reasoning.Trim());
            }

            if (profile.ReasoningPreserve && Supports(caps, "--reasoning-preserve"))
            {
                tokens.Add("--reasoning-preserve");
            }

            // --cache-ram：-1 不限制、0 停用，都是有效值，因此只在小於 -1 時才省略。
            if (profile.CacheRam >= -1 && Supports(caps, "--cache-ram"))
            {
                AddPair(tokens, "--cache-ram", profile.CacheRam.ToString(CultureInfo.InvariantCulture));
            }

            if (profile.Parallel > 0 && Supports(caps, "--parallel"))
            {
                AddPair(tokens, "--parallel", profile.Parallel.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(profile.KvCacheType))
            {
                AddPair(tokens, "--cache-type-k", profile.KvCacheType.Trim());
                AddPair(tokens, "--cache-type-v", profile.KvCacheType.Trim());
            }

            if (profile.UseMmproj && profile.ImageMinTokens > 0)
            {
                AddPair(tokens, "--image-min-tokens", profile.ImageMinTokens.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(profile.Host))
            {
                AddPair(tokens, "--host", profile.Host.Trim());
            }

            AddPair(tokens, "--port", profile.Port.ToString(CultureInfo.InvariantCulture));

            if (profile.Threads >= 0)
            {
                AddPair(tokens, "--threads", profile.Threads.ToString(CultureInfo.InvariantCulture));
            }

            if (profile.ThreadsBatch >= 0)
            {
                AddPair(tokens, "--threads-batch", profile.ThreadsBatch.ToString(CultureInfo.InvariantCulture));
            }

            string prio = SystemResources.NormalizePrio(profile.ProcessPrio);
            if (!string.IsNullOrEmpty(prio))
            {
                AddPair(tokens, "--prio", SystemResources.MapPrioToken(prio).ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(profile.ExtraArgs))
            {
                tokens.Add(profile.ExtraArgs.Trim());
            }

            return tokens;
        }

        private static void AddPair(System.Collections.Generic.List<string> tokens, string name, string value)
        {
            tokens.Add(name + " " + value);
        }

        /// <summary>
        /// 尚未偵測到能力資訊時一律視為支援，避免因偵測失敗而漏掉使用者要的參數。
        /// </summary>
        private static bool Supports(ServerCapabilities caps, string flag)
        {
            return caps == null || caps.Has(flag);
        }

        public static string BuildFullCommand(LaunchProfile profile)
        {
            return BuildFullCommand(profile, null);
        }

        public static string BuildFullCommand(LaunchProfile profile, ServerCapabilities caps)
        {
            if (profile == null)
            {
                return "";
            }

            return Quote(profile.LlamaServerPath) + " " + BuildArguments(profile, caps);
        }

        public static string BuildBat(LaunchProfile profile)
        {
            return BuildBat(profile, null);
        }

        public static string BuildBat(LaunchProfile profile, ServerCapabilities caps)
        {
            if (profile == null)
            {
                return "";
            }

            string llamaDir = "";
            if (!string.IsNullOrWhiteSpace(profile.LlamaServerPath))
            {
                llamaDir = Path.GetDirectoryName(profile.LlamaServerPath);
            }
            if (string.IsNullOrEmpty(llamaDir))
            {
                llamaDir = @"C:\ai-lab\llama-vulkan";
            }

            string[] parts = CollectTokens(profile, caps).ToArray();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal");
            sb.AppendLine("title " + SanitizeTitle(profile.Name));
            sb.AppendLine();
            sb.AppendLine("cd /d " + Quote(llamaDir));
            sb.AppendLine();
            sb.Append("llama-server.exe");
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(" ^");
                sb.AppendLine();
                sb.Append("  ");
                sb.Append(parts[i]);
            }
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("pause");
            return sb.ToString();
        }

        public static string[] Validate(LaunchProfile profile)
        {
            System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();
            if (profile == null)
            {
                errors.Add("沒有設定檔。");
                return errors.ToArray();
            }

            if (string.IsNullOrWhiteSpace(profile.LlamaServerPath) || !File.Exists(profile.LlamaServerPath))
            {
                errors.Add("找不到 llama-server.exe：" + profile.LlamaServerPath);
            }

            if (string.IsNullOrWhiteSpace(profile.ModelPath) || !File.Exists(profile.ModelPath))
            {
                errors.Add("找不到模型：" + profile.ModelPath);
            }

            if (profile.UseMmproj)
            {
                if (string.IsNullOrWhiteSpace(profile.MmprojPath) || !File.Exists(profile.MmprojPath))
                {
                    errors.Add("找不到視覺投影檔 mmproj：" + profile.MmprojPath);
                }
            }

            if (profile.Port < 1 || profile.Port > 65535)
            {
                errors.Add("連接埠必須介於 1 到 65535。");
            }

            if (profile.ContextSize < 256)
            {
                errors.Add("上下文長度過小。");
            }

            if (profile.UseChatTemplate)
            {
                if (string.IsNullOrWhiteSpace(profile.ChatTemplateFile) || !File.Exists(profile.ChatTemplateFile))
                {
                    errors.Add("找不到聊天模板檔：" + profile.ChatTemplateFile);
                }
            }

            return errors.ToArray();
        }

        /// <summary>
        /// 不會擋下啟動、但很可能造成怪異行為的設定，交由介面提示使用者。
        /// </summary>
        public static string[] Warn(LaunchProfile profile)
        {
            System.Collections.Generic.List<string> warnings = new System.Collections.Generic.List<string>();
            if (profile == null)
            {
                return warnings.ToArray();
            }

            if (profile.UseChatTemplate && LooksUncensored(profile.ModelPath))
            {
                warnings.Add("這個模型檔名含 Uncensored（JonathanColetti 版）。該版 GGUF 內建自己的聊天模板，"
                    + "外掛 froggeric 模板會讓模型吐出模板原始碼、看圖也會 HTTP 400。建議取消勾選聊天模板。");
            }

            if (IsQuantizedCache(profile.KvCacheType) && IsFlashAttnOff(profile.FlashAttn))
            {
                warnings.Add("KV 快取用了量化型別（" + profile.KvCacheType + "），但 Flash Attention 是關閉的。"
                    + "量化 KV 需要 -fa 才能正常運作，建議把 Flash Attention 設為 on 或 auto。");
            }

            if (profile.EnableSpeculative && profile.SpecDraftNMax > 3)
            {
                warnings.Add("推測解碼 n-max 超過 3，實測反而會比不開更慢。日常對話建議 1、寫程式建議 2~3。");
            }

            return warnings.ToArray();
        }

        private static bool LooksUncensored(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return false;
            }
            return modelPath.IndexOf("uncensored", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsQuantizedCache(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }
            string v = type.Trim();
            return v.StartsWith("q", StringComparison.OrdinalIgnoreCase)
                || v.StartsWith("iq", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFlashAttnOff(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            return string.Equals(NormalizeFlashAttn(value), "off", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 依 Windows 命令列規則加上雙引號：內含的引號要跳脫，
        /// 引號前的反斜線也必須加倍，否則會被視為跳脫字元。
        /// </summary>
        internal static string Quote(string value)
        {
            if (value == null)
            {
                value = "";
            }

            StringBuilder sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            int backslashes = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    // 引號前的反斜線要加倍，再跳脫引號本身。
                    sb.Append('\\', backslashes * 2 + 1);
                    backslashes = 0;
                    sb.Append('"');
                    continue;
                }

                sb.Append('\\', backslashes);
                backslashes = 0;
                sb.Append(c);
            }

            // 結尾的反斜線會跳脫收尾的引號，因此也要加倍。
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        private static string NormalizeFlashAttn(string value)
        {
            string v = value.Trim();
            if (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
            {
                return "on";
            }
            if (v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
            {
                return "off";
            }
            return v;
        }

        private static string SanitizeTitle(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "llama-server";
            }
            return name.Replace("&", "").Replace("|", "").Replace(">", "").Replace("<", "");
        }

    }
}
