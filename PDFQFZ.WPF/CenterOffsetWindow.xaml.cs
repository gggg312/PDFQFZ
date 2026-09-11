using System;
using System.Windows;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 中心偏移弹窗（非模态、可放大）：与随机角度与位移弹窗同规范。
    /// 横向/纵向偏移 ±100mm，拖动实时回调主窗口并随当前搜索文字记忆；
    /// 关闭即保存（无确定/取消），已打开时由主窗口激活复用。
    /// </summary>
    public partial class CenterOffsetWindow : Window
    {
        private readonly Action<int, int> _onChange;
        private readonly Action<string> _onHint;   // 拖动条操作提示：传该参数的功能说明（操作哪个提示哪个）

        public CenterOffsetWindow(int offsetX, int offsetY, Action<int, int> onChange, Action<string> onHint = null)
        {
            InitializeComponent();
            _onChange = onChange;
            _onHint = onHint;
            rowOffsetX.Value = Clamp(offsetX, -100, 100);
            rowOffsetY.Value = Clamp(offsetY, -100, 100);

            rowOffsetX.ValueChanged += Row_ValueChanged;
            rowOffsetY.ValueChanged += Row_ValueChanged;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void Row_ValueChanged(object sender, EventArgs e)
        {
            if (_onChange != null)
            {
                _onChange((int)rowOffsetX.Value, (int)rowOffsetY.Value);
            }
            if (_onHint != null)
            {
                _onHint(sender == rowOffsetX
                    ? "横向偏移：盖章位置相对识别点向左/右偏移的距离"
                    : "纵向偏移：盖章位置相对识别点向上/下偏移的距离");
            }
        }
    }
}
