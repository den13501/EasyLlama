using System;
using System.Threading;
using System.Windows.Forms;

namespace LlamaVulkanLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 必須在建立任何行程之前處理，之後啟動的 llama-server 才會繼承到設定。
            VulkanFix.ApplyToCurrentProcess();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
            Application.Run(new MainForm());
        }

        private static void OnUiException(object sender, ThreadExceptionEventArgs e)
        {
            ShowCrash(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowCrash(e.ExceptionObject as Exception);
        }

        private static void ShowCrash(Exception ex)
        {
            string message = ex != null ? ex.ToString() : "未知錯誤";
            try
            {
                MessageBox.Show(message, "EasyLlama 發生錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception dialogEx)
            {
                // 連錯誤對話框都顯示不了，只能寫到偵錯輸出。
                System.Diagnostics.Debug.WriteLine("顯示錯誤對話框失敗：" + dialogEx.Message);
                System.Diagnostics.Debug.WriteLine(message);
            }
        }
    }
}
