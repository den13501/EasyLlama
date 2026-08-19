using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace LlamaVulkanLauncher
{
    /// <summary>
    /// 介面文字集中管理。內建繁體中文，
    /// 另可在 config\lang\ 放入語言檔（例如 en-US.xml）翻譯成其他語言，不需重新編譯。
    /// </summary>
    internal static class Strings
    {
        private static readonly Dictionary<string, string> Texts =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>目前套用的語言代碼，例如 zh-TW。</summary>
        public static string CurrentLanguage { get; private set; }

        /// <summary>內建語言，也是找不到翻譯時的後備。</summary>
        public const string DefaultLanguage = "zh-TW";

        static Strings()
        {
            CurrentLanguage = DefaultLanguage;
            DefaultTexts.Fill(Texts);
            LoadExternal();
        }

        /// <summary>
        /// 取得翻譯文字。找不到 key 時直接回傳 key 本身，方便發現漏翻的項目。
        /// </summary>
        public static string Get(string key)
        {
            string value;
            if (!string.IsNullOrEmpty(key) && Texts.TryGetValue(key, out value))
            {
                return value;
            }
            return key;
        }

        /// <summary>取得文字後代入參數，用法同 string.Format。</summary>
        public static string Format(string key, params object[] args)
        {
            string text = Get(key);
            if (args == null || args.Length == 0)
            {
                return text;
            }
            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, args);
            }
            catch (FormatException ex)
            {
                Debug.WriteLine("套用文字參數失敗：" + key + " / " + ex.Message);
                return text;
            }
        }

        public static string GetLanguageDirectory()
        {
            return Path.Combine(ProfileStore.GetDirectory(), "lang");
        }

        /// <summary>列出 lang 資料夾中可用的語言代碼。</summary>
        public static string[] ListAvailable()
        {
            List<string> list = new List<string>();
            list.Add(DefaultLanguage);
            try
            {
                string dir = GetLanguageDirectory();
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.xml");
                    for (int i = 0; i < files.Length; i++)
                    {
                        string code = Path.GetFileNameWithoutExtension(files[i]);
                        if (!string.IsNullOrEmpty(code) && !list.Contains(code))
                        {
                            list.Add(code);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("列舉語言檔失敗：" + ex.Message);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 依 config\language.txt 指定的語言載入翻譯；沒有指定時嘗試比對系統語言。
        /// 只覆蓋語言檔中有寫到的 key，其餘沿用內建中文，因此翻譯不完整也能正常使用。
        /// </summary>
        private static void LoadExternal()
        {
            try
            {
                string code = ReadPreferredLanguage();
                if (string.IsNullOrEmpty(code)
                    || string.Equals(code, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string path = Path.Combine(GetLanguageDirectory(), code + ".xml");
                if (!File.Exists(path))
                {
                    return;
                }

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNodeList nodes = doc.SelectNodes("/Strings/String");
                if (nodes == null)
                {
                    return;
                }

                int applied = 0;
                foreach (XmlNode node in nodes)
                {
                    XmlElement element = node as XmlElement;
                    if (element == null)
                    {
                        continue;
                    }
                    string key = element.GetAttribute("key");
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }
                    string value = element.HasAttribute("value")
                        ? element.GetAttribute("value")
                        : element.InnerText;
                    if (!string.IsNullOrEmpty(value))
                    {
                        Texts[key] = value;
                        applied++;
                    }
                }

                if (applied > 0)
                {
                    CurrentLanguage = code;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("載入語言檔失敗：" + ex.Message);
            }
        }

        private static string ReadPreferredLanguage()
        {
            string settingPath = Path.Combine(ProfileStore.GetDirectory(), "language.txt");
            if (File.Exists(settingPath))
            {
                try
                {
                    string code = File.ReadAllText(settingPath).Trim();
                    if (!string.IsNullOrEmpty(code))
                    {
                        return code;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("讀取語言設定失敗：" + ex.Message);
                }
            }

            // 沒有明確指定時，若剛好有對應系統語言的翻譯檔就採用。
            return CultureInfo.CurrentUICulture.Name;
        }

        /// <summary>
        /// 匯出目前所有文字成語言範本，讓其他人可以照著翻譯後提交。
        /// </summary>
        public static void WriteTemplate(string path)
        {
            List<string> keys = new List<string>(Texts.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<!-- 翻譯方式：只修改 value 的內容，key 請保持不變。 -->");
            sb.AppendLine("<!-- 存成 config\\lang\\<語言代碼>.xml，再於 config\\language.txt 寫入該代碼即可套用。 -->");
            sb.AppendLine("<Strings>");
            for (int i = 0; i < keys.Count; i++)
            {
                sb.AppendLine("  <String key=\"" + Escape(keys[i]) + "\" value=\""
                    + Escape(Texts[keys[i]]) + "\" />");
            }
            sb.AppendLine("</Strings>");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("&", "&amp;").Replace("<", "&lt;")
                .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}