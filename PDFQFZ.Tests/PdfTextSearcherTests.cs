using PDFQFZ.Library;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class PdfTextSearcherTests
{
    private const string CompanyName = "西安塔力科技有限公司";
    private const string SealWord = "盖章";

    static PdfTextSearcherTests()
    {
        // .NET Core/.NET 5+ 默认不含 windows-1252 等代码页编码，iTextSharp 依赖它
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void FindAll_ChineseText_FindsPageAndPosition()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                List<PdfTextMatch> matches = searcher.FindAll(CompanyName);

                // 两页各出现一次
                Assert.Equal(2, matches.Count);

                // 第 1 页匹配：文字写在 (72, 700)，应在页面上半部
                PdfTextMatch page1 = matches[0];
                Assert.Equal(0, page1.PageIndex);
                Assert.True(page1.CenterY > 600, $"第1页 CenterY={page1.CenterY} 应在页面上半部");
                Assert.True(page1.CenterX > 100 && page1.CenterX < 300, $"第1页 CenterX={page1.CenterX} 应在预期横向范围");
                Assert.Equal(CompanyName, page1.MatchedText);

                // 第 2 页匹配：文字写在 (100, 200)，应在页面下半部
                PdfTextMatch page2 = matches[1];
                Assert.Equal(1, page2.PageIndex);
                Assert.True(page2.CenterY < 400, $"第2页 CenterY={page2.CenterY} 应在页面下半部");
                Assert.Equal(CompanyName, page2.MatchedText);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindAll_MultipleMatches_SamePage_ReturnsAll()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                List<PdfTextMatch> matches = searcher.FindAll(SealWord);

                // 第 1 页出现 1 次"盖章"
                Assert.Single(matches);
                Assert.Equal(0, matches[0].PageIndex);
                Assert.Equal(SealWord, matches[0].MatchedText);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindAll_NonExistentText_ReturnsEmpty()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                Assert.Empty(searcher.FindAll("不存在的文字XYZ"));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FindAll_AllBoundsStayInsidePage()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                foreach (PdfTextMatch match in searcher.FindAll(CompanyName))
                {
                    Assert.True(match.Left >= 0 && match.Right <= match.PageWidth,
                        $"X 越界: left={match.Left}, right={match.Right}, width={match.PageWidth}");
                    Assert.True(match.Bottom >= 0 && match.Top <= match.PageHeight,
                        $"Y 越界: bottom={match.Bottom}, top={match.Top}, height={match.PageHeight}");
                    Assert.True(match.Right > match.Left && match.Top > match.Bottom);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_FreesResources_AndThrowsOnUse()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            PdfTextSearcher searcher = new PdfTextSearcher(path);
            searcher.Dispose();
            searcher.Dispose();
            Assert.Throws<ObjectDisposedException>(() => searcher.FindAll(CompanyName));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasAnyText_TextPdf_ReturnsTrue()
    {
        string path = CreateChineseTwoPagePdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                Assert.True(searcher.HasAnyText());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasAnyText_ImageOnlyPdf_ReturnsFalse()
    {
        string path = CreateEmptyPdf();
        try
        {
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                Assert.False(searcher.HasAnyText());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateEmptyPdf()
    {
        string path = Path.Combine(Path.GetTempPath(), "PDFQFZ-emptypdf-" + Guid.NewGuid().ToString("N") + ".pdf");

        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (Document doc = new Document(PageSize.A4, 36, 36, 36, 36))
            {
                PdfWriter writer = PdfWriter.GetInstance(doc, fs);
                doc.Open();
                doc.NewPage();   // 空白页：只有无文字的内容（白色矩形），确保页面不被丢弃
                PdfContentByte cb = writer.DirectContent;
                cb.SetColorFill(new BaseColor(255, 255, 255));
                cb.Rectangle(36, 36, 523, 770);
                cb.Fill();
                doc.NewPage();
                PdfContentByte cb2 = writer.DirectContent;
                cb2.SetColorFill(new BaseColor(255, 255, 255));
                cb2.Rectangle(36, 36, 523, 770);
                cb2.Fill();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("生成空白测试 PDF 失败: " + ex.Message, ex);
        }

        return path;
    }

    private static string CreateChineseTwoPagePdf()
    {
        string path = Path.Combine(Path.GetTempPath(), "PDFQFZ-textsearch-" + Guid.NewGuid().ToString("N") + ".pdf");

        try
        {
            BaseFont chineseFont = LoadChineseFont(20);

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (Document doc = new Document(PageSize.A4, 36, 36, 36, 36))
            {
                PdfWriter writer = PdfWriter.GetInstance(doc, fs);
                doc.Open();
                doc.NewPage();

                // 第 1 页：顶部公司名 + 中部"盖章"
                PdfContentByte cb = writer.DirectContent;
                cb.BeginText();
                cb.SetFontAndSize(chineseFont, 20);
                cb.SetTextMatrix(72, 700);
                cb.ShowText(CompanyName);
                cb.SetTextMatrix(72, 400);
                cb.ShowText(SealWord);
                cb.EndText();

                // 第 2 页：公司名（第二处）
                doc.NewPage();
                PdfContentByte cb2 = writer.DirectContent;
                cb2.BeginText();
                cb2.SetFontAndSize(chineseFont, 16);
                cb2.SetTextMatrix(100, 200);
                cb2.ShowText(CompanyName);
                cb2.EndText();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("生成测试 PDF 失败: " + ex.Message, ex);
        }

        return path;
    }

    private static BaseFont LoadChineseFont(float fontSize)
    {
        string[] fontCandidates =
        {
            @"C:\Windows\Fonts\simsun.ttc,0",
            @"C:\Windows\Fonts\msyh.ttc,0",
            @"C:\Windows\Fonts\simhei.ttf",
            @"C:\Windows\Fonts\Deng.ttf"
        };

        List<string> errors = new List<string>();
        foreach (string candidate in fontCandidates)
        {
            try
            {
                if (File.Exists(candidate.Split(',')[0]))
                {
                    return BaseFont.CreateFont(candidate, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                }
            }
            catch (Exception ex)
            {
                errors.Add(candidate + " => " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        throw new InvalidOperationException("未找到可用的中文字体用于生成测试 PDF。诊断: " + string.Join(" | ", errors));
    }
}
