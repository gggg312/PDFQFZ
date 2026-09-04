using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 带历史下拉的输入控件（纯系统原生实现，无任何自绘）。
    /// 采用 WinForms 系统原生的"可编辑下拉框"（ComboBox，DropDown 样式）：
    /// - 控件本体是系统原生样式，与普通输入框外观一致
    /// - 展开历史时，输入框内依然可以打字、删字（系统原生行为）
    /// - 下拉列表为系统原生展开，自动支持滚动、键盘选择、Esc/点击外部关闭
    /// - 列表内容为最近使用的盖章文字（由 HistoryProvider 提供，最新在前，最多 10 条），点击即填入输入框
    /// </summary>
    public sealed class HistoryInputControl : UserControl
    {
        private readonly ComboBox combo;

        /// <summary>读取历史列表（最新在前）的委托。</summary>
        public Func<IEnumerable<string>> HistoryProvider { get; set; }

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
                DrawMode = DrawMode.Normal,               // 系统原生绘制，零自绘
                IntegralHeight = false,
                DropDownHeight = 220,
                FlatStyle = FlatStyle.Standard            // 系统原生 3D 边框，与普通输入框一致
            };
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
    }
}
