using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 带历史下拉和框内清除按钮的输入控件。
    /// 历史下拉使用 WinForms 系统原生菜单容器 ToolStripDropDown：
    /// - hover 高亮、滚动、键盘上下选择 + 回车确认、Esc / 点击外部关闭均由系统自动处理（标准菜单交互规范）
    /// - 每条历史右侧带圆形"×"删除按钮
    /// 输入框内右侧提供圆形"×"清除按钮（有文字时显示，点击一键清空）。
    /// </summary>
    public sealed class HistoryInputControl : UserControl
    {
        private readonly TextBox inner;
        private readonly InlineClearButton clear;
        private readonly ToolStripDropDown dropDown;

        /// <summary>读取历史列表（最新在前）的委托。</summary>
        public Func<IEnumerable<string>> HistoryProvider { get; set; }

        /// <summary>删除一条历史的委托。</summary>
        public Action<string> HistoryDelete { get; set; }

        /// <summary>输入框文字变化事件。</summary>
        public event EventHandler TextContentChanged;

        public HistoryInputControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Window;
            DoubleBuffered = true;
            Height = 24;

            inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(3, 1, 20, 1)   // 右侧留出清除按钮空间
            };

            clear = new InlineClearButton
            {
                Size = new Size(16, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabStop = false
            };
            clear.Cleared += (s, e) =>
            {
                inner.Text = string.Empty;
                inner.Focus();
            };

            Controls.Add(inner);
            Controls.Add(clear);
            LayoutClearButton();

            Resize += (s, e) => LayoutClearButton();

            inner.Click += (s, e) => ShowHistoryDropDown();
            inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down)
                {
                    ShowHistoryDropDown();
                    e.Handled = true;
                }
            };
            inner.TextChanged += (s, e) =>
            {
                clear.Visible = inner.Text.Length > 0;
                TextContentChanged?.Invoke(this, EventArgs.Empty);
            };
            clear.Visible = false;

            dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = new Padding(0)
            };
        }

        /// <summary>输入框当前文字。</summary>
        public new string Text
        {
            get { return inner.Text; }
            set { inner.Text = value ?? string.Empty; }
        }

        /// <summary>设置输入框文字（程序预填时使用）。</summary>
        public void SetText(string value)
        {
            inner.Text = value ?? string.Empty;
        }

        /// <summary>让输入框获得焦点。</summary>
        public void FocusInput()
        {
            inner.Focus();
        }

        private void LayoutClearButton()
        {
            clear.Left = Width - clear.Width - 6;
            clear.Top = (Height - clear.Height) / 2;
        }

        private void ShowHistoryDropDown()
        {
            List<string> items = (HistoryProvider?.Invoke() ?? Enumerable.Empty<string>()).ToList();
            if (items.Count == 0)
            {
                if (dropDown.Visible)
                {
                    dropDown.Close();
                }
                return;
            }

            int width = Math.Max(inner.Width, 200);
            dropDown.Items.Clear();
            foreach (string text in items)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(text)
                {
                    AutoSize = false,
                    Height = 26,
                    Width = width - 2
                };
                item.Paint += MenuItem_Paint;
                item.MouseDown += MenuItem_MouseDown;
                dropDown.Items.Add(item);
            }

            if (dropDown.Visible)
            {
                dropDown.Invalidate();
                return;
            }

            dropDown.Width = width;
            dropDown.Show(inner, new Point(0, inner.Height + 1));
        }

        private void MenuItem_Paint(object sender, PaintEventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Rectangle r = e.ClipRectangle;
            bool selected = item.Selected;

            using (SolidBrush bg = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Menu))
            {
                e.Graphics.FillRectangle(bg, r);
            }

            TextRenderer.DrawText(
                e.Graphics,
                item.Text,
                item.Font,
                new Rectangle(r.Left + 8, r.Top, r.Width - 34, r.Height),
                selected ? SystemColors.HighlightText : SystemColors.MenuText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧圆形删除叉
            Rectangle circle = GetDeleteCircle(r);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(selected ? Color.FromArgb(235, 235, 235) : Color.FromArgb(205, 205, 205)))
            {
                e.Graphics.FillEllipse(cb, circle);
            }

            using (Pen cp = new Pen(selected ? Color.Black : Color.White, 1.4f))
            {
                int pad = 5;
                e.Graphics.DrawLine(cp, circle.Left + pad, circle.Top + pad, circle.Right - pad, circle.Bottom - pad);
                e.Graphics.DrawLine(cp, circle.Right - pad, circle.Top + pad, circle.Left + pad, circle.Bottom - pad);
            }
        }

        private void MenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Rectangle r = new Rectangle(0, 0, item.Width, item.Height);
            Rectangle del = GetDeleteCircle(r);
            del.Inflate(3, 3);

            if (del.Contains(e.Location))
            {
                // 点击删除叉：删除该条历史并同步配置
                string keyword = item.Text;
                HistoryDelete?.Invoke(keyword);
                item.Owner.Items.Remove(item);
                if (dropDown.Items.Count == 0)
                {
                    dropDown.Close();
                }
                else
                {
                    dropDown.Invalidate();
                }
            }
            else
            {
                // 点击文字：选择该历史填入输入框
                string keyword = item.Text;
                inner.Text = keyword;
                inner.Select(keyword.Length, 0);
                inner.Focus();
                dropDown.Close();
            }
        }

        private static Rectangle GetDeleteCircle(Rectangle r)
        {
            return new Rectangle(r.Right - 26, r.Top + (r.Height - 18) / 2, 18, 18);
        }
    }

    /// <summary>框内圆形"×"清除按钮（自绘，浅灰圆底 + 白色叉）。</summary>
    internal sealed class InlineClearButton : Control
    {
        private bool hovered;

        /// <summary>用户点击清除按钮。</summary>
        public event EventHandler Cleared;

        public InlineClearButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle r = ClientRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(hovered ? Color.FromArgb(200, 200, 200) : Color.FromArgb(182, 182, 182)))
            {
                e.Graphics.FillEllipse(bg, r);
            }

            using (Pen pen = new Pen(Color.White, 1.5f))
            {
                int pad = 5;
                e.Graphics.DrawLine(pen, r.Left + pad, r.Top + pad, r.Right - pad, r.Bottom - pad);
                e.Graphics.DrawLine(pen, r.Right - pad, r.Top + pad, r.Left + pad, r.Bottom - pad);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Cleared?.Invoke(this, EventArgs.Empty);
        }
    }
}
