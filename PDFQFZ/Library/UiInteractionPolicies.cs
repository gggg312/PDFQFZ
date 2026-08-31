using System;
using System.Drawing;

namespace PDFQFZ.Library
{
    internal static class StartupModeDefaults
    {
        public const int FileMode = 1;
        public const int NoSeamStamp = 1;
        public const int NoPageStamp = 0;
        public const int OverlayOutput = 0;
    }

    internal static class PageNavigationPolicy
    {
        public static bool TryParse(string text, int pageCount, out int page, out string error)
        {
            page = 0;
            if (pageCount < 1)
            {
                error = "请先加载 PDF，再输入页码。";
                return false;
            }

            if (!int.TryParse((text ?? string.Empty).Trim(), out page))
            {
                error = "页码必须是整数，请输入 1 到 " + pageCount + "。";
                return false;
            }

            if (page < 1 || page > pageCount)
            {
                error = "页码超出范围，请输入 1 到 " + pageCount + "。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static int MoveByWheel(int currentPage, int pageCount, int wheelDelta)
        {
            if (pageCount < 1)
            {
                return 0;
            }

            int page = Math.Max(1, Math.Min(pageCount, currentPage));
            if (wheelDelta > 0)
            {
                return Math.Max(1, page - 1);
            }

            if (wheelDelta < 0)
            {
                return Math.Min(pageCount, page + 1);
            }

            return page;
        }

        public static int NormalizeScrollValue(int value, int pageCount)
        {
            if (pageCount < 1)
            {
                return 1;
            }

            return Math.Max(1, Math.Min(pageCount, value));
        }
    }

    internal static class WhiteBackgroundOptionPolicy
    {
        public static bool IsToleranceEnabled(bool removeWhiteBackground)
        {
            return removeWhiteBackground;
        }
    }

    internal static class IdlePreviewPolicy
    {
        public static string GetPlaceholderText(bool directoryMode, bool directorySelected)
        {
            if (!directoryMode)
            {
                return "请上传 PDF 文件";
            }

            return directorySelected
                ? "请在上方选择目录内的 PDF 文件"
                : "请选择 PDF 文件目录";
        }
    }

    internal static class PreviewViewportLayout
    {
        public static Size Calculate(int viewportWidth, int viewportHeight, int imageWidth, int imageHeight, int margin)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            {
                return Size.Empty;
            }

            int availableWidth = Math.Max(1, viewportWidth - Math.Max(0, margin) * 2);
            int availableHeight = Math.Max(1, viewportHeight - Math.Max(0, margin) * 2);
            double scale = Math.Min((double)availableWidth / imageWidth, (double)availableHeight / imageHeight);
            return new Size(
                Math.Max(1, Convert.ToInt32(imageWidth * scale)),
                Math.Max(1, Convert.ToInt32(imageHeight * scale)));
        }
    }
}
