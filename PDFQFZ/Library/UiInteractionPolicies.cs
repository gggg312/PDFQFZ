using System;
using System.Drawing;

namespace PDFQFZ.Library
{
    internal static class StartupModeDefaults
    {
        public const int FileMode = 1;
        public const int NoSeamStamp = 1;
        public const int NoPageStamp = 0;
        public const int OverlayOutput = 1;
    }

    internal enum PreviewViewMode
    {
        SinglePage = 0,
        Scroll = 1
    }

    internal static class PreviewZoomPolicy
    {
        public const int MinimumPercent = 100;
        public const int MaximumPercent = 300;
        public const int StepPercent = 25;
        public const int WheelStepPercent = 10;

        public static int Clamp(int percent)
        {
            return Math.Max(MinimumPercent, Math.Min(MaximumPercent, percent));
        }

        public static int Step(int percent, int direction)
        {
            if (direction == 0)
            {
                return Clamp(percent);
            }

            return Clamp(percent + Math.Sign(direction) * StepPercent);
        }

        public static int StepByWheel(int percent, int direction)
        {
            if (direction == 0)
            {
                return Clamp(percent);
            }

            return Clamp(percent + Math.Sign(direction) * WheelStepPercent);
        }

        public static bool TryParse(string text, out int percent, out string error)
        {
            percent = 0;
            if (!int.TryParse((text ?? string.Empty).Trim(), out int value))
            {
                error = "比例必须是 100 到 300 之间的整数。";
                return false;
            }

            if (value < MinimumPercent || value > MaximumPercent)
            {
                error = "比例必须是 100 到 300 之间的整数。";
                return false;
            }

            percent = value;
            error = string.Empty;
            return true;
        }
    }

    internal static class PreviewGesturePolicy
    {
        public static bool IsDrag(Point startPoint, Point currentPoint, Size dragSize)
        {
            int halfWidth = Math.Max(1, dragSize.Width / 2);
            int halfHeight = Math.Max(1, dragSize.Height / 2);
            Rectangle clickBounds = new Rectangle(
                startPoint.X - halfWidth,
                startPoint.Y - halfHeight,
                halfWidth * 2,
                halfHeight * 2);
            return !clickBounds.Contains(currentPoint);
        }
    }

    internal static class PreviewOperationHintPolicy
    {
        public static string Build(
            bool hasPreview,
            bool directoryMode,
            bool directorySelected,
            bool specifiedRangePending,
            bool pageStampEnabled,
            PreviewViewMode viewMode)
        {
            if (!hasPreview)
            {
                if (!directoryMode)
                {
                    return "请选择或拖入 PDF 文件。";
                }

                return directorySelected
                    ? "请在上方文件列表中选择要预览的 PDF。"
                    : "请选择或拖入 PDF 文件目录。";
            }

            string actionHint;
            if (specifiedRangePending)
            {
                actionHint = "已跳转至指定范围最后一页。单击页面设置盖章位置；按住左键拖动可查看页面；右键印章可删除。";
            }
            else if (pageStampEnabled)
            {
                actionHint = "单击页面添加印章；按住左键拖动页面；右键已有印章可删除。";
            }
            else
            {
                actionHint = "当前未启用页面印章；可按住左键拖动查看页面。";
            }

            string viewHint = viewMode == PreviewViewMode.SinglePage
                ? "单页视图。鼠标滚轮翻页，Ctrl+滚轮缩放。"
                : "放大视图。鼠标滚轮滚动页面，Ctrl+滚轮缩放。";
            return actionHint + " " + viewHint;
        }
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
