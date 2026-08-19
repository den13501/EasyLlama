using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace LlamaVulkanLauncher
{
    internal sealed class ServerProcess : IDisposable
    {
        private Process _process;
        private readonly object _sync = new object();

        public event Action<string> OutputReceived;
        public event Action<int> Exited;

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _process != null && !_process.HasExited;
                }
            }
        }

        public int ProcessId
        {
            get
            {
                lock (_sync)
                {
                    if (_process == null || _process.HasExited)
                    {
                        return 0;
                    }
                    return _process.Id;
                }
            }
        }

        public void Start(LaunchProfile profile)
        {
            Start(profile, null);
        }

        public void Start(LaunchProfile profile, ServerCapabilities caps)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            lock (_sync)
            {
                if (_process != null && !_process.HasExited)
                {
                    throw new InvalidOperationException("伺服器已在執行。");
                }

                string workDir = Path.GetDirectoryName(profile.LlamaServerPath);
                if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
                {
                    workDir = Environment.CurrentDirectory;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = profile.LlamaServerPath;
                psi.Arguments = CommandBuilder.BuildArguments(profile, caps);
                psi.WorkingDirectory = workDir;
                psi.UseShellExecute = profile.ShowConsole;
                psi.CreateNoWindow = !profile.ShowConsole;
                if (!profile.ShowConsole)
                {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.StandardOutputEncoding = Encoding.UTF8;
                    psi.StandardErrorEncoding = Encoding.UTF8;
                }

                Process process = new Process();
                process.StartInfo = psi;
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                if (!profile.ShowConsole)
                {
                    process.OutputDataReceived += OnOutput;
                    process.ErrorDataReceived += OnOutput;
                }

                if (!process.Start())
                {
                    process.Dispose();
                    throw new InvalidOperationException("無法啟動 llama-server。");
                }

                if (!profile.ShowConsole)
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }

                // 直接設定 Windows 行程優先權，效果等同 .bat 的 start /belownormal，
                // 而且不必依賴伺服器是否支援 --prio 旗標。
                ApplyPriority(process, profile.ProcessPrio);

                _process = process;
            }
        }

        /// <summary>
        /// 依設定檔的優先權選項調整行程優先度，讓前台辦公軟體維持流暢。
        /// </summary>
        private static void ApplyPriority(Process process, string prio)
        {
            string normalized = SystemResources.NormalizePrio(prio);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            ProcessPriorityClass target;
            if (string.Equals(normalized, "low", StringComparison.OrdinalIgnoreCase))
            {
                target = ProcessPriorityClass.BelowNormal;
            }
            else if (string.Equals(normalized, "medium", StringComparison.OrdinalIgnoreCase))
            {
                target = ProcessPriorityClass.AboveNormal;
            }
            else if (string.Equals(normalized, "high", StringComparison.OrdinalIgnoreCase))
            {
                target = ProcessPriorityClass.High;
            }
            else
            {
                target = ProcessPriorityClass.Normal;
            }

            try
            {
                process.PriorityClass = target;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("設定行程優先權失敗：" + ex.Message);
            }
        }

        public void Stop()
        {
            Process process;
            lock (_sync)
            {
                process = _process;
                _process = null;
            }

            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(4000);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("結束 llama-server 失敗：" + ex.Message);
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("釋放行程物件失敗：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 找出目前正在執行的 llama-server 行程（可能是上一次啟動器沒收乾淨留下的）。
        /// </summary>
        public static Process[] FindOrphanProcesses()
        {
            List<Process> list = new List<Process>();
            try
            {
                Process[] all = Process.GetProcessesByName("llama-server");
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && !all[i].HasExited)
                    {
                        list.Add(all[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("列舉 llama-server 行程失敗：" + ex.Message);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 結束所有 llama-server 行程，用於接管不是本次啟動的殘留服務。
        /// 回傳實際結束的數量。
        /// </summary>
        public static int StopAllServers()
        {
            Process[] list = FindOrphanProcesses();
            int stopped = 0;
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    if (!list[i].HasExited)
                    {
                        list[i].Kill();
                        list[i].WaitForExit(4000);
                        stopped++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("結束殘留行程失敗：" + ex.Message);
                }
                finally
                {
                    try
                    {
                        list[i].Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("釋放行程物件失敗：" + ex.Message);
                    }
                }
            }
            return stopped;
        }

        /// <summary>
        /// 在背景執行緒做健康檢查，完成後以 callback 回報結果，避免阻塞 UI 執行緒。
        /// </summary>
        public static void PingHealthAsync(string url, int timeoutMs, Action<bool> callback)
        {
            if (callback == null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try
                {
                    ok = TryPingHealth(url, timeoutMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("健康檢查失敗：" + ex.Message);
                }
                callback(ok);
            });
        }

        /// <summary>
        /// 在背景執行緒列舉裝置，完成後以 callback 回報結果，避免 UI 在等待期間凍結。
        /// </summary>
        public static void ListGpuDevicesAsync(string llamaServerPath, int timeoutMs, Action<GpuDevice[]> callback)
        {
            if (callback == null)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                GpuDevice[] devices;
                try
                {
                    devices = ListGpuDevices(llamaServerPath, timeoutMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("列舉裝置失敗：" + ex.Message);
                    devices = new GpuDevice[0];
                }
                callback(devices);
            });
        }

        public static bool TryPingHealth(string url, int timeoutMs)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.TrimEnd('/') + "/health");
                request.Method = "GET";
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string[] ListDevices(string llamaServerPath, int timeoutMs)
        {
            GpuDevice[] devices = ListGpuDevices(llamaServerPath, timeoutMs);
            string[] ids = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
            {
                ids[i] = devices[i].Id;
            }
            return ids;
        }

        public static GpuDevice[] ListGpuDevices(string llamaServerPath, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(llamaServerPath) || !File.Exists(llamaServerPath))
            {
                return new GpuDevice[0];
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = llamaServerPath;
            psi.Arguments = "--list-devices";
            psi.WorkingDirectory = Path.GetDirectoryName(llamaServerPath);
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
                process.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (output)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };
                process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (output)
                        {
                            output.AppendLine(e.Data);
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                    }
                }

                Thread.Sleep(50);
                string text;
                lock (output)
                {
                    text = output.ToString();
                }
                return ParseGpuDevices(text);
            }
        }

        internal static GpuDevice[] ParseGpuDevices(string text)
        {
            System.Collections.Generic.List<GpuDevice> list = new System.Collections.Generic.List<GpuDevice>();
            if (string.IsNullOrEmpty(text))
            {
                return list.ToArray();
            }

            string[] lines = text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("Vulkan", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("CUDA", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("Metal", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int colon = line.IndexOf(':');
                string id = colon > 0 ? line.Substring(0, colon).Trim() : line;
                if (id.Length == 0)
                {
                    continue;
                }
                bool exists = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (string.Equals(list[j].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    continue;
                }

                GpuDevice device = new GpuDevice();
                device.Id = id;
                device.Name = "";
                device.MemoryBytes = 0;
                if (colon > 0 && colon + 1 < line.Length)
                {
                    string rest = line.Substring(colon + 1).Trim();
                    device.MemoryBytes = ParseMib(rest);
                    int paren = rest.IndexOf('(');
                    device.Name = (paren > 0 ? rest.Substring(0, paren) : rest).Trim();
                }
                list.Add(device);
            }

            return list.ToArray();
        }

        private static long ParseMib(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            string[] tokens = text.Split(new char[] { ' ', '\t', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                if (!string.Equals(tokens[i + 1], "MiB", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tokens[i + 1], "MB", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tokens[i + 1], "GiB", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(tokens[i + 1], "GB", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                double value;
                if (!double.TryParse(tokens[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) || value <= 0)
                {
                    continue;
                }
                if (tokens[i + 1].StartsWith("G", StringComparison.OrdinalIgnoreCase))
                {
                    return (long)(value * 1024.0 * 1024.0 * 1024.0);
                }
                return (long)(value * 1024.0 * 1024.0);
            }
            return 0;
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            Action<string> handler = OutputReceived;
            if (handler != null)
            {
                handler(e.Data);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            int code = -1;
            try
            {
                Process process = sender as Process;
                if (process != null)
                {
                    code = process.ExitCode;
                }
            }
            catch (Exception)
            {
            }

            Action<int> handler = Exited;
            if (handler != null)
            {
                handler(code);
            }
        }

        public void Dispose()
        {
            Stop();
            lock (_sync)
            {
                if (_process != null)
                {
                    _process.Dispose();
                    _process = null;
                }
            }
        }
    }
}
