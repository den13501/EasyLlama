using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LlamaVulkanLauncher
{
    /// <summary>
    /// 有些系統的 Vulkan loader 註冊表（HKLM\SOFTWARE\Khronos\Vulkan\Drivers）會遺失，
    /// 導致 llama-server 明明有顯卡卻偵測不到，整個模型退回 CPU 執行。
    /// 這裡改從顯示卡驅動註冊表找出 ICD 檔，啟動時用 VK_ICD_FILENAMES 指給子行程，
    /// 不必修改系統設定也不需要重裝驅動。
    /// </summary>
    internal static class VulkanFix
    {
        private const string DisplayClassKey =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        private const string LoaderKey = @"SOFTWARE\Khronos\Vulkan\Drivers";

        /// <summary>Vulkan loader 是否已能自行找到 ICD 設定。</summary>
        public static bool LoaderRegistryExists()
        {
            try
            {
                using (RegistryKey key = RegistryKey
                    .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(LoaderKey))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    return key.GetValueNames().Length > 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("檢查 Vulkan 註冊表失敗：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 從顯示卡驅動設定取出各家的 Vulkan ICD 檔路徑（獨顯排在前面）。
        /// </summary>
        public static string[] FindIcdFiles()
        {
            List<string> discrete = new List<string>();
            List<string> others = new List<string>();

            try
            {
                using (RegistryKey root = RegistryKey
                    .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(DisplayClassKey))
                {
                    if (root == null)
                    {
                        return new string[0];
                    }

                    string[] subKeys = root.GetSubKeyNames();
                    for (int i = 0; i < subKeys.Length; i++)
                    {
                        using (RegistryKey sub = root.OpenSubKey(subKeys[i]))
                        {
                            if (sub == null)
                            {
                                continue;
                            }

                            string icd = sub.GetValue("VulkanDriverName") as string;
                            if (string.IsNullOrEmpty(icd))
                            {
                                string[] many = sub.GetValue("VulkanDriverName") as string[];
                                if (many != null && many.Length > 0)
                                {
                                    icd = many[0];
                                }
                            }
                            if (string.IsNullOrEmpty(icd) || !File.Exists(icd))
                            {
                                continue;
                            }

                            string desc = (sub.GetValue("DriverDesc") as string) ?? "";
                            if (IsDiscrete(desc))
                            {
                                discrete.Add(icd);
                            }
                            else
                            {
                                others.Add(icd);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("搜尋 Vulkan ICD 失敗：" + ex.Message);
            }

            discrete.AddRange(others);
            return discrete.ToArray();
        }

        private static bool IsDiscrete(string driverDesc)
        {
            if (string.IsNullOrEmpty(driverDesc))
            {
                return false;
            }
            return driverDesc.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0
                || driverDesc.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0
                || driverDesc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0
                || driverDesc.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 需要時回傳 VK_ICD_FILENAMES 的值；系統本身正常則回傳空字串。
        /// </summary>
        public static string BuildIcdOverride()
        {
            if (LoaderRegistryExists())
            {
                return "";
            }

            string[] files = FindIcdFiles();
            if (files.Length == 0)
            {
                return "";
            }

            // 有些內顯的 ICD 會讓 Vulkan loader 直接失敗，連帶讓獨顯也無法使用，
            // 因此只挑出獨立顯示卡的設定；找不到才退回完整清單。
            List<string> discreteOnly = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                if (IsDiscreteIcdPath(files[i]))
                {
                    discreteOnly.Add(files[i]);
                }
            }

            if (discreteOnly.Count > 0)
            {
                return string.Join(";", discreteOnly.ToArray());
            }
            return string.Join(";", files);
        }

        /// <summary>由 ICD 檔路徑判斷是否屬於獨立顯示卡的驅動。</summary>
        private static bool IsDiscreteIcdPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            string name = path.ToLowerInvariant();
            return name.Contains("amdvlk") || name.Contains("amd-vulkan")
                || name.Contains("nv-vk") || name.Contains("nvoglv");
        }

        /// <summary>上次套用的設定值，供介面顯示狀態。</summary>
        public static string AppliedIcd { get; private set; }

        /// <summary>
        /// 把 ICD 路徑設進本行程的環境變數，之後啟動的 llama-server 會自動繼承。
        /// 使用者若已自行設定則尊重原設定，不覆寫。
        /// </summary>
        public static bool ApplyToCurrentProcess()
        {
            AppliedIcd = "";
            try
            {
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VK_ICD_FILENAMES")))
                {
                    return false;
                }

                string value = BuildIcdOverride();
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                Environment.SetEnvironmentVariable("VK_ICD_FILENAMES", value);
                AppliedIcd = value;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("套用 Vulkan ICD 設定失敗：" + ex.Message);
                return false;
            }
        }
    }
}