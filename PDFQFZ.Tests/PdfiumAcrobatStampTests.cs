using PDFQFZ.Library;
using System.Drawing;
using System.Drawing.Imaging;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class PdfiumAcrobatStampTests
{
    [Fact]
    public void Pdfium_RenderFlags_AnnotationsPaintAcrobatStampAppearance()
    {
        string samplePath = Environment.GetEnvironmentVariable("PDFQFZ_SAMPLE_PDF");
        if (string.IsNullOrWhiteSpace(samplePath) || !File.Exists(samplePath))
        {
            return;
        }

        string flattenedPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(samplePath);
        try
        {
            Assert.NotEqual(samplePath, flattenedPath);
            using IPdfDocumentRenderer sourceRenderer = PdfiumDocumentRenderer.Open(samplePath);
            using IPdfDocumentRenderer flattenedRenderer = PdfiumDocumentRenderer.Open(flattenedPath);
            using Bitmap sourceImage = sourceRenderer.RenderPage(0, 144);
            using Bitmap flattenedImage = flattenedRenderer.RenderPage(0, 144);
            bool differentPixelFound = false;
            for (int y = 0; y < sourceImage.Height && !differentPixelFound; y++)
            {
                for (int x = 0; x < sourceImage.Width; x++)
                {
                    if (sourceImage.GetPixel(x, y) != flattenedImage.GetPixel(x, y))
                    {
                        differentPixelFound = true;
                        break;
                    }
                }
            }

            Assert.True(differentPixelFound);
        }
        finally
        {
            PreviewPdfPreparation.TryDelete(flattenedPath);
        }
    }

    [Fact]
    public void MergeModeRasterization_RetainsAcrobatStampAppearance()
    {
        string samplePath = Environment.GetEnvironmentVariable("PDFQFZ_SAMPLE_PDF");
        if (string.IsNullOrWhiteSpace(samplePath) || !File.Exists(samplePath))
        {
            return;
        }

        string flattenedPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(samplePath);
        string mergedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            Assert.NotEqual(samplePath, flattenedPath);
            using (IPdfDocumentRenderer flattenedRenderer = PdfiumDocumentRenderer.Open(flattenedPath))
            using (Bitmap flattenedPage = flattenedRenderer.RenderPage(0, 144))
            {
                WriteRasterizedPdf(flattenedPage, mergedPath);
            }

            using IPdfDocumentRenderer sourceRenderer = PdfiumDocumentRenderer.Open(samplePath);
            using IPdfDocumentRenderer flattenedResultRenderer = PdfiumDocumentRenderer.Open(flattenedPath);
            using IPdfDocumentRenderer mergedRenderer = PdfiumDocumentRenderer.Open(mergedPath);
            using Bitmap sourcePage = sourceRenderer.RenderPage(0, 144);
            using Bitmap flattenedResultPage = flattenedResultRenderer.RenderPage(0, 144);
            using Bitmap mergedPage = mergedRenderer.RenderPage(0, 144);

            Assert.True(
                StampDifferencePixelsWereRetained(sourcePage, flattenedResultPage, mergedPage, out string diagnostic),
                diagnostic);
        }
        finally
        {
            PreviewPdfPreparation.TryDelete(flattenedPath);
            PreviewPdfPreparation.TryDelete(mergedPath);
        }
    }

    private static bool StampDifferencePixelsWereRetained(
        Bitmap source,
        Bitmap flattened,
        Bitmap merged,
        out string diagnostic)
    {
        Assert.Equal(source.Width, flattened.Width);
        Assert.Equal(source.Height, flattened.Height);
        Assert.Equal(source.Width, merged.Width);
        Assert.Equal(source.Height, merged.Height);

        int stampDifferencePixels = 0;
        int retainedPixels = 0;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Color sourceColor = source.GetPixel(x, y);
                Color flattenedColor = flattened.GetPixel(x, y);
                if (ColorDistance(sourceColor, flattenedColor) < 30)
                {
                    continue;
                }

                stampDifferencePixels++;
                Color mergedColor = merged.GetPixel(x, y);
                if (ColorDistance(mergedColor, flattenedColor) < ColorDistance(mergedColor, sourceColor))
                {
                    retainedPixels++;
                }
            }
        }

        diagnostic = $"stampDifferencePixels={stampDifferencePixels}, retainedPixels={retainedPixels}";
        return stampDifferencePixels > 100
            && retainedPixels >= stampDifferencePixels * 0.9;
    }

    private static int ColorDistance(Color first, Color second)
    {
        return Math.Abs(first.R - second.R)
            + Math.Abs(first.G - second.G)
            + Math.Abs(first.B - second.B);
    }

    private static void WriteRasterizedPdf(Bitmap bitmap, string outputPath)
    {
        using FileStream output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (Document document = new Document(new iTextSharp.text.Rectangle(0, 0), 0, 0, 0, 0))
        {
            PdfWriter.GetInstance(document, output);
            document.Open();
            iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(bitmap, ImageFormat.Bmp);
            float width = image.Width * 72f / 144f;
            float height = image.Height * 72f / 144f;
            image.ScaleToFit(width, height);
            document.SetPageSize(new iTextSharp.text.Rectangle(0, 0, width, height));
            document.NewPage();
            document.Add(image);
        }
    }
}
