using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class PageRangeTests
{
    [Fact]
    public void TryCreate_AcceptsValidInclusiveRange()
    {
        Assert.True(PageRange.TryCreate("3", "8", 10, out PageRange range, out _));
        Assert.Equal(3, range.StartPage);
        Assert.Equal(8, range.EndPage);
        Assert.True(range.Contains(3));
        Assert.True(range.Contains(8));
        Assert.False(range.Contains(2));
    }

    [Theory]
    [InlineData("", "8")]
    [InlineData("3", "abc")]
    [InlineData("0", "8")]
    [InlineData("3", "11")]
    [InlineData("8", "3")]
    public void TryCreate_RejectsInvalidRange(string start, string end)
    {
        Assert.False(PageRange.TryCreate(start, end, 10, out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
