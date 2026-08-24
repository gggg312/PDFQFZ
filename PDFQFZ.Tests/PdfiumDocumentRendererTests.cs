using PDFQFZ.Library;
using System.Drawing;
using System.Globalization;
using System.Text;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class PdfiumDocumentRendererTests
{
    [Fact]
    public void Open_RendersPortraitAndLandscapePages_AndReleasesResources()
    {
        string path = CreateTwoPagePdf();
        try
        {
            IPdfDocumentRenderer renderer = PdfiumDocumentRenderer.Open(path);
            Assert.Equal(2, renderer.PageCount);

            using (Bitmap portrait = renderer.RenderPage(0, 72))
            using (Bitmap landscape = renderer.RenderPage(1, 300))
            {
                Assert.Equal(new Size(612, 792), portrait.Size);
                Assert.Equal(new Size(3300, 2550), landscape.Size);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(-1, 72));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(2, 72));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.RenderPage(0, 0));

            renderer.Dispose();
            renderer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => renderer.RenderPage(0, 72));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTwoPagePdf()
    {
        string[] objects =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << >> /Contents 5 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        };

        using MemoryStream stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        List<long> offsets = new List<long>();

        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, string.Format(
                CultureInfo.InvariantCulture,
                "{0} 0 obj\n{1}\nendobj\n",
                i + 1,
                objects[i]));
        }

        long xrefOffset = stream.Position;
        WriteAscii(stream, "xref\n0 6\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            WriteAscii(stream, offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(stream, string.Format(
            CultureInfo.InvariantCulture,
            "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{0}\n%%EOF\n",
            xrefOffset));

        string path = Path.Combine(Path.GetTempPath(), "PDFQFZ-render-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    private static void WriteAscii(Stream stream, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
