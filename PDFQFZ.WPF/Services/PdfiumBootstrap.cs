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
    /// 明确指定加载路径，完全不依赖系统临时文件夹。
    /// </summary>
    internal static class PdfiumBootstrap
    {
        private const string DllName = "pdfium.dll";

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

                    // 1) 从嵌入资源读取 pdfium.dll
                    byte[] data = ReadEmbeddedResource(resourceName);
                    if (data == null || data.Length == 0)
                    {
                        return;
                    }

                    // 2) 写入 EXE 同目录（已存在且大小一致则跳过，避免每次启动都重写）
                    bool needWrite = !File.Exists(dllPath);
                    if (!needWrite)
                    {
                        try
                        {
                            needWrite = new FileInfo(dllPath).Length != data.Length;
                        }
                        catch
                        {
                            needWrite = true;
                        }
                    }
                    if (needWrite)
                    {
                        try
                        {
                            File.WriteAllBytes(dllPath, data);
                        }
                        catch
                        {
                            return;
                        }
                    }

                    // 3) 用完整路径预加载到进程（PdfiumViewer 与 PdfTextSearcher 都能复用已加载模块）
                    IntPtr handle = LoadLibrary(dllPath);
                    if (handle != IntPtr.Zero)
                    {
                        // 4) 注册 PdfiumViewer 的解析事件：明确告诉引擎“用这个文件”
                        //    （在 PdfiumViewer 首次使用之前必须注册完成，本方法由 App.OnStartup 最早调用）
                        try
                        {
                            PdfiumViewer.PdfiumResolver.Resolve += (s, e) =>
                            {
                                e.PdfiumFileName = dllPath;
                            };
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
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
    }
}
