using System;
using System.Drawing;

namespace PDFQFZ.Library
{
    internal interface IPdfDocumentRenderer : IDisposable
    {
        int PageCount { get; }

        Bitmap RenderPage(int pageIndex, int dpi);
    }
}
