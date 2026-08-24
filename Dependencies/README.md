# PDF rendering dependencies

PDFQFZ no longer requires a manually supplied PDF rendering DLL.

Visual Studio or MSBuild restores these open-source packages from NuGet:

- `PdfiumViewer.Updated 2.14.5`
- `bblanchon.PDFium.Win32 153.0.8009`
- `iTextSharp 5.5.13.6`
- `Costura.Fody 6.2.0`

The project embeds the Windows x86 and x64 native PDFium libraries into the final executable through Costura. Do not commit restored NuGet DLLs or local copies of `pdfium.dll` to this directory.

See [`../THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) for license and redistribution information.
