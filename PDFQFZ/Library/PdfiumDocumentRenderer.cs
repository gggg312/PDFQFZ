using PdfiumViewer;
using System;
using System.Drawing;

namespace PDFQFZ.Library
{
    internal sealed class PdfiumDocumentRenderer : IPdfDocumentRenderer
    {
        private readonly object syncRoot = new object();
        private PdfDocument document;

        private PdfiumDocumentRenderer(PdfDocument document)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public int PageCount
        {
            get
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    return document.PageCount;
                }
            }
        }

        public static IPdfDocumentRenderer Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PDF path is required.", nameof(path));
            }

            return new PdfiumDocumentRenderer(PdfDocument.Load(path));
        }

        public Bitmap RenderPage(int pageIndex, int dpi)
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();

                if (pageIndex < 0 || pageIndex >= document.PageCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(pageIndex));
                }

                SizeF pageSize = document.PageSizes[pageIndex];
                Size renderSize = PdfRenderSizeCalculator.Calculate(pageSize.Width, pageSize.Height, dpi);
                Image renderedImage = document.Render(
                    pageIndex,
                    renderSize.Width,
                    renderSize.Height,
                    dpi,
                    dpi,
                    PdfRenderFlags.Annotations);

                Bitmap bitmap = renderedImage as Bitmap;
                if (bitmap != null)
                {
                    return bitmap;
                }

                using (renderedImage)
                {
                    return new Bitmap(renderedImage);
                }
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (document == null)
                {
                    return;
                }

                document.Dispose();
                document = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (document == null)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }
}
