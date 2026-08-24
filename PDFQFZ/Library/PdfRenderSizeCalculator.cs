using System;
using System.Drawing;

namespace PDFQFZ.Library
{
    internal static class PdfRenderSizeCalculator
    {
        private const float PdfPointsPerInch = 72f;

        public static Size Calculate(float widthPoints, float heightPoints, int dpi)
        {
            if (widthPoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(widthPoints));
            }

            if (heightPoints <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(heightPoints));
            }

            if (dpi <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dpi));
            }

            int width = Math.Max(1, (int)Math.Round(widthPoints * dpi / PdfPointsPerInch, MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round(heightPoints * dpi / PdfPointsPerInch, MidpointRounding.AwayFromZero));
            return new Size(width, height);
        }
    }
}
