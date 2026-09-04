using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 带历史下拉的输入控件。
    /// 采用 WinForms 系统原生的"可编辑下拉框"（ComboBox，DropDown 样式）实现：
    /// - 控件本体是系统原生样式，与普通输入框外观一致
    /// - 展开历史时，输入框内依然可以打字、删字（系统原生行为）
    /// - 下拉列表为系统原生展开，自动支持滚动、键盘选择、Esc/点击外部关闭
    /// - 每条历史右侧带轻量的系统文字"×"删除按钮（OwnerDraw 绘制，外观仍是原生下拉）
    /// </summary>
    public sealed class HistoryInputControl : UserControl
    {
        private readonly ComboBox combo;

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
            BackColor = SystemColors.Control;
            DoubleBuffered = true;
            Height = 23;

            combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,   // 可编辑
                DrawMode = DrawMode.OwnerDrawFixed,        // 每项绘制文字 + 右侧 ×
                IntegralHeight = false,
                DropDownHeight = 220,
                FlatStyle = FlatStyle.Standard             // 系统原生 3D 边框，与普通输入框一致
            };
            combo.DrawItem += Combo_DrawItem;
            combo.MouseUp += Combo_MouseUp;
            combo.DropDown += (s, e) => RebuildItems();
            combo.TextChanged += (s, e) => TextContentChanged?.Invoke(this, EventArgs.Empty);

            Controls.Add(combo);
        }

        /// <summary>输入框当前文字。</summary>
        public new string Text
        {
            get { return combo.Text; }
            set { combo.Text = value ?? string.Empty; }
        }

        /// <summary>设置输入框文字（程序预填时使用）。</summary>
        public void SetText(string value)
        {
            combo.Text = value ?? string.Empty;
        }

        /// <summary>让输入框获得焦点。</summary>
        public void FocusInput()
        {
            combo.Focus();
        }

        /// <summary>当前是否处于展开状态（供外部判断）。</summary>
        public bool IsDroppedDown
        {
            get { return combo.DroppedDown; }
        }

        /// <summary>收起下拉。</summary>
        public void CloseDropDown()
        {
            if (combo.DroppedDown)
            {
                combo.DroppedDown = false;
            }
        }

        private void RebuildItems()
        {
            string current = combo.Text;
            List<string> items = (HistoryProvider?.Invoke() ?? Enumerable.Empty<string>()).ToList();
            combo.Items.Clear();
            foreach (string text in items)
            {
                combo.Items.Add(text);
            }
            // 可编辑下拉：填充 Items 不应改变用户正在输入/已有的文字
            if (!string.Equals(combo.Text, current, StringComparison.Ordinal))
            {
                combo.Text = current;
            }
        }

        private void Combo_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= combo.Items.Count)
            {
                return;
            }

            string text = combo.Items[e.Index]?.ToString() ?? string.Empty;
            Rectangle r = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using (SolidBrush bg = new SolidBrush(selected ? SystemColors.Highlight : SystemColors.Window))
            {
                e.Graphics.FillRectangle(bg, r);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                e.Font,
                new Rectangle(r.Left + 2, r.Top, r.Width - 24, r.Height),
                selected ? SystemColors.HighlightText : SystemColors.WindowText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧轻量删除叉：系统文字"×"，无底色，hover 时变亮
            Rectangle delRect = GetDeleteRect(r);
            TextRenderer.DrawText(
                e.Graphics,
                "×",
                e.Font,
                delRect,
                selected ? Color.FromArgb(235, 235, 235) : Color.FromArgb(150, 150, 150),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void Combo_MouseUp(object sender, MouseEventArgs e)
        {
            if (!combo.DroppedDown)
            {
                return;
            }

            // 点击位置换算到展开列表中的项：列表从下拉框底部开始展开
            int top = combo.Height + 1;
            int idx = (e.Y - top) / Math.Max(1, combo.ItemHeight);
            if (idx < 0 || idx >= combo.Items.Count)
            {
                return;
            }

            Rectangle itemRect = new Rectangle(0, top + idx * combo.ItemHeight, combo.Width, combo.ItemHeight);
            Rectangle delRect = GetDeleteRect(itemRect);
            delRect.Inflate(2, 2);
            if (!delRect.Contains(e.Location))
            {
                return;
            }

            string keyword = combo.Items[idx].ToString();
            HistoryDelete?.Invoke(keyword);
            RebuildItems();
            // 删除后保持下拉展开，方便继续删除
            if (!combo.DroppedDown)
            {
                combo.DroppedDown = true;
            }
        }

        private static Rectangle GetDeleteRect(Rectangle r)
        {
            return new Rectangle(r.Right - 20, r.Top + (r.Height - 16) / 2, 16, 16);
        }
    }
}
