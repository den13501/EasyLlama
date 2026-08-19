using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace LlamaVulkanLauncher
{
    /// <summary>
    /// 解析 llama-server --help，記錄這顆執行檔實際支援哪些旗標。
    /// llama.cpp 更新頻繁（例如 --no-mmap 已被 --load-mode 取代），
    /// 以實際輸出為準才不會在升級後突然組出無效的命令列。
    /// </summary>
    internal sealed class ServerCapabilities
    {
        private readonly HashSet<string> _flags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>沒有成功讀到 --help 時為 false，此時一律沿用使用者設定不做過濾。</summary>
        public bool Known { get; private set; }

        public string ExePath { get; private set; }

        public static ServerCapabilities Unknown(string exePath)
        {
            ServerCapabilities caps = new ServerCapabilities();
            caps.ExePath = exePath;
            caps.Known = false;
            return caps;
        }

        /// <summary>
        /// 查詢旗標是否可用。未取得 --help 時一律回報 true，維持原本行為。
        /// </summary>
        public bool Has(string flag)
        {
            if (!Known || string.IsNullOrEmpty(flag))
            {
                return true;
            }
            return _flags.Contains(flag);
        }

        public int FlagCount
        {
            get { return _flags.Count; }
        }

        public static ServerCapabilities Detect(string llamaServerPath, int timeoutMs)
        {
            ServerCapabilities caps = Unknown(llamaServerPath);
            if (string.IsNullOrWhiteSpace(llamaServerPath) || !File.Exists(llamaServerPath))
            {
                return caps;
            }

            string text = RunHelp(llamaServerPath, timeoutMs);
            if (string.IsNullOrEmpty(text))
            {
                return caps;
            }

            caps.Parse(text);
            caps.Known = caps._flags.Count > 0;
            return caps;
        }

        public static void DetectAsync(string llamaServerPath, int timeoutMs, Action<ServerCapabilities> callback)
        {
            if (callback == null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                ServerCapabilities caps;
                try
                {
                    caps = Detect(llamaServerPath, timeoutMs);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("偵測伺服器參數失敗：" + ex.Message);
                    caps = Unknown(llamaServerPath);
                }
                callback(caps);
            });
        }

        /// <summary>
        /// 從說明文字取出所有長短旗標。--help 每個項目可能列出多個別名，
        /// 例如「--mmap, --no-mmap」或「-lm, --load-mode MODE」，全部都要收錄。
        /// </summary>
        internal void Parse(string helpText)
        {
            if (string.IsNullOrEmpty(helpText))
            {
                return;
            }

            for (int i = 0; i < helpText.Length; i++)
            {
                if (helpText[i] != '-')
                {
                    continue;
                }
                // 只從字首開始擷取，避免把 "q4_0" 這類值中的減號誤判成旗標。
                if (i > 0 && !char.IsWhiteSpace(helpText[i - 1]) && helpText[i - 1] != ',' && helpText[i - 1] != '`')
                {
                    continue;
                }

                int j = i;
                while (j < helpText.Length && helpText[j] == '-')
                {
                    j++;
                }
                int start = j;
                while (j < helpText.Length && (char.IsLetterOrDigit(helpText[j]) || helpText[j] == '-' || helpText[j] == '_'))
                {
                    j++;
                }
                if (j <= start)
                {
                    i = j;
                    continue;
                }

                string name = helpText.Substring(i, j - i).TrimEnd('-');
                if (name.Length > 2)
                {
                    _flags.Add(name);
                }
                i = j - 1;
            }
        }

        private static string RunHelp(string exePath, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exePath;
            psi.Arguments = "--help";
            psi.WorkingDirectory = Path.GetDirectoryName(exePath);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                StringBuilder output = new StringBuilder();
                DataReceivedEventHandler collect = delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (output)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("結束 --help 行程失敗：" + ex.Message);
                    }
                }

                Thread.Sleep(50);
                lock (output)
                {
                    return output.ToString();
                }
            }
        }
    }
}