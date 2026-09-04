using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PDFQFZ.Library
{
    /// <summary>
    /// PDFium 原生文本 API 的动态加载封装（LoadLibrary + GetProcAddress + 函数委托）。
    /// 与 PdfiumViewer 复用同一个 pdfium.dll 模块：先按模块名加载，命中已加载实例则复用同一句柄，
    /// 避免重复加载与句柄泄漏；找不到再从常见输出目录（含 NuGet runtimes 目录）加载。
    /// 函数签名依据 bblanchon.PDFium.Win32 包内的 fpdfview.h / fpdf_text.h。
    /// </summary>
    internal static class PdfiumTextNative
    {
        private const string DllName = "pdfium.dll";

        private static readonly object syncRoot = new object();
        private static IntPtr dllHandle = IntPtr.Zero;
        private static bool ready;

        // ---- Kernel32 ----
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        // ---- 函数委托 ----
        private delegate void FPDF_InitLibraryDelegate();
        private delegate uint FPDF_GetLastErrorDelegate();
        private delegate IntPtr FPDF_LoadMemDocumentDelegate(IntPtr dataBuf, int size, [MarshalAs(UnmanagedType.LPStr)] string password);
        private delegate void FPDF_CloseDocumentDelegate(IntPtr document);
        private delegate int FPDF_GetPageCountDelegate(IntPtr document);
        private delegate IntPtr FPDF_LoadPageDelegate(IntPtr document, int pageIndex);
        private delegate void FPDF_ClosePageDelegate(IntPtr page);
        private delegate double FPDF_GetPageWidthDelegate(IntPtr page);
        private delegate double FPDF_GetPageHeightDelegate(IntPtr page);
        private delegate IntPtr FPDFText_LoadPageDelegate(IntPtr page);
        private delegate void FPDFText_ClosePageDelegate(IntPtr textPage);
        private delegate int FPDFText_CountCharsDelegate(IntPtr textPage);
        private delegate IntPtr FPDFText_FindStartDelegate(IntPtr textPage, [MarshalAs(UnmanagedType.LPWStr)] string findWhat, uint flags, int startIndex);
        private delegate int FPDFText_FindNextDelegate(IntPtr searchHandle);
        private delegate int FPDFText_FindPrevDelegate(IntPtr searchHandle);
        private delegate void FPDFText_FindCloseDelegate(IntPtr searchHandle);
        private delegate int FPDFText_GetSchResultIndexDelegate(IntPtr searchHandle);
        private delegate int FPDFText_GetSchCountDelegate(IntPtr searchHandle);
        private delegate int FPDFText_GetCharBoxDelegate(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);
        private delegate int FPDFText_GetTextDelegate(IntPtr textPage, int startIndex, int count, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder result);

        // ---- 解析后的函数入口 ----
        private static FPDF_InitLibraryDelegate fFPDF_InitLibrary;
        private static FPDF_GetLastErrorDelegate fFPDF_GetLastError;
        private static FPDF_LoadMemDocumentDelegate fFPDF_LoadMemDocument;
        private static FPDF_CloseDocumentDelegate fFPDF_CloseDocument;
        private static FPDF_GetPageCountDelegate fFPDF_GetPageCount;
        private static FPDF_LoadPageDelegate fFPDF_LoadPage;
        private static FPDF_ClosePageDelegate fFPDF_ClosePage;
        private static FPDF_GetPageWidthDelegate fFPDF_GetPageWidth;
        private static FPDF_GetPageHeightDelegate fFPDF_GetPageHeight;
        private static FPDFText_LoadPageDelegate fFPDFText_LoadPage;
        private static FPDFText_ClosePageDelegate fFPDFText_ClosePage;
        private static FPDFText_CountCharsDelegate fFPDFText_CountChars;
        private static FPDFText_FindStartDelegate fFPDFText_FindStart;
        private static FPDFText_FindNextDelegate fFPDFText_FindNext;
        private static FPDFText_FindPrevDelegate fFPDFText_FindPrev;
        private static FPDFText_FindCloseDelegate fFPDFText_FindClose;
        private static FPDFText_GetSchResultIndexDelegate fFPDFText_GetSchResultIndex;
        private static FPDFText_GetSchCountDelegate fFPDFText_GetSchCount;
        private static FPDFText_GetCharBoxDelegate fFPDFText_GetCharBox;
        private static FPDFText_GetTextDelegate fFPDFText_GetText;

        public static void EnsureReady()
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

                dllHandle = LoadPdfiumLibrary();
                if (dllHandle == IntPtr.Zero)
                {
                    throw new DllNotFoundException(
                        "无法加载 " + DllName + "，请确认程序目录或运行库目录存在该文件。");
                }

                ResolveDelegate("FPDF_InitLibrary", out fFPDF_InitLibrary);
                ResolveDelegate("FPDF_GetLastError", out fFPDF_GetLastError);
                ResolveDelegate("FPDF_LoadMemDocument", out fFPDF_LoadMemDocument);
                ResolveDelegate("FPDF_CloseDocument", out fFPDF_CloseDocument);
                ResolveDelegate("FPDF_GetPageCount", out fFPDF_GetPageCount);
                ResolveDelegate("FPDF_LoadPage", out fFPDF_LoadPage);
                ResolveDelegate("FPDF_ClosePage", out fFPDF_ClosePage);
                ResolveDelegate("FPDF_GetPageWidth", out fFPDF_GetPageWidth);
                ResolveDelegate("FPDF_GetPageHeight", out fFPDF_GetPageHeight);
                ResolveDelegate("FPDFText_LoadPage", out fFPDFText_LoadPage);
                ResolveDelegate("FPDFText_ClosePage", out fFPDFText_ClosePage);
                ResolveDelegate("FPDFText_CountChars", out fFPDFText_CountChars);
                ResolveDelegate("FPDFText_FindStart", out fFPDFText_FindStart);
                ResolveDelegate("FPDFText_FindNext", out fFPDFText_FindNext);
                ResolveDelegate("FPDFText_FindPrev", out fFPDFText_FindPrev);
                ResolveDelegate("FPDFText_FindClose", out fFPDFText_FindClose);
                ResolveDelegate("FPDFText_GetSchResultIndex", out fFPDFText_GetSchResultIndex);
                ResolveDelegate("FPDFText_GetSchCount", out fFPDFText_GetSchCount);
                ResolveDelegate("FPDFText_GetCharBox", out fFPDFText_GetCharBox);
                ResolveDelegate("FPDFText_GetText", out fFPDFText_GetText);

                fFPDF_InitLibrary();
                ready = true;
            }
        }

        private static IntPtr LoadPdfiumLibrary()
        {
            IntPtr loaded = LoadLibrary(DllName);
            if (loaded != IntPtr.Zero)
            {
                return loaded;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, DllName),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", DllName),
                Path.Combine(baseDir, "runtimes", "win-x86", "native", DllName),
                Path.Combine(baseDir, "runtimes", "win-arm64", "native", DllName),
                Path.Combine(baseDir, "x64", DllName),
                Path.Combine(baseDir, "x86", DllName)
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    IntPtr handle = LoadLibrary(candidate);
                    if (handle != IntPtr.Zero)
                    {
                        return handle;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private static void ResolveDelegate<TDelegate>(string procName, out TDelegate del) where TDelegate : class
        {
            IntPtr proc = GetProcAddress(dllHandle, procName);
            if (proc == IntPtr.Zero)
            {
                throw new EntryPointNotFoundException("PDFium 导出函数不存在：" + procName);
            }

            del = Marshal.GetDelegateForFunctionPointer(proc, typeof(TDelegate)) as TDelegate;
        }

        // ---- 公开调用入口 ----
        public static uint GetLastError()
        {
            return fFPDF_GetLastError();
        }

        public static IntPtr LoadMemDocument(byte[] fileBytes, out IntPtr buffer)
        {
            buffer = Marshal.AllocHGlobal(fileBytes.Length);
            Marshal.Copy(fileBytes, 0, buffer, fileBytes.Length);
            return fFPDF_LoadMemDocument(buffer, fileBytes.Length, null);
        }

        public static void CloseDocument(IntPtr document)
        {
            fFPDF_CloseDocument(document);
        }

        public static int GetPageCount(IntPtr document)
        {
            return fFPDF_GetPageCount(document);
        }

        public static IntPtr LoadPage(IntPtr document, int pageIndex)
        {
            return fFPDF_LoadPage(document, pageIndex);
        }

        public static void ClosePage(IntPtr page)
        {
            fFPDF_ClosePage(page);
        }

        public static double GetPageWidth(IntPtr page)
        {
            return fFPDF_GetPageWidth(page);
        }

        public static double GetPageHeight(IntPtr page)
        {
            return fFPDF_GetPageHeight(page);
        }

        public static IntPtr TextLoadPage(IntPtr page)
        {
            return fFPDFText_LoadPage(page);
        }

        public static void TextClosePage(IntPtr textPage)
        {
            fFPDFText_ClosePage(textPage);
        }

        public static int TextCountChars(IntPtr textPage)
        {
            return fFPDFText_CountChars(textPage);
        }

        public static IntPtr TextFindStart(IntPtr textPage, string findWhat, bool matchCase, int startIndex)
        {
            uint flags = matchCase ? 0x00000001u : 0u;
            return fFPDFText_FindStart(textPage, findWhat, flags, startIndex);
        }

        public static bool TextFindNext(IntPtr searchHandle)
        {
            return fFPDFText_FindNext(searchHandle) != 0;
        }

        public static bool TextFindPrev(IntPtr searchHandle)
        {
            return fFPDFText_FindPrev(searchHandle) != 0;
        }

        public static void TextFindClose(IntPtr searchHandle)
        {
            fFPDFText_FindClose(searchHandle);
        }

        public static int TextGetSchResultIndex(IntPtr searchHandle)
        {
            return fFPDFText_GetSchResultIndex(searchHandle);
        }

        public static int TextGetSchCount(IntPtr searchHandle)
        {
            return fFPDFText_GetSchCount(searchHandle);
        }

        public static bool TextGetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top)
        {
            return fFPDFText_GetCharBox(textPage, index, out left, out right, out bottom, out top) != 0;
        }

        public static string TextGetText(IntPtr textPage, int startIndex, int count)
        {
            StringBuilder buffer = new StringBuilder(count + 1);
            int written = fFPDFText_GetText(textPage, startIndex, count, buffer);
            return written > 0 ? buffer.ToString(0, Math.Min(written, count)) : string.Empty;
        }
    }

    /// <summary>PDF 中一处文字匹配结果（坐标为 PDF 页面点坐标，原点在页面左下角，单位 pt）。</summary>
    internal sealed class PdfTextMatch
    {
        public int PageIndex { get; set; }        // 0 起始页码
        public double Left { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double Top { get; set; }
        public double PageWidth { get; set; }
        public double PageHeight { get; set; }

        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Bottom + Top) / 2.0;

        public string MatchedText { get; set; }
    }

    /// <summary>
    /// 基于 PDFium 的文字查找封装：输入一段文字，返回每个匹配在 PDF 中的页码与包围盒。
    /// 通过 FPDF_LoadMemDocument 从内存加载，避免中文路径的编码问题。
    /// </summary>
    internal sealed class PdfTextSearcher : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly string pdfPath;
        private byte[] fileBytes;
        private IntPtr fileBuffer = IntPtr.Zero;
        private IntPtr document = IntPtr.Zero;

        public PdfTextSearcher(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file was not found.", pdfPath);
            }

            this.pdfPath = pdfPath;
            PdfiumTextNative.EnsureReady();
            OpenDocument();
        }

        public int PageCount
        {
            get
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    return PdfiumTextNative.GetPageCount(document);
                }
            }
        }

        /// <summary>
        /// 在全部页面中查找指定文字，返回所有匹配（页码 + 包围盒）。
        /// </summary>
        public List<PdfTextMatch> FindAll(string text, bool matchCase = false)
        {
            List<PdfTextMatch> results = new List<PdfTextMatch>();

            if (string.IsNullOrEmpty(text))
            {
                return results;
            }

            lock (syncRoot)
            {
                ThrowIfDisposed();

                int pageCount = PdfiumTextNative.GetPageCount(document);
                if (pageCount < 1)
                {
                    return results;
                }

                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    IntPtr page = PdfiumTextNative.LoadPage(document, pageIndex);
                    if (page == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        double pageWidth = PdfiumTextNative.GetPageWidth(page);
                        double pageHeight = PdfiumTextNative.GetPageHeight(page);
                        if (pageWidth <= 0 || pageHeight <= 0)
                        {
                            continue;
                        }

                        IntPtr textPage = PdfiumTextNative.TextLoadPage(page);
                        if (textPage == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            IntPtr search = PdfiumTextNative.TextFindStart(textPage, text, matchCase, 0);
                            if (search == IntPtr.Zero)
                            {
                                continue;
                            }

                            try
                            {
                                while (PdfiumTextNative.TextFindNext(search))
                                {
                                    int startChar = PdfiumTextNative.TextGetSchResultIndex(search);
                                    int charCount = PdfiumTextNative.TextGetSchCount(search);
                                    if (charCount <= 0)
                                    {
                                        continue;
                                    }

                                    PdfTextMatch match = BuildMatch(
                                        textPage, pageIndex, startChar, charCount, pageWidth, pageHeight);
                                    if (match != null)
                                    {
                                        results.Add(match);
                                    }
                                }
                            }
                            finally
                            {
                                PdfiumTextNative.TextFindClose(search);
                            }
                        }
                        finally
                        {
                            PdfiumTextNative.TextClosePage(textPage);
                        }
                    }
                    finally
                    {
                        PdfiumTextNative.ClosePage(page);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 判断文档是否含可搜索的文字层（任一页有字符即返回 true）。
        /// 用于区分图片型 PDF（扫描件/纯图片，无文字层）与文字型 PDF。
        /// </summary>
        public bool HasAnyText()
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();

                int pageCount = PdfiumTextNative.GetPageCount(document);
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    IntPtr page = PdfiumTextNative.LoadPage(document, pageIndex);
                    if (page == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        IntPtr textPage = PdfiumTextNative.TextLoadPage(page);
                        if (textPage == IntPtr.Zero)
                        {
                            continue;
                        }

                        try
                        {
                            if (PdfiumTextNative.TextCountChars(textPage) > 0)
                            {
                                return true;
                            }
                        }
                        finally
                        {
                            PdfiumTextNative.TextClosePage(textPage);
                        }
                    }
                    finally
                    {
                        PdfiumTextNative.ClosePage(page);
                    }
                }
            }

            return false;
        }

        private static PdfTextMatch BuildMatch(
            IntPtr textPage,
            int pageIndex,
            int startChar,
            int charCount,
            double pageWidth,
            double pageHeight)
        {
            double left = 0, right = 0, bottom = 0, top = 0;
            bool hasBox = false;

            for (int i = startChar; i < startChar + charCount; i++)
            {
                double l, r, b, t;
                if (!PdfiumTextNative.TextGetCharBox(textPage, i, out l, out r, out b, out t))
                {
                    continue;
                }

                if (!hasBox)
                {
                    left = l; right = r; bottom = b; top = t;
                    hasBox = true;
                }
                else
                {
                    left = Math.Min(left, l);
                    right = Math.Max(right, r);
                    bottom = Math.Min(bottom, b);
                    top = Math.Max(top, t);
                }
            }

            if (!hasBox)
            {
                return null;
            }

            return new PdfTextMatch
            {
                PageIndex = pageIndex,
                Left = left,
                Right = right,
                Bottom = bottom,
                Top = top,
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                MatchedText = PdfiumTextNative.TextGetText(textPage, startChar, charCount)
            };
        }

        private void OpenDocument()
        {
            try
            {
                fileBytes = File.ReadAllBytes(pdfPath);
                document = PdfiumTextNative.LoadMemDocument(fileBytes, out fileBuffer);
            }
            catch
            {
                ReleaseFileBuffer();
                throw;
            }

            if (document == IntPtr.Zero)
            {
                ReleaseFileBuffer();
                throw new InvalidOperationException(
                    "PDFium 无法打开该 PDF 文件，错误码：" + PdfiumTextNative.GetLastError());
            }
        }

        private void ReleaseFileBuffer()
        {
            if (fileBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(fileBuffer);
                fileBuffer = IntPtr.Zero;
            }

            fileBytes = null;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (document != IntPtr.Zero)
                {
                    PdfiumTextNative.CloseDocument(document);
                    document = IntPtr.Zero;
                }

                ReleaseFileBuffer();
            }
        }

        private void ThrowIfDisposed()
        {
            if (document == IntPtr.Zero)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
