using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 带历史下拉和框内清除按钮的输入控件（标准样式）。
    /// - 输入框右侧框内有一个圆形"×"清除按钮（有文字时显示，点击清空）
    /// - 点击输入框弹出历史下拉（无焦点，输入框仍可继续输入/删除）
    /// - 下拉每条历史右侧带圆形"×"删除按钮
    /// 历史数据通过 HistoryProvider / HistoryDelete 与外部配置读写对接。
    /// </summary>
    public sealed class HistoryInputControl : UserControl
    {
        private readonly TableLayoutPanel layout;
        private readonly TextBox inner;
        private readonly InlineClearButton clear;
        private readonly HistoryPopupForm popup;
        private readonly PopupDismissMessageFilter dismissFilter;

        /// <summary>读取历史列表（最新在前）的委托。</summary>
        public Func<IEnumerable<string>> HistoryProvider { get; set; }

        /// <summary>删除一条历史的委托。</summary>
        public Action<string> HistoryDelete { get; set; }

        public HistoryInputControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Window;
            DoubleBuffered = true;
            Height = 24;

            layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(1),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22F));

            inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 2, 0, 2),
                Padding = new Padding(3, 1, 22, 1)
            };

            clear = new InlineClearButton
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Margin = new Padding(2, 4, 4, 4)
            };
            clear.Cleared += (s, e) =>
            {
                inner.Text = string.Empty;
                inner.Focus();
            };

            layout.Controls.Add(inner, 0, 0);
            layout.Controls.Add(clear, 1, 0);
            Controls.Add(layout);

            popup = new HistoryPopupForm(Font, OnHistorySelected, OnHistoryDeleted);
            dismissFilter = new PopupDismissMessageFilter(
                p => popup.Bounds.Contains(p) || RectangleToScreen(ClientRectangle).Contains(p));
            dismissFilter.OutsideClick += HidePopup;

            inner.Click += (s, e) => ShowHistory();
            inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down)
                {
                    ShowHistory();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    HidePopup();
                }
            };
            inner.TextChanged += (s, e) =>
            {
                clear.Visible = inner.Text.Length > 0;
                TextContentChanged?.Invoke(this, EventArgs.Empty);
            };
            clear.Visible = false;
        }

        /// <summary>输入框文字变化事件。</summary>
        public event EventHandler TextContentChanged;

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

        private void ShowHistory()
        {
            List<string> items = (HistoryProvider?.Invoke() ?? Enumerable.Empty<string>()).ToList();
            if (items.Count == 0)
            {
                HidePopup();
                return;
            }

            if (popup.Visible)
            {
                popup.UpdateItems(items);
                return;
            }

            Point screen = inner.PointToScreen(new Point(0, inner.Height + 1));
            int width = Math.Max(inner.Width, 160);
            int height = Math.Min(items.Count * popup.ItemHeight + 8, 240);
            popup.ShowAt(screen, new Size(width, height), items);
            Application.AddMessageFilter(dismissFilter);
        }

        private void HidePopup()
        {
            if (popup.Visible)
            {
                popup.Hide();
                Application.RemoveMessageFilter(dismissFilter);
            }
        }

        private void OnHistorySelected(string text)
        {
            HidePopup();
            inner.Text = text;
            inner.Select(text.Length, 0);
            inner.Focus();
        }

        private void OnHistoryDeleted(string text)
        {
            HistoryDelete?.Invoke(text);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(inner.Focused ? SystemColors.Highlight : Color.FromArgb(171, 173, 179)))
            {
                Rectangle r = ClientRectangle;
                r.Width--;
                r.Height--;
                e.Graphics.DrawRectangle(pen, r);
            }
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
            Size = new Size(16, 16);
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

    /// <summary>无焦点历史下拉窗体（不抢输入框焦点，输入框可继续输入）。</summary>
    internal sealed class HistoryPopupForm : Form
    {
        private readonly ListBox list;
        private readonly Action<string> onSelect;
        private readonly Action<string> onDelete;

        public HistoryPopupForm(Font font, Action<string> onSelect, Action<string> onDelete)
        {
            this.onSelect = onSelect;
            this.onDelete = onDelete;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(171, 173, 179);   // 1px 边框色
            Padding = new Padding(1);

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 28,
                IntegralHeight = false,
                BackColor = SystemColors.Window,
                Font = font
            };
            list.DrawItem += DrawItemHandler;
            list.MouseDown += MouseDownHandler;
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Hide();
                }
            };
            Controls.Add(list);
        }

        public int ItemHeight
        {
            get { return list.ItemHeight; }
        }

        /// <summary>无焦点弹出：不激活、不抢输入框焦点。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public void ShowAt(Point screenLocation, Size size, IEnumerable<string> items)
        {
            list.Items.Clear();
            foreach (string item in items)
            {
                list.Items.Add(item);
            }

            SetBounds(screenLocation.X, screenLocation.Y, size.Width, size.Height);
            Rectangle workArea = Screen.GetWorkingArea(screenLocation);
            if (Right > workArea.Right)
            {
                Left = workArea.Right - Width;
            }
            if (Bottom > workArea.Bottom)
            {
                Top = workArea.Bottom - Height;
            }

            Show();
            list.Invalidate();
        }

        public void UpdateItems(IEnumerable<string> items)
        {
            list.Items.Clear();
            foreach (string item in items)
            {
                list.Items.Add(item);
            }
            list.Invalidate();
        }

        private void DrawItemHandler(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || list.Items.Count == 0)
            {
                return;
            }

            string text = list.Items[e.Index].ToString();
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using (SolidBrush bg = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Window))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                list.Font,
                new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 32, e.Bounds.Height),
                selected ? SystemColors.HighlightText : SystemColors.WindowText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧圆形删除叉
            Rectangle circle = GetDeleteCircle(e.Bounds);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush cb = new SolidBrush(selected ? Color.FromArgb(230, 230, 230) : Color.FromArgb(200, 200, 200)))
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

        private void MouseDownHandler(object sender, MouseEventArgs e)
        {
            int index = list.IndexFromPoint(e.Location);
            if (index < 0 || index >= list.Items.Count)
            {
                return;
            }

            string keyword = list.Items[index].ToString();
            Rectangle bounds = list.GetItemRectangle(index);
            Rectangle circle = GetDeleteCircle(bounds);
            circle.Inflate(3, 3);

            if (circle.Contains(e.Location))
            {
                // 点击删除叉：从历史中移除并同步配置
                onDelete?.Invoke(keyword);
                list.Items.RemoveAt(index);
                if (list.Items.Count == 0)
                {
                    Hide();
                }
                else
                {
                    list.Invalidate();
                }
            }
            else
            {
                // 点击文字：选择该历史
                onSelect?.Invoke(keyword);
            }
        }

        private static Rectangle GetDeleteCircle(Rectangle bounds)
        {
            return new Rectangle(bounds.Right - 24, bounds.Top + (bounds.Height - 18) / 2, 18, 18);
        }
    }

    /// <summary>全局鼠标过滤：点击下拉与输入控件之外时关闭下拉。</summary>
    internal sealed class PopupDismissMessageFilter : IMessageFilter
    {
        private readonly Func<Point, bool> isInside;

        /// <summary>点击到外部时触发。</summary>
        public event Action OutsideClick;

        public PopupDismissMessageFilter(Func<Point, bool> isInside)
        {
            this.isInside = isInside;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == 0x201 || m.Msg == 0x204 || m.Msg == 0x207)   // 鼠标左/右/中键按下
            {
                if (!isInside(Cursor.Position))
                {
                    OutsideClick?.Invoke();
                }
            }
            return false;
        }
    }
}
