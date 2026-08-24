# Local build dependency

`PDFQFZ` currently requires `O2S.Components.PDFRender4NET.dll` to render PDF previews.

The DLL is not included in this repository. To build the application locally, obtain a legally licensed copy and place it at:

```text
Dependencies/O2S.Components.PDFRender4NET.dll
```

The project file references this relative path. Do not commit the DLL to the repository.
