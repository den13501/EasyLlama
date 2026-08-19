using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace LlamaVulkanLauncher
{
    /// <summary>
    /// 集中管理介面配色。預設為內建的淺色主題，
    /// 若 config\theme.xml 存在則以其中的設定覆蓋，不需重新編譯即可換色。
    /// </summary>
    internal static class Theme
    {
        private static readonly Dictionary<string, Color> Colors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        static Theme()
        {
            LoadDefaults();
            LoadOverrides();
        }

        public static Color Header { get { return Get("Header"); } }
        public static Color HeaderText { get { return Get("HeaderText"); } }
        public static Color HeaderSubText { get { return Get("HeaderSubText"); } }
        public static Color Start { get { return Get("Start"); } }
        public static Color Stop { get { return Get("Stop"); } }
        public static Color Web { get { return Get("Web"); } }
        public static Color Ok { get { return Get("Ok"); } }
        public static Color Bad { get { return Get("Bad"); } }
        public static Color Muted { get { return Get("Muted"); } }
        public static Color Window { get { return Get("Window"); } }
        public static Color Panel { get { return Get("Panel"); } }
        public static Color LogBack { get { return Get("LogBack"); } }
        public static Color LogText { get { return Get("LogText"); } }
        /// <summary>伺服器執行中的狀態文字色。</summary>
        public static Color Running { get { return Get("Running"); } }
        /// <summary>模型載入中的狀態文字色。</summary>
        public static Color Loading { get { return Get("Loading"); } }

        private static void LoadDefaults()
        {
            Colors["Header"] = Color.FromArgb(28, 33, 40);
            Colors["HeaderText"] = Color.White;
            Colors["HeaderSubText"] = Color.FromArgb(168, 178, 190);
            Colors["Start"] = Color.FromArgb(46, 160, 67);
            Colors["Stop"] = Color.FromArgb(207, 73, 73);
            Colors["Web"] = Color.FromArgb(56, 66, 80);
            Colors["Ok"] = Color.FromArgb(26, 127, 55);
            Colors["Bad"] = Color.FromArgb(207, 73, 73);
            Colors["Muted"] = Color.FromArgb(110, 118, 129);
            Colors["Window"] = Color.FromArgb(244, 246, 248);
            Colors["Panel"] = Color.White;
            Colors["LogBack"] = Color.FromArgb(22, 27, 34);
            Colors["LogText"] = Color.FromArgb(201, 209, 217);
            Colors["Running"] = Color.FromArgb(63, 185, 80);
            Colors["Loading"] = Color.FromArgb(227, 179, 65);
        }

        public static Color Get(string key)
        {
            Color value;
            if (!string.IsNullOrEmpty(key) && Colors.TryGetValue(key, out value))
            {
                return value;
            }
            return Color.Black;
        }

        public static string GetFilePath()
        {
            return Path.Combine(ProfileStore.GetDirectory(), "theme.xml");
        }

        private static void LoadOverrides()
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNodeList nodes = doc.SelectNodes("/Theme/Color");
                if (nodes == null)
                {
                    return;
                }

                foreach (XmlNode node in nodes)
                {
                    XmlElement element = node as XmlElement;
                    if (element == null)
                    {
                        continue;
                    }
                    string key = element.GetAttribute("key");
                    Color parsed;
                    if (!string.IsNullOrEmpty(key) && TryParseColor(element.GetAttribute("value"), out parsed))
                    {
                        Colors[key] = parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("讀取佈景設定失敗：" + ex.Message);
            }
        }

        /// <summary>支援 #RRGGBB 與 #AARRGGBB 兩種寫法。</summary>
        internal static bool TryParseColor(string text, out Color color)
        {
            color = Color.Black;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string v = text.Trim().TrimStart('#');
            uint raw;
            if (!uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw))
            {
                return false;
            }

            if (v.Length == 6)
            {
                color = Color.FromArgb(255, (int)((raw >> 16) & 0xFF),
                    (int)((raw >> 8) & 0xFF), (int)(raw & 0xFF));
                return true;
            }
            if (v.Length == 8)
            {
                color = Color.FromArgb((int)((raw >> 24) & 0xFF), (int)((raw >> 16) & 0xFF),
                    (int)((raw >> 8) & 0xFF), (int)(raw & 0xFF));
                return true;
            }
            return false;
        }

        /// <summary>匯出目前配色成範本，方便使用者照著改自己的主題。</summary>
        public static void WriteTemplate(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<!-- 顏色格式 #RRGGBB 或 #AARRGGBB，改完存檔重開程式即可套用 -->");
            sb.AppendLine("<Theme>");
            foreach (KeyValuePair<string, Color> pair in Colors)
            {
                sb.AppendLine("  <Color key=\"" + pair.Key + "\" value=\"" + ToHex(pair.Value) + "\" />");
            }
            sb.AppendLine("</Theme>");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string ToHex(Color color)
        {
            return "#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
                + color.G.ToString("X2", CultureInfo.InvariantCulture)
                + color.B.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}