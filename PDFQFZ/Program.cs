using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PDFQFZ
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //单实例：只允许同时运行一个程序实例，重复启动时提示并退出
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "PDFQFZ_GG_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("程序已经在运行，请到已打开的窗口中操作。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1(args));
            }
        }
    }
}
