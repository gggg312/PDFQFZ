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
        Assert.Equal(1, StartupModeDefaults.OverlayOutput);
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

    [Theory]
    [InlineData(1, 14, 120, 1)]
    [InlineData(5, 14, 120, 4)]
    [InlineData(5, 14, -120, 6)]
    [InlineData(14, 14, -120, 14)]
    [InlineData(5, 14, 0, 5)]
    public void PageNavigationPolicy_MovesOnePagePerWheelStep(
        int currentPage,
        int pageCount,
        int wheelDelta,
        int expected)
    {
        Assert.Equal(expected, PageNavigationPolicy.MoveByWheel(currentPage, pageCount, wheelDelta));
    }

    [Theory]
    [InlineData(-1, 14, 1)]
    [InlineData(1, 14, 1)]
    [InlineData(20, 14, 14)]
    [InlineData(6, 0, 1)]
    public void PageNavigationPolicy_NormalizesScrollValueToPageRange(
        int value,
        int pageCount,
        int expected)
    {
        Assert.Equal(expected, PageNavigationPolicy.NormalizeScrollValue(value, pageCount));
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

    [Theory]
    [InlineData("100", 100)]
    [InlineData(" 225 ", 225)]
    [InlineData("300", 300)]
    public void PreviewZoomPolicy_AcceptsIntegerPercentagesInsideRange(string text, int expected)
    {
        Assert.True(PreviewZoomPolicy.TryParse(text, out int percent, out string error));
        Assert.Equal(expected, percent);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("99", false)]
    [InlineData("301", false)]
    [InlineData("100.5", false)]
    public void PreviewZoomPolicy_RejectsInvalidPercentages(string text, bool expected)
    {
        Assert.Equal(expected, PreviewZoomPolicy.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData(100, -1, 100)]
    [InlineData(100, 1, 125)]
    [InlineData(275, 1, 300)]
    [InlineData(300, 1, 300)]
    [InlineData(125, -1, 100)]
    public void PreviewZoomPolicy_StepsByTwentyFiveAndClamps(int current, int direction, int expected)
    {
        Assert.Equal(expected, PreviewZoomPolicy.Step(current, direction));
    }

    [Theory]
    [InlineData(100, -1, 100)]
    [InlineData(100, 1, 110)]
    [InlineData(150, 1, 160)]
    [InlineData(150, -1, 140)]
    [InlineData(300, 1, 300)]
    public void PreviewZoomPolicy_WheelStepsByTenAndClamps(int current, int direction, int expected)
    {
        Assert.Equal(expected, PreviewZoomPolicy.StepByWheel(current, direction));
    }

    [Fact]
    public void PreviewGesturePolicy_StaysClickInsideSystemDragThreshold()
    {
        Assert.False(PreviewGesturePolicy.IsDrag(new Point(100, 100), new Point(102, 102), new Size(8, 8)));
    }

    [Fact]
    public void PreviewGesturePolicy_BecomesDragOutsideSystemDragThreshold()
    {
        Assert.True(PreviewGesturePolicy.IsDrag(new Point(100, 100), new Point(106, 100), new Size(8, 8)));
    }

    [Fact]
    public void PreviewOperationHintPolicy_DescribesSpecifiedRangeDeletionAndSinglePageControls()
    {
        string hint = PreviewOperationHintPolicy.Build(true, false, false, true, true, PreviewViewMode.SinglePage);

        Assert.Contains("盖章后双击印章可删除", hint);
        Assert.Contains("鼠标滚轮翻页", hint);
        Assert.DoesNotContain("100%", hint);
    }

    [Fact]
    public void PreviewOperationHintPolicy_DescribesScrollControlsWithoutPercentage()
    {
        string hint = PreviewOperationHintPolicy.Build(true, false, false, false, true, PreviewViewMode.Scroll);

        Assert.Contains("按住左键拖动页面", hint);
        Assert.Contains("鼠标滚轮滚动页面", hint);
        Assert.DoesNotContain("%", hint);
    }

    [Theory]
    [InlineData(false, false, "请选择或拖入 PDF 文件。")]
    [InlineData(true, false, "请选择或拖入 PDF 文件目录。")]
    [InlineData(true, true, "请在上方文件列表中选择要预览的 PDF。")]
    public void PreviewOperationHintPolicy_DescribesIdleState(bool directoryMode, bool directorySelected, string expected)
    {
        Assert.Equal(expected, PreviewOperationHintPolicy.Build(false, directoryMode, directorySelected, false, false, PreviewViewMode.SinglePage));
    }
}
