using System;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 文字预盖章的位置计算结果：印章左上角的归一化坐标（px/py），
    /// 语义与手动点击盖章（AddPreviewStampAtPoint）保持一致：
    /// px/py 表示印章左上角在"页面显示区减印章尺寸后"区间内的比例位置。
    /// </summary>
    internal sealed class AutoStampPositionResult
    {
        public AutoStampPositionResult(float px, float py)
        {
            Px = px;
            Py = py;
        }

        public float Px { get; }
        public float Py { get; }
    }

    /// <summary>
    /// 文字预盖章位置计算：把 PDF 页面坐标中的文字中心换算成印章放置的归一化坐标。
    /// 规则：印章中心与文字中心重合；印章边缘超出页面边界时自动内移，保证印章完整在页面内。
    /// </summary>
    internal static class AutoStampPositionCalculator
    {
        /// <summary>
        /// 计算印章放置位置。
        /// </summary>
        /// <param name="textCenterX">文字中心 X（PDF 页面点坐标，原点在左下）。</param>
        /// <param name="textCenterY">文字中心 Y（PDF 页面点坐标，原点在左下）。</param>
        /// <param name="pageWidth">页面宽度（pt）。</param>
        /// <param name="pageHeight">页面高度（pt）。</param>
        /// <param name="stampWidthRatio">印章宽度占页面显示宽度的比例（0~1）。</param>
        /// <param name="stampHeightRatio">印章高度占页面显示高度的比例（0~1）。</param>
        public static AutoStampPositionResult Calculate(
            double textCenterX,
            double textCenterY,
            double pageWidth,
            double pageHeight,
            double stampWidthRatio,
            double stampHeightRatio)
        {
            if (pageWidth <= 0 || pageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageWidth), "页面尺寸必须大于 0。");
            }

            stampWidthRatio = Clamp(stampWidthRatio, 0.0, 0.98);
            stampHeightRatio = Clamp(stampHeightRatio, 0.0, 0.98);

            // 文字中心在页面中的比例位置
            double cxRatio = Clamp(textCenterX / pageWidth, 0.0, 1.0);            // 从左到右 0~1
            double cyFromTopRatio = Clamp(1.0 - textCenterY / pageHeight, 0.0, 1.0); // 从上到下 0~1

            // 印章左上角的页面比例位置（中心对齐）
            double leftRatio = cxRatio - stampWidthRatio / 2.0;
            double topRatio = cyFromTopRatio - stampHeightRatio / 2.0;

            // 出页内移：印章必须完整在页面内
            leftRatio = Clamp(leftRatio, 0.0, 1.0 - stampWidthRatio);
            topRatio = Clamp(topRatio, 0.0, 1.0 - stampHeightRatio);

            // 转成与手动盖章一致的 px/py 语义（PositionPreviewOverlay: x = (previewW - overlayW) * px）
            double px = leftRatio / Math.Max(1e-6, 1.0 - stampWidthRatio);
            double py = topRatio / Math.Max(1e-6, 1.0 - stampHeightRatio);

            return new AutoStampPositionResult((float)px, (float)py);
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
