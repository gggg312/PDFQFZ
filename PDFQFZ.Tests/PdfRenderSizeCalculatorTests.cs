using PDFQFZ.Library;
using System.Drawing;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class PdfRenderSizeCalculatorTests
{
    [Theory]
    [InlineData(612f, 792f, 72, 612, 792)]
    [InlineData(595f, 842f, 300, 2479, 3508)]
    [InlineData(842f, 595f, 300, 3508, 2479)]
    public void Calculate_ConvertsPdfPointsToPixels(
        float widthPoints,
        float heightPoints,
        int dpi,
        int expectedWidth,
        int expectedHeight)
    {
        Size result = PdfRenderSizeCalculator.Calculate(widthPoints, heightPoints, dpi);

        Assert.Equal(new Size(expectedWidth, expectedHeight), result);
    }

    [Fact]
    public void Calculate_RejectsNonPositiveInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderSizeCalculator.Calculate(0, 100, 72));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderSizeCalculator.Calculate(100, 0, 72));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfRenderSizeCalculator.Calculate(100, 100, 0));
    }
}
