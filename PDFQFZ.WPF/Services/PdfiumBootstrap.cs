using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFQFZ.WPF.Services
{
    /// <summary>
    /// PDFium 引擎引导器。
    /// 背景：软件内嵌的 pdfium.dll 此前由 Costura 在运行时解压到系统临时文件夹（%TEMP%）再加载，
    /// 部分电脑（杀软拦截、临时目录被清理或权限异常）会出现
    /// “PdfiumViewer.NativeMethods 的类型初始值设定项引发异常”，表现为拖入 PDF 时加载失败。
    /// 本类改为：程序启动时把嵌入的 pdfium.dll 提取到 EXE 同目录，并通过 PdfiumResolver.Resolve
    /// 明确指定加载路径，完全不依赖系统临时文件夹；同时写入 pdfium_diag.log 诊断日志，
    /// 便于在异常电脑上定位底层原因。
    /// </summary>
    internal static class PdfiumBootstrap
    {
        private const string DllName = "pdfium.dll";
        private const string DiagFileName = "pdfium_diag.log";

        // 嵌入资源名（与 PDFQFZ.WPF.csproj 中的 LogicalName 一致）
        private const string ResourceX64 = "PDFQFZ.WPF.assets.pdfium-x64.dll";
        private const string ResourceX86 = "PDFQFZ.WPF.assets.pdfium-x86.dll";

        private static readonly object syncRoot = new object();
        private static bool ready;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>程序启动时调用：提取引擎到 EXE 同目录并注册 PdfiumViewer 的加载路径。</summary>
        public static void EnsurePdfiumReady()
        {
            if (ready)
            {
                return;
            }

            lock (syncRoot)
            {
                if (ready)
                {
                    return;
                }

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string dllPath = Path.Combine(baseDir, DllName);
                    string resourceName = Environment.Is64BitProcess ? ResourceX64 : ResourceX86;

                    WriteDiag("进程位数: " + (Environment.Is64BitProcess ? "x64" : "x86")
                        + " | 程序目录: " + baseDir);

                    // 1) 从嵌入资源读取 pdfium.dll
                    byte[] data = ReadEmbeddedResource(resourceName);
                    if (data == null || data.Length == 0)
                    {
                        WriteDiag("错误：嵌入资源不存在或为空：" + resourceName);
                        return;
                    }
                    WriteDiag("嵌入资源读取成功：" + resourceName + "（" + data.Length + " 字节）");

                    // 2) 写入 EXE 同目录（已存在且大小一致则跳过，避免每次启动都重写）
                    bool needWrite = !File.Exists(dllPath);
                    if (!needWrite)
                    {
                        try
                        {
                            needWrite = new FileInfo(dllPath).Length != data.Length;
                        }
                        catch (Exception ex)
                        {
                            WriteDiag("检查已有文件失败（将重新写入）: " + ex.Message);
                            needWrite = true;
                        }
                    }
                    if (needWrite)
                    {
                        try
                        {
                            File.WriteAllBytes(dllPath, data);
                            WriteDiag("已提取引擎到: " + dllPath);
                        }
                        catch (Exception ex)
                        {
                            WriteDiag("错误：写入引擎文件失败: " + ex.Message);
                            return;
                        }
                    }
                    else
                    {
                        WriteDiag("引擎已存在于: " + dllPath + "（跳过写入）");
                    }

                    // 3) 用完整路径预加载到进程（PdfiumViewer 与 PdfTextSearcher 都能复用已加载模块）
                    IntPtr handle = LoadLibrary(dllPath);
                    if (handle == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        WriteDiag("错误：LoadLibrary 预加载失败，Win32错误码=" + err
                            + "（0x" + err.ToString("X8") + "）");
                    }
                    else
                    {
                        WriteDiag("LoadLibrary 预加载成功，句柄=0x" + handle.ToInt64().ToString("X"));
                    }

                    // 4) 注册 PdfiumViewer 的解析事件：明确告诉引擎“用这个文件”
                    //    （在 PdfiumViewer 首次使用之前必须注册完成，本方法由 App.OnStartup 最早调用）
                    try
                    {
                        PdfiumViewer.PdfiumResolver.Resolve += (s, e) =>
                        {
                            e.PdfiumFileName = dllPath;
                        };
                        WriteDiag("PdfiumResolver.Resolve 事件已注册 -> " + dllPath);
                    }
                    catch (Exception ex)
                    {
                        WriteDiag("错误：注册 PdfiumResolver.Resolve 失败: " + ex.Message);
                    }

                    WriteDiag("PdfiumBootstrap 初始化完成。");
                }
                catch (Exception ex)
                {
                    WriteDiag("PdfiumBootstrap 异常: " + BuildExceptionChain(ex));
                }
                finally
                {
                    ready = true;
                }
            }
        }

        /// <summary>从当前程序集读取嵌入资源。</summary>
        private static byte[] ReadEmbeddedResource(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                byte[] buffer = new byte[stream.Length];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }

                return offset == buffer.Length ? buffer : null;
            }
        }

        /// <summary>递归拼接完整异常链（含 InnerException），便于定位底层原因。</summary>
        public static string BuildExceptionChain(Exception ex)
        {
            if (ex == null)
            {
                return "(无异常)";
            }

            StringBuilder sb = new StringBuilder();
            int depth = 0;
            for (Exception cur = ex; cur != null; cur = cur.InnerException, depth++)
            {
                sb.AppendLine(new string(' ', depth * 2) + "层级" + depth + ": [" + cur.GetType().FullName + "] " + cur.Message);
            }

            return sb.ToString();
        }

        /// <summary>把诊断信息写入 EXE 同目录的 pdfium_diag.log（UTF-8，追加模式）。</summary>
        public static void WriteDiag(string message)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DiagFileName);
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message
                    + Environment.NewLine;
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch
            {
                // 写日志失败不影响主流程
            }
        }
    }
}
