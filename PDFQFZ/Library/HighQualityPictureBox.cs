using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 高质量缩放显示的图片框：使用双三次插值渲染图片，
    /// 替代 PictureBox 默认的低质量缩放，使 PDF 预览文字在缩放显示时更清晰锐利。
    /// 行为与 PictureBoxSizeMode.Zoom 一致：等比缩放并居中。
    /// </summary>
    internal sealed class HighQualityPictureBox : PictureBox
    {
        public HighQualityPictureBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            Image img = Image;
            Graphics g = pe.Graphics;
            if (img == null)
            {
                g.Clear(BackColor);
                return;
            }

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;

            float scale = Math.Min(
                (float)ClientSize.Width / img.Width,
                (float)ClientSize.Height / img.Height);
            if (scale <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                g.Clear(BackColor);
                return;
            }

            int destWidth = Math.Max(1, (int)Math.Round(img.Width * scale));
            int destHeight = Math.Max(1, (int)Math.Round(img.Height * scale));
            int destX = Math.Max(0, (ClientSize.Width - destWidth) / 2);
            int destY = Math.Max(0, (ClientSize.Height - destHeight) / 2);

            g.Clear(BackColor);
            g.DrawImage(
                img,
                new Rectangle(destX, destY, destWidth, destHeight),
                new Rectangle(0, 0, img.Width, img.Height),
                GraphicsUnit.Pixel);
        }
    }
}
