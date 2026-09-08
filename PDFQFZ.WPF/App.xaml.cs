using System;
using System.Threading;
using System.Windows;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 应用程序入口。单实例：只允许同时运行一个程序实例，重复启动时提示并退出。
    /// </summary>
    public partial class App : Application
    {
        private Mutex mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;
            mutex = new Mutex(true, "PDFQFZ_GG_SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("程序已经在运行，请到已打开的窗口中操作。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 在创建主窗口（首次使用 PDF 引擎）之前，先准备 pdfium.dll：
            // 提取到 EXE 同目录并注册 PdfiumViewer 的加载路径，避免依赖系统临时文件夹。
            PdfiumBootstrap.EnsurePdfiumReady();

            base.OnStartup(e);
            MainWindow = new MainWindow(e.Args);
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex != null)
            {
                try { mutex.ReleaseMutex(); } catch { /* 忽略 */ }
                mutex.Dispose();
                mutex = null;
            }
            base.OnExit(e);
        }
    }
}
