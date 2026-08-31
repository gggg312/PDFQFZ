using iTextSharp.text.pdf;
using System;
using System.IO;

namespace PDFQFZ.Library
{
    internal static class PreviewPdfPreparation
    {
        public static string CreateAnnotationFlattenedCopy(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Preview PDF was not found.", sourcePath);
            }

            string directory = Path.Combine(Path.GetTempPath(), "PDFQFZ", "preview");
            Directory.CreateDirectory(directory);
            string targetPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pdf");

            try
            {
                using (PdfReader reader = new PdfReader(sourcePath))
                using (FileStream output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (PdfStamper stamper = new PdfStamper(reader, output))
                {
                    stamper.AnnotationFlattening = true;
                }

                return targetPath;
            }
            catch
            {
                TryDelete(targetPath);
                return sourcePath;
            }
        }

        public static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
