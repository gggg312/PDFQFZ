using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>盖章参数（从原 Form1 的实例字段参数化而来）。</summary>
    internal sealed class StampOptions
    {
        public string StampImagePath;        // 印章原图路径
        public string OutputPath;            // 输出目录
        public int QfzType;                  // 骑缝章类型 0加盖/1不加/2单页/3双页/4随意
        public int QmType;                   // 数字签名 0不使用/1自生成/2自定义
        public string SignText;              // 签名文本 / 证书标识
        public string SignPassword;          // 签名密码
        public bool RemoveWhite;             // 去除白色背景
        public int Opacity;                  // 印章不透明度
        public int Rotation;                 // 印章旋转角度
        public bool OriginalRotationCrop;    // 旋转切边（qbflag==0）
        public int SizeMm;                   // 印章尺寸(mm)
        public string PdfPassword;           // PDF 密码
        public int WzType;                   // 骑缝章位置 0下/1上/2左/3右
        public int WzPercent;                // 骑缝章位置百分比
        public int MaxSplit;                 // 骑缝章最大分割数
        public StampPlacementCollection Placements;   // 页面章放置集合
        public string CertDefaultPath;       // 内置证书路径
        public X509Certificate2 Cert;        // 已加载的签名证书（PrepareStampResources 输出）
        public int FixType;                  // 输出前后缀类型
        public string FixStr;                // 输出前后缀文本
        public string FixStr2;               // 加密后缀
    }

    /// <summary>
    /// 盖章引擎：从原 Form1.cs 移植的核心业务（iTextSharp 盖章、图片处理、骑缝章、签名、加密、转图）。
    /// 纯逻辑，不依赖任何 UI 控件；进度与提示通过回调上抛。
    /// </summary>
    internal static class StampEngine
    {

        // ===================== 印章资源准备 =====================
        /// <summary>准备骑缝章图像与证书。返回 false 表示失败（日志已写）。</summary>
        public static bool PrepareStampResources(
            StampOptions opt,
            out Bitmap seamImage,
            out float xzbl,
            out X509Certificate2 cert,
            Action<string> log)
        {
            seamImage = null;
            xzbl = 1f;
            cert = null;

            if (!Directory.Exists(opt.OutputPath))
            {
                Directory.CreateDirectory(opt.OutputPath);
            }

            try
            {
                // 无盖章、无签名、无印章：占位图走"仅输出文件"流程
                if (opt.QfzType == 1 && opt.QmType == 0 && opt.Placements.Count == 0
                    && (string.IsNullOrEmpty(opt.StampImagePath) || !File.Exists(opt.StampImagePath)))
                {
                    seamImage = new Bitmap(1, 1);
                    return true;
                }

                // 数字签名：先确保证书可加载
                if (opt.QmType != 0)
                {
                    string certPath = null;
                    if (opt.QmType == 1)
                    {
                        if (!File.Exists(opt.CertDefaultPath))
                        {
                            var rsa = RSA.Create(4096);
                            var req = new CertificateRequest("CN=" + opt.SignText, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                            X509Certificate2 newCert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));
                            File.WriteAllBytes(opt.CertDefaultPath, newCert.Export(X509ContentType.Pkcs12, opt.SignPassword));
                        }
                        certPath = opt.CertDefaultPath;
                    }
                    else
                    {
                        certPath = opt.SignText;
                    }

                    try
                    {
                        cert = new X509Certificate2(certPath, opt.SignPassword,
                            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
                    }
                    catch
                    {
                        log("证书加载失败，请检查证书路径和密码。");
                        return false;
                    }
                }

                seamImage = new Bitmap(opt.StampImagePath);
                if (opt.RemoveWhite)
                {
                    seamImage = WhiteTransparencyHelper.Apply(seamImage, 20);
                }
                if (opt.Opacity < 100)
                {
                    seamImage = SetImageOpacity(seamImage, opt.Opacity);
                }
                if (opt.Rotation != 0)
                {
                    int iw = seamImage.Width;
                    seamImage = RotateImg(seamImage, opt.Rotation, opt.OriginalRotationCrop);
                    xzbl = 1f * seamImage.Width / iw;
                }
                return true;
            }
            catch (Exception ex)
            {
                log("印章准备失败：" + ex.Message);
                if (seamImage != null) { seamImage.Dispose(); seamImage = null; }
                return false;
            }
        }

        // ===================== 盖章核心 =====================
        public static bool PDFWatermark(
            StampOptions opt,
            Bitmap seamImage,
            float xzbl,
            string inputfilepath,
            string outputfilepath,
            string sourcepath,
            Action<string> log,
            Action<int, int> pageProgress = null,
            Action<bool> savingIndicator = null)
        {
            float sfbl = (100f * opt.SizeMm * xzbl * 72) / (25.4f * seamImage.Width);

            PdfReader pdfReader = null;
            PdfStamper pdfStamper = null;
            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(outputfilepath, FileMode.Create);
                pdfReader = new PdfReader(inputfilepath, new UTF8Encoding().GetBytes(opt.PdfPassword));
                if (opt.QmType != 0)
                {
                    pdfStamper = PdfStamper.CreateSignature(pdfReader, fileStream, '\0', null, true);
                }
                else
                {
                    pdfStamper = new PdfStamper(pdfReader, fileStream);
                }

                int numberOfPages = pdfReader.NumberOfPages;
                int qfzPages = 0;
                List<int> qfzList = new List<int>();

                bool skipSeamStamp = SeamStampPolicy.ShouldSkipForSinglePage(numberOfPages, opt.QfzType);
                if (!skipSeamStamp && opt.QfzType == 0)
                {
                    for (int i = 1; i <= numberOfPages; i++)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 2)
                {
                    for (int i = 1; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 3)
                {
                    for (int i = 2; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if (!skipSeamStamp && opt.QfzType == 4)
                {
                    foreach (int page in opt.Placements.DistinctPages(sourcepath))
                    {
                        qfzList.Add(page);
                        qfzPages++;
                    }
                }

                PdfContentByte waterMarkContent;

                if (opt.QfzType != 1 && qfzPages > 1)
                {
                    int max = opt.MaxSplit;
                    if (max < 1) max = 1;
                    // 段数向上取整：max 大于等于骑缝章页数时一整段盖完；
                    // 旧公式 qfzPages/max+1 在整除（如 29 页、分割数 29）时会多拆一段，导致效果减半。
                    int ss = (qfzPages + max - 1) / max;
                    int sy = qfzPages - ss * max / 2;
                    int sys = sy / ss;
                    int syy = sy % ss;
                    int pp = max / 2 + sys;
                    Bitmap[] nImage;
                    int startIndex = 0;
                    for (int i = 0; i < ss; i++)
                    {
                        int tmp = pp;
                        if (i < syy)
                        {
                            tmp++;
                        }
                        nImage = subImages(seamImage, tmp);
                        for (int y = 0; y < tmp; y++)
                        {
                            int page = qfzList[startIndex + y];
                            waterMarkContent = pdfStamper.GetOverContent(page);
                            int rotation = pdfReader.GetPageRotation(page);
                            iTextSharp.text.Rectangle psize = pdfReader.GetPageSize(page);
                            float pWidth, pHeight;
                            if (rotation == 90 || rotation == 270)
                            {
                                pWidth = psize.Height;
                                pHeight = psize.Width;
                            }
                            else
                            {
                                pWidth = psize.Width;
                                pHeight = psize.Height;
                            }
                            Bitmap qfzImage;
                            if (opt.WzType == 3 || opt.WzType == 2)
                            {
                                qfzImage = nImage[y];
                            }
                            else
                            {
                                qfzImage = RotateImg(nImage[y], 90, false);
                            }
                            iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(qfzImage, ImageFormat.Png);
                            float imageW, imageH;
                            image.ScalePercent(sfbl);
                            imageW = image.Width * sfbl / 100f;
                            imageH = image.Height * sfbl / 100f;

                            float xPos = 0, yPos = 0;
                            if (opt.WzType == 3)
                            {
                                xPos = pWidth - imageW;
                                yPos = (pHeight - imageH) * (100 - opt.WzPercent) / 100;
                            }
                            else if (opt.WzType == 2)
                            {
                                xPos = 0;
                                yPos = (pHeight - imageH) * (100 - opt.WzPercent) / 100;
                            }
                            else if (opt.WzType == 1)
                            {
                                xPos = (pWidth - imageW) * opt.WzPercent / 100;
                                yPos = 0;
                            }
                            else
                            {
                                xPos = (pWidth - imageW) * opt.WzPercent / 100;
                                yPos = pHeight - imageH;
                            }
                            image.SetAbsolutePosition(xPos, yPos);
                            waterMarkContent.AddImage(image);
                        }
                        startIndex += tmp;
                    }
                }

                iTextSharp.text.Image img = null;
                float imgW = 0, imgH = 0;
                float stampXPos = 0, stampYPos = 0;
                int signpage = numberOfPages;

                // 页面章渲染：预览上实际放置了什么章，生成时就盖什么章
                if (opt.Placements.Count > 0)
                {
                    List<int> placementPages = opt.Placements.DistinctPages(sourcepath).ToList();
                    signpage = placementPages.Count == 0 ? 1 : placementPages[placementPages.Count - 1];
                    StampPlacement signaturePlacement = opt.QmType == 0
                        ? null
                        : opt.Placements.ForPage(sourcepath, signpage).LastOrDefault();

                    int totalProcessPages = numberOfPages;
                    for (int page = 1; page <= numberOfPages; page++)
                    {
                        if (pageProgress != null)
                        {
                            pageProgress(Math.Min(page, totalProcessPages), totalProcessPages);
                        }

                        List<StampPlacement> pagePlacements = opt.Placements.ForPage(sourcepath, page).ToList();
                        if (pagePlacements.Count == 0)
                        {
                            continue;
                        }

                        waterMarkContent = pdfStamper.GetOverContent(page);
                        int pageRotation = pdfReader.GetPageRotation(page);
                        iTextSharp.text.Rectangle pageSize = pdfReader.GetPageSize(page);

                        foreach (StampPlacement placement in pagePlacements)
                        {
                            using (Bitmap placementBitmap = CreatePlacementBitmap(placement))
                            {
                                iTextSharp.text.Image placementImage = iTextSharp.text.Image.GetInstance(
                                    placementBitmap, ImageFormat.Png);
                                float placementScale = 100f * placement.SizeMm * 72f /
                                    (25.4f * placementBitmap.Width);
                                placementImage.ScalePercent(placementScale);
                                placementImage.RotationDegrees = placement.Rotation;

                                float placementWidth = placementImage.Width * placementScale / 100f;
                                float placementHeight = placementImage.Height * placementScale / 100f;
                                float placementX;
                                float placementY;
                                CalculateStampPosition(
                                    pageRotation,
                                    pageSize,
                                    placementWidth,
                                    placementHeight,
                                    placement.X,
                                    1f - placement.Y,
                                    placement.CenterRatio,
                                    out placementX,
                                    out placementY);
                                placementImage.SetAbsolutePosition(placementX, placementY);

                                bool useForDigitalSignature = signaturePlacement != null &&
                                    placement.Id == signaturePlacement.Id;
                                if (useForDigitalSignature)
                                {
                                    img = placementImage;
                                    imgW = placementWidth;
                                    imgH = placementHeight;
                                    stampXPos = placementX;
                                    stampYPos = placementY;
                                }
                                else
                                {
                                    waterMarkContent.AddImage(placementImage);
                                }
                            }
                        }
                    }
                }

                if (opt.QmType != 0 && opt.Cert != null)
                {
                    Org.BouncyCastle.X509.X509CertificateParser cp = new Org.BouncyCastle.X509.X509CertificateParser();
                    Org.BouncyCastle.X509.X509Certificate[] chain = new Org.BouncyCastle.X509.X509Certificate[] { cp.ReadCertificate(opt.Cert.RawData) };

                    AsymmetricCipherKeyPair pk = DotNetUtilities.GetKeyPair(opt.Cert.GetRSAPrivateKey());
                    IExternalSignature externalSignature = new PrivateKeySignature(pk.Private, DigestAlgorithms.SHA256);

                    PdfSignatureAppearance signatureAppearance = pdfStamper.SignatureAppearance;
                    signatureAppearance.SignDate = DateTime.Now;
                    signatureAppearance.SetVisibleSignature(new iTextSharp.text.Rectangle(0, 0, 0, 0), numberOfPages, null);
                    if (img != null)
                    {
                        signatureAppearance.SignatureRenderingMode = PdfSignatureAppearance.RenderingMode.GRAPHIC;
                        signatureAppearance.SignatureGraphic = img;

                        float bk = 2;
                        signatureAppearance.SetVisibleSignature(new iTextSharp.text.Rectangle(stampXPos - bk, stampYPos - bk, stampXPos + imgW + bk, stampYPos + imgH + bk), signpage, null);
                    }

                    MakeSignature.SignDetached(signatureAppearance, externalSignature, chain, null, null, null, 0, CryptoStandard.CMS);
                }
                return true;
            }
            catch (BadPasswordException)
            {
                log("文件“" + Path.GetFileName(inputfilepath) + "”打不开：PDF 密码错误或文件已加密。");
                return false;
            }
            catch (Exception ex)
            {
                log("文件“" + Path.GetFileName(inputfilepath) + "”盖章失败：" + ex.Message);
                return false;
            }
            finally
            {
                if (savingIndicator != null)
                {
                    savingIndicator(true);
                }

                try
                {
                    if (pdfStamper != null) pdfStamper.Close();
                    if (pdfReader != null) pdfReader.Close();
                    if (fileStream != null) fileStream.Close();
                }
                finally
                {
                    if (savingIndicator != null)
                    {
                        savingIndicator(false);
                    }
                }

                if (File.Exists(outputfilepath))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(outputfilepath);
                        if (fi.Length == 0)
                        {
                            File.Delete(outputfilepath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        // ===================== 图片处理 =====================
        /// <summary>按放置参数生成最终印章位图（去白/透明度/旋转）。供预览叠加与盖章共用。</summary>
        public static Bitmap CreatePlacementBitmap(StampPlacement placement)
        {
            Bitmap processed = new Bitmap(placement.StampPath);
            if (placement.UseWhiteTransparency)
            {
                Bitmap transparent = WhiteTransparencyHelper.Apply(processed, placement.WhiteTransparencyTolerance);
                processed.Dispose();
                processed = transparent;
            }

            if (placement.Opacity < 100)
            {
                processed = SetImageOpacity(processed, placement.Opacity);
            }

            if (placement.Rotation != 0)
            {
                Bitmap rotated = RotateImg(processed, placement.Rotation, placement.UseOriginalRotationCrop);
                if (!ReferenceEquals(rotated, processed))
                {
                    processed.Dispose();
                }
                processed = rotated;
            }

            return processed;
        }

        private static void CalculateStampPosition(
            int pageRotation,
            iTextSharp.text.Rectangle pageSize,
            float imageWidth,
            float imageHeight,
            float widthRatio,
            float heightRatio,
            bool centerRatio,
            out float x,
            out float y)
        {
            // centerRatio=true（手动/范围页盖章）：印章中心在页面内的比例，超出部分由页面边界自然裁剪；
            // centerRatio=false（按文字盖章）：印章完整在页面内（左上角在"页面减章宽"区间内的比例）
            if (pageRotation == 90 || pageRotation == 270)
            {
                x = centerRatio
                    ? pageSize.Height * widthRatio - imageWidth / 2f
                    : (pageSize.Height - imageWidth) * widthRatio;
                y = centerRatio
                    ? pageSize.Width * heightRatio - imageHeight / 2f
                    : (pageSize.Width - imageHeight) * heightRatio;
            }
            else
            {
                x = centerRatio
                    ? pageSize.Width * widthRatio - imageWidth / 2f
                    : (pageSize.Width - imageWidth) * widthRatio;
                y = centerRatio
                    ? pageSize.Height * heightRatio - imageHeight / 2f
                    : (pageSize.Height - imageHeight) * heightRatio;
            }
        }

        private static Bitmap SetImageOpacity(Bitmap src, int opacity)
        {
            byte alpha = (byte)(opacity * 255 / 100);
            BitmapData data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
                                         ImageLockMode.ReadWrite,
                                         PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (int i = 3; i < buffer.Length; i += 4)
                {
                    if (buffer[i] != 0) buffer[i] = alpha;
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                src.UnlockBits(data);
            }
            return src;
        }

        private static Bitmap RotateImg(Bitmap bitmap, int angle, bool original = true)
        {
            angle = angle % 360;
            double radian = angle * Math.PI / 180.0;
            double cos = Math.Cos(radian);
            double sin = Math.Sin(radian);
            int w = bitmap.Width;
            int h = bitmap.Height;
            int W = (int)(Math.Max(Math.Abs(w * cos - h * sin), Math.Abs(w * cos + h * sin)));
            int H = (int)(Math.Max(Math.Abs(w * sin - h * cos), Math.Abs(w * sin + h * cos)));

            if (original)
            {
                H = H * w / W;
                W = w;
            }

            Bitmap dsImage = new Bitmap(W, H);
            Graphics g = Graphics.FromImage(dsImage);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            Point Offset = new Point((W - w) / 2, (H - h) / 2);
            Rectangle rect = new Rectangle(Offset.X, Offset.Y, w, h);
            Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            g.TranslateTransform(-center.X, -center.Y);
            g.DrawImage(bitmap, rect);
            g.ResetTransform();
            g.Dispose();
            return dsImage;
        }

        private static Bitmap[] subImages(Bitmap img, int n)
        {
            Bitmap[] nImage = new Bitmap[n];
            int H = img.Height;
            int W = img.Width;
            int w1 = W / 3;
            int w = (W - w1) / n;
            n = n - 1;
            int tmpw = W;
            for (int i = 0; i <= n; i++)
            {
                int sw;
                if (i == n)
                {
                    sw = tmpw;
                }
                else if (i == 0)
                {
                    sw = w1;
                }
                else
                {
                    sw = w;
                }
                Bitmap newbitmap = new Bitmap(sw, H);
                Graphics g = Graphics.FromImage(newbitmap);
                g.DrawImage(img, new Rectangle(0, 0, sw, H), new Rectangle(W - tmpw, 0, sw, H), GraphicsUnit.Pixel);
                g.Dispose();
                nImage[i] = newbitmap;
                tmpw = tmpw - sw;
            }
            return nImage;
        }

        // ===================== 转图 / 签名 / 加密 =====================
        public static void ImageToPDF(Bitmap[] bitmaps, float bl, string trageFullName)
        {
            using (iTextSharp.text.Document document = new iTextSharp.text.Document(new iTextSharp.text.Rectangle(0, 0), 0, 0, 0, 0))
            {
                PdfWriter.GetInstance(document, new FileStream(trageFullName, FileMode.Create, FileAccess.ReadWrite));
                document.Open();
                for (int i = 0; i < bitmaps.Length; i++)
                {
                    iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(bitmaps[i], ImageFormat.Bmp);
                    float Width = image.Width * bl, Height = image.Height * bl;
                    image.ScaleToFit(Width, Height);
                    document.SetPageSize(new iTextSharp.text.Rectangle(0, 0, Width, Height));
                    document.NewPage();
                    document.Add(image);
                }
            }
        }

        public static void SignaturePDF(string inputPath, string outPath, X509Certificate2 cert)
        {
            PdfReader pdfReader = null;
            PdfStamper pdfStamper = null;
            try
            {
                pdfReader = new PdfReader(inputPath);
                pdfStamper = PdfStamper.CreateSignature(pdfReader, new FileStream(outPath, FileMode.Create), '\0', null, true);
                Org.BouncyCastle.X509.X509CertificateParser cp = new Org.BouncyCastle.X509.X509CertificateParser();
                Org.BouncyCastle.X509.X509Certificate[] chain = new Org.BouncyCastle.X509.X509Certificate[] { cp.ReadCertificate(cert.RawData) };

                AsymmetricCipherKeyPair pk = DotNetUtilities.GetKeyPair(cert.GetRSAPrivateKey());
                IExternalSignature externalSignature = new PrivateKeySignature(pk.Private, DigestAlgorithms.SHA256);

                PdfSignatureAppearance signatureAppearance = pdfStamper.SignatureAppearance;
                signatureAppearance.SignDate = DateTime.Now;
                MakeSignature.SignDetached(signatureAppearance, externalSignature, chain, null, null, null, 0, CryptoStandard.CMS);
            }
            finally
            {
                if (pdfStamper != null) pdfStamper.Close();
                if (pdfReader != null) pdfReader.Close();
            }
        }

        public static void EncryptPDF(string inputFilePath, string outputFilePath, string password)
        {
            try
            {
                using (FileStream fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    using (PdfReader reader = new PdfReader(inputFilePath))
                    {
                        using (iTextSharp.text.Document document = new iTextSharp.text.Document())
                        {
                            PdfEncryptor.Encrypt(reader, fs, true, password, password, PdfWriter.ALLOW_PRINTING);
                        }
                    }
                }
                File.Delete(inputFilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("添加密码保护时发生错误：" + ex.Message, ex);
            }
        }

        /// <summary>PDF 转图片后重新生成 PDF（"合并"输出模式：盖章不可编辑）。</summary>
        public static void PDFToiPDF(string pdfPath, int qmType, X509Certificate2 cert)
        {
            int dpi = 300;
            float bl = 72f / dpi;
            Bitmap[] bitmaps = null;
            string renderPath = pdfPath;
            try
            {
                renderPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(pdfPath);
                using (IPdfDocumentRenderer pdfRenderer = PdfiumDocumentRenderer.Open(renderPath))
                {
                    bitmaps = new Bitmap[pdfRenderer.PageCount];
                    for (int i = 0; i < pdfRenderer.PageCount; i++)
                    {
                        bitmaps[i] = pdfRenderer.RenderPage(i, dpi);
                    }
                }

                string tmpPdf = pdfPath;
                if (qmType != 0)
                {
                    tmpPdf = Path.GetTempPath() + "PDFQFZ_tmp.pdf";
                }

                ImageToPDF(bitmaps, bl, tmpPdf);

                if (qmType != 0 && cert != null)
                {
                    SignaturePDF(tmpPdf, pdfPath, cert);
                }
            }
            finally
            {
                if (bitmaps != null)
                {
                    foreach (Bitmap bitmap in bitmaps)
                    {
                        if (bitmap != null) bitmap.Dispose();
                    }
                }
                if (!string.Equals(renderPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                {
                    PreviewPdfPreparation.TryDelete(renderPath);
                }
            }
        }
    }
}
