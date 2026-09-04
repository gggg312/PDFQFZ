using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 带历史下拉的输入控件。
    /// 历史下拉使用 WinForms 系统原生菜单容器 ToolStripDropDown：
    /// - hover 高亮、滚动、键盘上下选择 + 回车确认、Esc / 点击外部关闭均由系统自动处理（标准菜单交互规范）
    /// - 每条历史右侧带轻量的系统文字"×"删除按钮
    /// 说明：WinForms 输入框没有系统自带的清除按钮，故输入框保持普通样式，由用户手动编辑/删除文字。
    /// </summary>
    public sealed class HistoryInputControl : UserControl
    {
        private readonly TextBox inner;
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
                Padding = new Padding(3, 1, 2, 1)
            };

            Controls.Add(inner);

            inner.Click += (s, e) => ShowHistoryDropDown();
            inner.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down)
                {
                    ShowHistoryDropDown();
                    e.Handled = true;
                }
            };
            inner.TextChanged += (s, e) => TextContentChanged?.Invoke(this, EventArgs.Empty);

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
                new Rectangle(r.Left + 8, r.Top, r.Width - 30, r.Height),
                selected ? SystemColors.HighlightText : SystemColors.MenuText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧轻量删除叉：系统文字"×"，无底色，hover 时变亮
            Rectangle delRect = GetDeleteRect(r);
            TextRenderer.DrawText(
                e.Graphics,
                "×",
                item.Font,
                delRect,
                selected ? Color.FromArgb(235, 235, 235) : Color.FromArgb(150, 150, 150),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void MenuItem_MouseDown(object sender, MouseEventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Rectangle r = new Rectangle(0, 0, item.Width, item.Height);
            Rectangle del = GetDeleteRect(r);
            del.Inflate(2, 2);

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

        private static Rectangle GetDeleteRect(Rectangle r)
        {
            return new Rectangle(r.Right - 22, r.Top, 18, r.Height);
        }
    }
}
