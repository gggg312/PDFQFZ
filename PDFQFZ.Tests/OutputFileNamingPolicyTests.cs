using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests;

public class OutputFileNamingPolicyTests
{
    [Fact]
    public void FirstSuffixOutputUsesVersionOne()
    {
        string result = OutputFileNamingPolicy.GetNextFileName("1.pdf", "已盖章", false, Array.Empty<string>());

        Assert.Equal("1_已盖章V1.pdf", result);
    }

    [Fact]
    public void ExistingVersionsUseHighestVersionPlusOne()
    {
        string[] existing = { "1_已盖章V1.pdf", "1_已盖章V2.pdf", "1_已盖章V4.pdf" };

        string result = OutputFileNamingPolicy.GetNextFileName("1.pdf", "已盖章", false, existing);

        Assert.Equal("1_已盖章V5.pdf", result);
    }

    [Fact]
    public void EncryptedVariantParticipatesInVersionDetection()
    {
        string[] existing = { "项目_一_已盖章V1_加密.pdf" };

        string result = OutputFileNamingPolicy.GetNextFileName("项目_一.pdf", "已盖章", false, existing);

        Assert.Equal("项目_一_已盖章V2.pdf", result);
    }

    [Fact]
    public void PrefixModeKeepsVersionWithMarker()
    {
        string[] existing = { "已盖章V1_1.pdf", "已盖章V3_1_加密.pdf" };

        string result = OutputFileNamingPolicy.GetNextFileName("1.pdf", "已盖章", true, existing);

        Assert.Equal("已盖章V4_1.pdf", result);
    }

    [Fact]
    public void AddSuffixKeepsAllocatedVersion()
    {
        string result = OutputFileNamingPolicy.AddSuffix(@"D:\output\1_已盖章V2.pdf", "加密");

        Assert.Equal(@"D:\output\1_已盖章V2_加密.pdf", result);
    }

    [Fact]
    public void SuccessMessageIncludesActualOutputFileNameWithoutTime()
    {
        string result = OutputFileNamingPolicy.BuildSuccessMessage("1.pdf", @"D:\output\1_已盖章V2.pdf");

        Assert.Equal("成功！“1.pdf”盖章完成！输出文件名“1_已盖章V2.pdf”", result);
    }
}
