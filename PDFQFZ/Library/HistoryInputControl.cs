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
            combo.MouseDown += Combo_MouseDown;
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

        private bool deleteClickPending;
        private string pendingText;

        // 点击展开列表中的项，需在 MouseDown 阶段判断删除区：
        // 若点在"×"上则删除该条历史，并阻止系统把该行内容填入输入框。
        private void Combo_MouseDown(object sender, MouseEventArgs e)
        {
            if (!combo.DroppedDown)
            {
                return;
            }

            int idx = HitTestItem(e.Location);
            if (idx < 0 || idx >= combo.Items.Count)
            {
                return;
            }

            Rectangle itemRect = new Rectangle(0, combo.ClientSize.Height + 1 + idx * combo.ItemHeight, combo.Width, combo.ItemHeight);
            Rectangle delRect = GetDeleteRect(itemRect);
            delRect.Inflate(3, 3);
            if (!delRect.Contains(e.Location))
            {
                return;
            }

            // 点击删除区：删除该条历史，并还原输入框文字（删除不应改变输入框内容）
            string keyword = combo.Items[idx].ToString();
            string current = combo.Text;
            HistoryDelete?.Invoke(keyword);
            deleteClickPending = true;
            pendingText = current;
            RebuildItems();
            combo.SelectedIndex = -1;
            combo.Text = current;
        }

        private void Combo_MouseUp(object sender, MouseEventArgs e)
        {
            if (!deleteClickPending)
            {
                return;
            }

            deleteClickPending = false;
            // 兜底：系统可能在点击时把被点项填入了输入框，这里还原为点击前的文字
            if (!string.Equals(combo.Text, pendingText, StringComparison.Ordinal))
            {
                combo.Text = pendingText;
            }

            // 保持下拉展开，方便连续删除多条
            if (!combo.DroppedDown && combo.Items.Count > 0)
            {
                combo.DroppedDown = true;
            }
        }

        private int HitTestItem(Point location)
        {
            int top = combo.ClientSize.Height + 1;
            return (location.Y - top) / Math.Max(1, combo.ItemHeight);
        }

        private static Rectangle GetDeleteRect(Rectangle r)
        {
            return new Rectangle(r.Right - 20, r.Top + (r.Height - 16) / 2, 16, 16);
        }
    }
}
