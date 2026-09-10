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

                                // 随机旋转：位图未旋转（布局尺寸恒定=SizeMm），旋转在输出绘制层用 iText RotationDegrees 完成，
                                // 预览端用 WPF RenderTransform 旋转，两端角度一致。
                                bool drawRotate = placement.RandomRotation && placement.Rotation != 0;
                                if (drawRotate)
                                {
                                    placementImage.RotationDegrees = placement.Rotation;
                                }

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
                                // 随机位移（mm→PDF点）：放置时已固定随机值，预览与输出一致。
                                // iText 坐标系 y 向上为正，预览坐标系 y 向下为正，故 Y 方向取反。
                                placementX += placement.OffsetXmm * 72f / 25.4f;
                                placementY -= placement.OffsetYmm * 72f / 25.4f;
                                // 随机旋转章：旋转后包围盒（任意角度投影），用于出界移回与中心补偿
                                float boundW = placementWidth;
                                float boundH = placementHeight;
                                if (drawRotate)
                                {
                                    double rad = placement.Rotation * Math.PI / 180.0;
                                    double cosA = Math.Abs(Math.Cos(rad));
                                    double sinA = Math.Abs(Math.Sin(rad));
                                    boundW = (float)(placementWidth * cosA + placementHeight * sinA);
                                    boundH = (float)(placementWidth * sinA + placementHeight * cosA);
                                }
                                // 出界自动移回页面内（按维度：章子比页面还大时保持中心出界裁剪，水印大章不受影响）。
                                // 页面旋转 90/270 时 iText 坐标系同步旋转，可用长度相应交换。
                                if (pageRotation == 90 || pageRotation == 270)
                                {
                                    if (boundW <= pageSize.Height)
                                        placementX = Math.Min(Math.Max(placementX, 0f), pageSize.Height - boundW);
                                    if (boundH <= pageSize.Width)
                                        placementY = Math.Min(Math.Max(placementY, 0f), pageSize.Width - boundH);
                                }
                                else
                                {
                                    if (boundW <= pageSize.Width)
                                        placementX = Math.Min(Math.Max(placementX, 0f), pageSize.Width - boundW);
                                    if (boundH <= pageSize.Height)
                                        placementY = Math.Min(Math.Max(placementY, 0f), pageSize.Height - boundH);
                                }
                                // iText 旋转绕图像左下角：把左下角补偿到旋转后包围盒的左下角，保持章中心与未旋转时一致
                                if (drawRotate)
                                {
                                    placementX += (placementWidth - boundW) / 2f;
                                    placementY += (placementHeight - boundH) / 2f;
                                }
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

            // 盖章渲染：七维随机质感（明暗/斑块大小/渐变/局部露白/内部斑点/径向压印/整体色偏；
            // 放置时固定种子 → 预览刷新与输出纹理一致）
            if (placement.TextureEnabled
                && (placement.TextureBrightness > 0 || placement.TextureBlob > 0
                    || placement.TextureGradient > 0 || placement.TextureWhite > 0
                    || placement.TextureSpot > 0 || placement.TextureRadial > 0
                    || placement.TextureCast > 0))
            {
                processed = ApplyInkTexture(processed, placement.TextureSeed,
                    placement.TextureBrightness, placement.TextureBlob,
                    placement.TextureGradient, placement.TextureWhite,
                    placement.TextureSpot, placement.TextureRadial,
                    placement.TextureCast,
                    placement.TextureKb, placement.TextureKblob, placement.TextureKgrad,
                    placement.TextureKwhite, placement.TextureKspot,
                    placement.TextureKradial, placement.TextureKcast);
            }

            // 随机旋转的章：位图不旋转（布局尺寸恒定=SizeMm，宽高比不变，杜绝“同一批章有大有小”）；
            // 旋转在绘制层完成：预览用 WPF RenderTransform、输出用 iText RotationDegrees（与原版骑缝章随机旋转一致）。
            // 固定旋转：保持原位图旋转 + 切边/不切边选项（行为不变）。
            if (placement.Rotation != 0 && !placement.RandomRotation)
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

        /// <summary>盖章渲染：五维随机质感。每章以种子确定性派生：明暗幅度(0~上限)、
        /// 斑块大小(0~上限)、渐变方向与幅度(0~上限)、局部露白(0~上限)、内部斑点(0~上限，簇状)，
        /// 组合生成印泥纹理。强度上限已按用户要求整体加倍（100 档 ≈ 手工盖章强效果）。</summary>
        private static Bitmap ApplyInkTexture(Bitmap src, int seed,
            int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast,
            float kb, float kblob, float kgrad, float kwhite, float kspot,
            float kradial, float kcast)
        {
            // 分布随机（噪声/方向/斑点位置，固定于本章，与参数无关 → 调参时分布稳定）
            Random distRnd = new Random(seed);
            // 强度 = 本章固化系数(0.85~1.15，±15% 浮动) × 参数上限 → 参数主控、章与章微调
            double amp = kb * brightness / 100.0 * 2.4;                 // 印泥浓淡不均 0~2.4（最大值按 40% 档封顶）
            double blobLevel = kblob * blob / 100.0;                    // 斑点大小 0~1
            double gradAmp = kgrad * gradient / 100.0 * 1.0;            // 渐变幅度 0~1.0
            double whiteLevel = kwhite * white / 100.0;                 // 局部露白随滑块（分布随机）
            double radialLevel = kradial * radial / 100.0 * 2.0;        // 径向压印 0~2（最大值提高 2 倍）
            // 色调：参数 -100~+100，0=原色；负=偏冷(玫红/紫红)、正=偏暖(橘红/橙)；
            // 方向全局一致（同一批印泥同一色调）、幅度按章浮动；红章始终是红，只是冷暖微差
            double castT = Math.Abs(cast) / 100.0 * kcast;
            double hueShift = cast / 100.0 * 20.0;   // 100 档最多偏 20°（HSV 色相）
            if (amp < 0.005 && gradAmp < 0.005 && whiteLevel < 0.005 && spot <= 0
                && radialLevel < 0.005 && castT < 0.005)
            {
                return src;
            }
            int w = src.Width, h = src.Height;
            if (w < 8 || h < 8)
            {
                return src;
            }
            // 低频噪声源：网格尺寸由斑块大小决定（0 → 细碎 w/9；100 → 大块 w/16，加倍）
            int nw = Math.Max(6, (int)Math.Round(w / (9.0 + blobLevel * 63.0)));   // 斑块大小上限×9
            int nh = Math.Max(6, (int)Math.Round(h / (9.0 + blobLevel * 63.0)));
            Bitmap noise = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData nd = noise.LockBits(new Rectangle(0, 0, nw, nh),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] nb = new byte[nd.Stride * nh];
                for (int yy = 0; yy < nh; yy++)
                {
                    for (int xx = 0; xx < nw; xx++)
                    {
                        byte val = (byte)distRnd.Next(256);
                        int i = yy * nd.Stride + xx * 4;
                        nb[i] = val;
                        nb[i + 1] = val;
                        nb[i + 2] = val;
                        nb[i + 3] = 255;
                    }
                }
                Marshal.Copy(nb, 0, nd.Scan0, nb.Length);
            }
            finally
            {
                noise.UnlockBits(nd);
            }
            noise = BoxBlurGray(noise);
            // 放大到章尺寸（双三次 → 大块低频斑块）
            Bitmap bigNoise = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics gn = Graphics.FromImage(bigNoise))
            {
                gn.Clear(Color.Transparent);
                gn.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                gn.DrawImage(noise, 0, 0, w, h);
            }
            noise.Dispose();
            // 渐变方向（种子派生，固定于本章）
            double gradDir = gradient > 0 ? distRnd.NextDouble() * Math.PI * 2.0 : 0.0;
            double gx = Math.Cos(gradDir), gy = Math.Sin(gradDir);
            // 逐像素亮度调制：仅章内像素（alpha>0）生效，章外保持完全透明。
            // 渲染顺序：先画内部斑点（作为底层融入章面）→ 再整体逐像素调制，斑点与章面一体处理、不割裂。
            Bitmap dst = (Bitmap)src.Clone();
            System.Drawing.Imaging.BitmapData d1 = src.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            long sr = 0, sg = 0, sb = 0;
            int scnt = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            byte[] b1 = null;
            int stride1 = d1.Stride;
            try
            {
                b1 = new byte[d1.Stride * h];
                Marshal.Copy(d1.Scan0, b1, 0, b1.Length);
            }
            finally
            {
                src.UnlockBits(d1);
            }
            // 第一遍：扫描章面边界 + 统计平均色（供斑点采样）
            for (int yy = 0; yy < h; yy++)
            {
                int row1 = yy * stride1;
                for (int xx = 0; xx < w; xx++)
                {
                    int i1 = row1 + xx * 4;
                    if (b1[i1 + 3] == 0)
                    {
                        continue;
                    }
                    sr += b1[i1 + 2];   // R（内存序 B,G,R,A）
                    sg += b1[i1 + 1];   // G
                    sb += b1[i1];       // B
                    scnt++;
                    if (xx < minX) minX = xx;
                    if (xx > maxX) maxX = xx;
                    if (yy < minY) minY = yy;
                    if (yy > maxY) maxY = yy;
                }
            }
            // 内部斑点：章形内部随机簇状不规则颗粒（有的连片、有的孤立，模拟印泥颗粒）。
            // 颜色从章内像素随机采样（红章取红系；BGR 通道序已修正，不再发蓝）；
            // 允许落在印章原有红色上；画在章面下方（先画），后续整体调制与章面融为一体。
            if (spot > 0 && scnt > 0 && maxX >= minX && maxY >= minY)
            {
                byte mr = (byte)(sr / scnt);
                byte mg = (byte)(sg / scnt);
                byte mb = (byte)(sb / scnt);
                // 预采样章面颜色（最多 48 个），斑点随机取其一 → 各点颜色略异更真实
                List<Color> inkColors = new List<Color>();
                for (int s = 0; s < 48; s++)
                {
                    int sx = minX + distRnd.Next(maxX - minX + 1);
                    int sy = minY + distRnd.Next(maxY - minY + 1);
                    if (b1[sy * stride1 + sx * 4 + 3] != 0)
                    {
                        inkColors.Add(Color.FromArgb(255,
                            b1[sy * stride1 + sx * 4 + 2],   // R
                            b1[sy * stride1 + sx * 4 + 1],   // G
                            b1[sy * stride1 + sx * 4]));     // B
                    }
                }
                // 斑点密度 = 参数上限 × 本章系数（调参平滑变化、章与章不同）
                int clusterCount = (int)Math.Round(spot / 100.0 * 495.0 * kspot);
                // 斑点尺寸：最大值减半（blob=100 → 原 50 档大小），直径 2~12px、簇散布 ±8~±20
                double spotSizeScale = 1.0 + blobLevel * 1.5;
                using (Graphics gs = Graphics.FromImage(dst))
                {
                    for (int k = 0; k < clusterCount; k++)
                    {
                        int cx = minX + distRnd.Next(maxX - minX + 1);
                        int cy = minY + distRnd.Next(maxY - minY + 1);
                        // 簇中心必须在章形内部（四方向都能扫描到章面），防止撒到圆章外接方框角落
                        if (!IsInsideStamp(b1, stride1, cx, cy, minX, minY, maxX, maxY))
                        {
                            continue;
                        }
                        int clusterSize = 1 + distRnd.Next(5); // 1~5 个点：孤立或连片
                        int spread = Math.Max(2, (int)Math.Round(8.0 * spotSizeScale)); // 簇散布
                        for (int j = 0; j < clusterSize; j++)
                        {
                            int ox = cx + distRnd.Next(-spread, spread + 1);
                            int oy = cy + distRnd.Next(-spread, spread + 1);
                            if (ox < 0) ox = 0;
                            if (oy < 0) oy = 0;
                            if (ox >= w) ox = w - 1;
                            if (oy >= h) oy = h - 1;
                            // 偏移点必须在章形内部，避免斑点出界
                            if (!IsInsideStamp(b1, stride1, ox, oy, minX, minY, maxX, maxY))
                            {
                                continue;
                            }
                            int r = Math.Max(1, (int)Math.Round((1 + distRnd.Next(3)) * spotSizeScale));
                            int al = 100 + distRnd.Next(156);            // 半透明~实色
                            Color cc = inkColors.Count > 0
                                ? inkColors[distRnd.Next(inkColors.Count)]
                                : Color.FromArgb(255, mr, mg, mb);
                            using (SolidBrush br = new SolidBrush(Color.FromArgb(al, cc.R, cc.G, cc.B)))
                            {
                                // 不规则形状：随机 6~9 个顶点的多边形（半径 0.6~1.4r、顶点角度扰动），模拟印泥吸附不均的毛边
                                int verts = 6 + distRnd.Next(4);
                                System.Drawing.PointF[] pts = new System.Drawing.PointF[verts];
                                for (int v = 0; v < verts; v++)
                                {
                                    double ang = 2.0 * Math.PI * v / verts + (distRnd.NextDouble() - 0.5) * 0.8;
                                    double rr = r * (0.6 + distRnd.NextDouble() * 0.8);
                                    pts[v] = new System.Drawing.PointF(
                                        ox + (float)(Math.Cos(ang) * rr),
                                        oy + (float)(Math.Sin(ang) * rr));
                                }
                                gs.FillPolygon(br, pts);
                            }
                        }
                    }
                }
            }
            // 低频噪声整体调制（含斑点区域，斑点与章面一起被处理 → 融为一体）
            System.Drawing.Imaging.BitmapData d2 = dst.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData dn = bigNoise.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] b2 = new byte[d2.Stride * h];
            Marshal.Copy(d2.Scan0, b2, 0, b2.Length);   // 读入当前像素（含斑点混合色）
            byte[] bn = new byte[dn.Stride * h];
            Marshal.Copy(dn.Scan0, bn, 0, bn.Length);
            try
            {
                double blobBoost = 1.0 + blobLevel * 3.0;                     // 斑块强度：调大时明暗对比增强（1~4）
                double maxF = 1.0 + amp * blobBoost + 1.6 * whiteLevel + 0.6 * radialLevel;
                for (int yy = 0; yy < h; yy++)
                {
                    int row2 = yy * d2.Stride, rown = yy * dn.Stride;
                    for (int xx = 0; xx < w; xx++)
                    {
                        int i2 = row2 + xx * 4;
                        byte a = b2[i2 + 3];
                        if (a == 0)
                        {
                            continue;
                        }
                        double n = bn[rown + xx * 4] / 255.0; // 0~1
                        double f = 1.0 + (n - 0.5) * 2.0 * amp * blobBoost;
                        if (gradAmp > 0.0)
                        {
                            double t = (xx - w / 2.0) / w * gx + (yy - h / 2.0) / h * gy;
                            f *= 1.0 - gradAmp * Math.Max(0.0, t + 0.5);
                        }
                        // 局部露白：噪声亮区显著变淡（印泥薄处接近纸色），100 档效果明显
                        if (whiteLevel > 0.004 && n > 1.0 - whiteLevel * 0.85)
                        {
                            f *= 1.0 + 1.6 * whiteLevel * (0.5 + n * 0.5);
                        }
                        // 径向压印：中心不动、边缘平滑渐淡（模拟印泥边缘薄），系数 0.6
                        if (radialLevel > 0.004)
                        {
                            double dxx = (xx - w / 2.0) / (w / 2.0);
                            double dyy = (yy - h / 2.0) / (h / 2.0);
                            double dist = Math.Sqrt(dxx * dxx + dyy * dyy);
                            if (dist > 1.0) dist = 1.0;
                            f *= 1.0 + radialLevel * dist * 0.6;
                        }
                        if (f < 0.02) f = 0.02;
                        if (f > maxF) f = maxF;
                        // 读当前像素（含斑点混合色）整体调制
                        double rr = b2[i2 + 2] * f;
                        double gg = b2[i2 + 1] * f;
                        double bb = b2[i2] * f;
                        // 色调：HSV 色相微偏（正=偏暖/橘红，负=偏冷/玫红），饱和度与明度不变
                        if (castT > 0.004)
                        {
                            HsvShift(ref rr, ref gg, ref bb, hueShift);
                        }
                        b2[i2] = (byte)Math.Max(0, Math.Min(255, (int)bb));
                        b2[i2 + 1] = (byte)Math.Max(0, Math.Min(255, (int)gg));
                        b2[i2 + 2] = (byte)Math.Max(0, Math.Min(255, (int)rr));
                        // alpha 保持（含斑点混合后的透明度）
                    }
                }
                Marshal.Copy(b2, 0, d2.Scan0, b2.Length);
            }
            finally
            {
                dst.UnlockBits(d2);
                bigNoise.UnlockBits(dn);
            }
            src.Dispose();            src.Dispose();
            bigNoise.Dispose();
            return dst;
        }

        /// <summary>盖章渲染实时预览：对章原图应用当前参数（固定种子 → 斑块分布稳定，仅强度随参数变化）。</summary>
        public static Bitmap RenderTexturePreview(Bitmap src, int seed,
            int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast,
            float kb, float kblob, float kgrad, float kwhite, float kspot,
            float kradial, float kcast)
        {
            return ApplyInkTexture(src, seed, brightness, blob, gradient, white, spot, radial, cast,
                kb, kblob, kgrad, kwhite, kspot, kradial, kcast);
        }

        /// <summary>HSV 色相微偏：模拟印泥色调差异（红章偏橘=暖 / 偏玫红=冷），饱和度与明度不变。
        /// r/g/b 为 0~255 域，dh 为色相偏移角度（正=偏暖、负=偏冷）。</summary>
        private static void HsvShift(ref double r, ref double g, ref double b, double dh)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double v = max;
            double s = max > 0 ? delta / max : 0.0;
            double h = 0.0;
            if (delta > 0.0001)
            {
                if (max == r) h = 60.0 * (((g - b) / delta) % 6.0);
                else if (max == g) h = 60.0 * ((b - r) / delta + 2.0);
                else h = 60.0 * ((r - g) / delta + 4.0);
                if (h < 0) h += 360.0;
            }
            h += dh;
            h %= 360.0;
            if (h < 0) h += 360.0;
            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;
            double r1 = 0, g1 = 0, b1 = 0;
            if (h < 60) { r1 = c; g1 = x; }
            else if (h < 120) { r1 = x; g1 = c; }
            else if (h < 180) { g1 = c; b1 = x; }
            else if (h < 240) { g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; b1 = c; }
            else { r1 = c; b1 = x; }
            r = r1 + m;
            g = g1 + m;
            b = b1 + m;
        }

        /// <summary>判定点是否位于章形状内部（四方向扫描均能遇到章面 alpha>0）。
        /// 用于将内部斑点限制在章内空白处，避免撒到圆章外接方框角落等章外区域。</summary>
        private static bool IsInsideStamp(byte[] b1, int stride, int x, int y,
            int minX, int minY, int maxX, int maxY)
        {
            bool left = false;
            for (int xx = x - 1; xx >= minX; xx--)
            {
                if (b1[y * stride + xx * 4 + 3] != 0) { left = true; break; }
            }
            if (!left) return false;
            bool right = false;
            for (int xx = x + 1; xx <= maxX; xx++)
            {
                if (b1[y * stride + xx * 4 + 3] != 0) { right = true; break; }
            }
            if (!right) return false;
            bool top = false;
            for (int yy = y - 1; yy >= minY; yy--)
            {
                if (b1[yy * stride + x * 4 + 3] != 0) { top = true; break; }
            }
            if (!top) return false;
            bool bottom = false;
            for (int yy = y + 1; yy <= maxY; yy++)
            {
                if (b1[yy * stride + x * 4 + 3] != 0) { bottom = true; break; }
            }
            return bottom;
        }

        /// <summary>灰度噪声 3x3 均值模糊（更柔和的低频斑块）。</summary>
        private static Bitmap BoxBlurGray(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Bitmap dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData d1 = src.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            System.Drawing.Imaging.BitmapData d2 = dst.LockBits(new Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] b1 = new byte[d1.Stride * h];
                byte[] b2 = new byte[d2.Stride * h];
                Marshal.Copy(d1.Scan0, b1, 0, b1.Length);
                for (int yy = 0; yy < h; yy++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        int sum = 0, cnt = 0;
                        for (int ky = -1; ky <= 1; ky++)
                        {
                            int ny = yy + ky;
                            if (ny < 0 || ny >= h) continue;
                            for (int kx = -1; kx <= 1; kx++)
                            {
                                int nx = xx + kx;
                                if (nx < 0 || nx >= w) continue;
                                sum += b1[ny * d1.Stride + nx * 4];
                                cnt++;
                            }
                        }
                        byte val = (byte)(sum / cnt);
                        int o = yy * d2.Stride + xx * 4;
                        b2[o] = val;
                        b2[o + 1] = val;
                        b2[o + 2] = val;
                        b2[o + 3] = 255;
                    }
                }
                Marshal.Copy(b2, 0, d2.Scan0, b2.Length);
            }
            finally
            {
                src.UnlockBits(d1);
                dst.UnlockBits(d2);
            }
            src.Dispose();
            return dst;
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

        /// <summary>PDF 转图片后重新生成 PDF（"合并"输出模式：盖章不可编辑）。
        /// dpi 由输出清晰度档位决定（极高300/高200/标准150/低96/极低72），默认标准 150。</summary>
        public static void PDFToiPDF(string pdfPath, int qmType, X509Certificate2 cert, int dpi = 150)
        {
            if (dpi < 72) dpi = 72;
            if (dpi > 600) dpi = 600;
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
