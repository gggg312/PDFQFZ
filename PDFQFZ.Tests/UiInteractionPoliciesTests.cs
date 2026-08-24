using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class UiInteractionPoliciesTests
{
    [Fact]
    public void StartupModeDefaults_UseRequestedFirstRunSelections()
    {
        Assert.Equal(1, StartupModeDefaults.FileMode);
        Assert.Equal(1, StartupModeDefaults.NoSeamStamp);
        Assert.Equal(0, StartupModeDefaults.NoPageStamp);
        Assert.Equal(0, StartupModeDefaults.OverlayOutput);
    }

    [Theory]
    [InlineData("1", 8, 1)]
    [InlineData(" 6 ", 8, 6)]
    [InlineData("8", 8, 8)]
    public void PageNavigationPolicy_AcceptsPagesInsideRange(string text, int pageCount, int expected)
    {
        Assert.True(PageNavigationPolicy.TryParse(text, pageCount, out int page, out string error));
        Assert.Equal(expected, page);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("", 8)]
    [InlineData("a", 8)]
    [InlineData("0", 8)]
    [InlineData("9", 8)]
    [InlineData("1", 0)]
    public void PageNavigationPolicy_RejectsInvalidOrUnavailablePages(string text, int pageCount)
    {
        Assert.False(PageNavigationPolicy.TryParse(text, pageCount, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void WhiteBackgroundOptionPolicy_EnablesToleranceOnlyWhenSelected()
    {
        Assert.False(WhiteBackgroundOptionPolicy.IsToleranceEnabled(false));
        Assert.True(WhiteBackgroundOptionPolicy.IsToleranceEnabled(true));
    }

    [Theory]
    [InlineData(false, false, "请上传 PDF 文件")]
    [InlineData(true, false, "请选择 PDF 文件目录")]
    [InlineData(true, true, "请在上方选择目录内的 PDF 文件")]
    public void IdlePreviewPolicy_ReturnsStateSpecificPlaceholder(
        bool directoryMode,
        bool directorySelected,
        string expected)
    {
        Assert.Equal(expected, IdlePreviewPolicy.GetPlaceholderText(directoryMode, directorySelected));
    }

    [Fact]
    public void PreviewViewportLayout_FitsPortraitPageInsideAvailableSpace()
    {
        var size = PreviewViewportLayout.Calculate(800, 700, 595, 842, 18);

        Assert.True(size.Width <= 764);
        Assert.True(size.Height <= 664);
        Assert.InRange((double)size.Width / size.Height, 0.70, 0.71);
    }

    [Fact]
    public void PreviewViewportLayout_ReturnsEmptyForInvalidDimensions()
    {
        Assert.True(PreviewViewportLayout.Calculate(0, 700, 595, 842, 18).IsEmpty);
    }
}
