using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PDFQFZ.WPF.Controls
{
    /// <summary>
    /// 拖动条规范控件：标题(上) + 滑块行(下，滑块 + 数值框 + 上下箭头)，两行一体（Windows NumericUpDown 标准调节按钮）。
    /// 滑块拖动、数值框手动输入、上下箭头步进（按住连续）、点击轨道跳转，四种方式统一同步。
    /// 外部读写 Value；数值变化时触发 ValueChanged（实时生效用）。
    /// 输入规则：仅数字（Minimum &lt; 0 时允许负号），失焦/点击外部自动按内容提交。
    /// </summary>
    public partial class NumericSliderRow : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(NumericSliderRow),
                new PropertyMetadata(""));   // 显示由 XAML Binding 负责，见 §0.7 初始化规范

        /// <summary>功能说明文字（标题下小灰字，§4.12 拖动条规范 V141）：为空自动隐藏。</summary>
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(NumericSliderRow),
                new PropertyMetadata(""));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(NumericSliderRow),
                new PropertyMetadata(0.0, (d, e) => ((NumericSliderRow)d).OnValuePropChanged((double)e.NewValue)));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(NumericSliderRow),
                new PropertyMetadata(0.0, (d, e) => ((NumericSliderRow)d).OnMinMaxChanged((double)e.NewValue, false)));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(NumericSliderRow),
                new PropertyMetadata(100.0, (d, e) => ((NumericSliderRow)d).OnMinMaxChanged((double)e.NewValue, true)));

        /// <summary>数值变化（拖动/输入/箭头任一路径）触发。</summary>
        public event EventHandler ValueChanged;

        private bool _internal;   // 防回环
        private double _lastNotified = double.NaN;   // 上次已广播的值：相同值不广播（去重初始化假触发）

        public NumericSliderRow()
        {
            InitializeComponent();
        }

        /// <summary>Minimum/Maximum 变化同步到 Slider；模板未加载时由 Loaded 兜底应用。</summary>
        private void OnMinMaxChanged(double v, bool isMax)
        {
            if (sld == null) return;
            if (isMax) sld.Maximum = v; else sld.Minimum = v;
        }

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>外部设置 Value → 同步滑块与数值框并通知（用户正在编辑时保留输入内容，失焦提交）。</summary>
        private void OnValuePropChanged(double v)
        {
            if (_internal) return;
            _internal = true;
            try
            {
                sld.Value = v;
                if (!txtValue.IsKeyboardFocused)
                {
                    txtValue.Text = ((int)Math.Round(v)).ToString();
                }
            }
            finally
            {
                _internal = false;
            }
            NotifyValueChanged();
        }

        /// <summary>滑块拖动 → 同步数值框并通知（以 Slider 实际值为准，防 DP 不同步）。</summary>
        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_internal) return;
            _internal = true;
            try
            {
                Value = Math.Round(sld.Value);
                if (!txtValue.IsKeyboardFocused)
                {
                    txtValue.Text = ((int)Math.Round(sld.Value)).ToString();
                }
            }
            finally
            {
                _internal = false;
            }
            NotifyValueChanged();
        }

        /// <summary>
        /// 值变化去重广播：仅当数值与上次广播值不同才触发 ValueChanged。
        /// 背景（V140）：窗口 Show 后 Slider/TextBox 初始化（Loaded/模板应用）会回写一次相同值，
        /// 旧实现无条件广播被当作"用户手动修改"，导致弹窗内"参数选中态"刚恢复即被清除。
        /// 真实操作值必然变化（30→35→40 均广播；拖回 35 时上次广播 40，仍广播），不受影响。
        /// </summary>
        private void NotifyValueChanged()
        {
            double v = Value;
            if (v == _lastNotified)
            {
                return;
            }
            _lastNotified = v;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>上下箭头：步进 1（按住 RepeatButton 自动连续），超界自动截断。</summary>
        private void OnSpinClick(object sender, RoutedEventArgs e)
        {
            int step = "up".Equals(((RepeatButton)sender).Tag as string) ? 1 : -1;
            Value = Clamp((int)Value + step, (int)Minimum, (int)Maximum);
        }

        /// <summary>输入过滤：仅数字（Minimum&lt;0 允许负号）。</summary>
        private void OnValueBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            string candidate = tb.Text.Substring(0, tb.SelectionStart) + e.Text
                + tb.Text.Substring(tb.SelectionStart + tb.SelectionLength);
            if (!IsValidNumberText(candidate)) e.Handled = true;
        }

        /// <summary>粘贴过滤：仅数字内容可粘贴。</summary>
        private void OnValueBoxPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!IsValidNumberText(text)) e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }

        /// <summary>输入过程允许空/负号前缀；最终必须可解析为整数（拒绝小数、科学计数法等）。</summary>
        private bool IsValidNumberText(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            if (s == "-") return Minimum < 0;
            return int.TryParse(s, out _);
        }

        /// <summary>数值框回车提交。</summary>
        private void OnValueBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitEdit();
                ((TextBox)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        /// <summary>失焦提交：非法/超界自动取最近合法值，滑块联动。</summary>
        private void OnValueBoxLostFocus(object sender, RoutedEventArgs e)
        {
            CommitEdit();
        }

        /// <summary>按数值框内容提交到 Value：仅数字；解析失败用旧值；超界自动钳制到最近合法值并回显。
        /// 提交（回车/失焦/点击外部）＝结束编辑，必须把输入框回显为钳制后的值，防止残留"999"。</summary>
        private void CommitEdit()
        {
            if (!int.TryParse(txtValue.Text.Trim(), out int v))
            {
                v = (int)Value;
            }
            int clamped = Clamp(v, (int)Minimum, (int)Maximum);
            Value = clamped;   // 触发 OnValuePropChanged：同步滑块并通知
            _internal = true;
            try
            {
                txtValue.Text = clamped.ToString();   // 强制回显钳制值（不论焦点状态）
            }
            finally
            {
                _internal = false;
            }
        }

        /// <summary>点击窗口任意位置（数值框以外）→ 自动退出编辑并提交，保证点击空白/滑块也能保存。</summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // 兜底应用 Minimum/Maximum（模板未加载时 DP 回调可能被跳过），并刷新数值框
            sld.Minimum = Minimum;
            sld.Maximum = Maximum;
            if (!txtValue.IsKeyboardFocused)
            {
                txtValue.Text = ((int)Math.Round(Value)).ToString();
            }
            var win = Window.GetWindow(this);
            if (win != null) win.PreviewMouseDown += Window_PreviewMouseDown;
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            var win = Window.GetWindow(this);
            if (win != null) win.PreviewMouseDown -= Window_PreviewMouseDown;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!txtValue.IsKeyboardFocused) return;
            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src == txtValue) return;   // 点在数值框内：不打断输入
                src = VisualTreeHelper.GetParent(src);
            }
            CommitEdit();
            Keyboard.ClearFocus();
        }

        /// <summary>点击轨道（非滑块区域）直接跳到点击位置。</summary>
        private void Slider_TrackJump(object sender, MouseButtonEventArgs e)
        {
            var slider = sender as Slider;
            if (slider == null || e.LeftButton != MouseButtonState.Pressed) return;
            var track = slider.Template.FindName("PART_Track", slider) as Track;
            if (track == null || track.Thumb == null) return;
            System.Windows.Point p = e.GetPosition(track);
            System.Windows.Point thumbOrigin = track.Thumb.TranslatePoint(new System.Windows.Point(0, 0), track);
            if (p.X >= thumbOrigin.X - 2 && p.X <= thumbOrigin.X + track.Thumb.ActualWidth + 2)
            {
                return; // 点在滑块上：交给默认拖拽
            }
            double value = track.ValueFromPoint(p);
            if (double.IsNaN(value)) return;
            slider.Value = Math.Min(Math.Max(value, slider.Minimum), slider.Maximum);
            e.Handled = true;
        }
    }
}
