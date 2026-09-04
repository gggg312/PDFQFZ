using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class AutoStampPositionCalculatorTests
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double Tolerance = 1e-4;

    [Fact]
    public void Calculate_CenterAligned_StampCenterMatchesTextCenter()
    {
        // 文字中心在页面正中央
        double cx = PageWidth * 0.5;
        double cy = PageHeight * 0.5;
        double w = 0.2, h = 0.2;

        AutoStampPositionResult result = AutoStampPositionCalculator.Calculate(cx, cy, PageWidth, PageHeight, w, h);

        // 由 px/py 反推印章中心的页面比例，应等于文字中心比例
        double stampCenterX = result.Px * (1 - w) + w / 2;
        double stampCenterY = result.Py * (1 - h) + h / 2;
        Assert.True(Math.Abs(stampCenterX - 0.5) < Tolerance, $"stampCenterX={stampCenterX}");
        Assert.True(Math.Abs(stampCenterY - 0.5) < Tolerance, $"stampCenterY={stampCenterY}");
    }

    [Fact]
    public void Calculate_TextNearTop_StampMovesInsidePage()
    {
        // 文字中心贴近页面顶部（PDF 坐标 Y 从底部向上，所以 Y 大 = 靠顶），
        // 中心对齐会溢出顶部，应内移到印章顶贴页顶
        double cx = PageWidth * 0.5;
        double cy = PageHeight * 0.98;
        double w = 0.2, h = 0.2;

        AutoStampPositionResult result = AutoStampPositionCalculator.Calculate(cx, cy, PageWidth, PageHeight, w, h);

        // 印章顶部应贴页面顶部（topRatio=0 → py=0）
        Assert.True(Math.Abs(result.Py - 0.0) < Tolerance, $"Py={result.Py} 应内移到顶部");
        Assert.True(result.Px >= 0 && result.Px <= 1);
    }

    [Fact]
    public void Calculate_TextNearBottom_StampMovesInsidePage()
    {
        // 文字中心贴近页面底部（Y 小 = 靠底），应内移到印章底贴页底
        double cx = PageWidth * 0.5;
        double cy = PageHeight * 0.02;
        double w = 0.2, h = 0.2;

        AutoStampPositionResult result = AutoStampPositionCalculator.Calculate(cx, cy, PageWidth, PageHeight, w, h);

        Assert.True(Math.Abs(result.Py - 1.0) < Tolerance, $"Py={result.Py} 应内移到底部");
    }

    [Fact]
    public void Calculate_TextNearLeftEdge_StampMovesInsidePage()
    {
        double cx = PageWidth * 0.01;
        double cy = PageHeight * 0.5;
        double w = 0.2, h = 0.2;

        AutoStampPositionResult result = AutoStampPositionCalculator.Calculate(cx, cy, PageWidth, PageHeight, w, h);

        Assert.True(Math.Abs(result.Px - 0.0) < Tolerance, $"Px={result.Px} 应内移到左侧");
    }

    [Fact]
    public void Calculate_TextNearRightEdge_StampMovesInsidePage()
    {
        double cx = PageWidth * 0.99;
        double cy = PageHeight * 0.5;
        double w = 0.2, h = 0.2;

        AutoStampPositionResult result = AutoStampPositionCalculator.Calculate(cx, cy, PageWidth, PageHeight, w, h);

        Assert.True(Math.Abs(result.Px - 1.0) < Tolerance, $"Px={result.Px} 应内移到右侧");
    }

    [Fact]
    public void Calculate_AlwaysInsideUnitRange()
    {
        // 各种边界输入，px/py 都应落在 [0,1]
        double[] ratios = { 0.05, 0.1, 0.2, 0.5, 0.9 };
        foreach (double w in ratios)
        {
            foreach (double h in ratios)
            {
                foreach (double cxr in new[] { 0.0, 0.01, 0.5, 0.99, 1.0 })
                {
                    foreach (double cyr in new[] { 0.0, 0.01, 0.5, 0.99, 1.0 })
                    {
                        AutoStampPositionResult r = AutoStampPositionCalculator.Calculate(
                            PageWidth * cxr, PageHeight * cyr, PageWidth, PageHeight, w, h);
                        Assert.True(r.Px >= -Tolerance && r.Px <= 1 + Tolerance, $"Px={r.Px} 越界 w={w} cxr={cxr}");
                        Assert.True(r.Py >= -Tolerance && r.Py <= 1 + Tolerance, $"Py={r.Py} 越界 h={h} cyr={cyr}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Calculate_InvalidPageSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoStampPositionCalculator.Calculate(10, 10, 0, 100, 0.2, 0.2));
    }
}
