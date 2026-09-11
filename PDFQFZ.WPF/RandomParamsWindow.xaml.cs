using System;
using System.Windows;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 随机角度与位移弹窗（非模态、可放大）：与盖章渲染弹窗同规范。
    /// 角度 0-360°、位移 0-200mm，拖动实时回调主窗口并随当前章保存；
    /// 关闭即保存（无确定/取消），已打开时由主窗口激活复用。
    /// </summary>
    public partial class RandomParamsWindow : Window
    {
        private readonly Action<int, int> _onChange;
        private readonly Action<string> _onHint;   // 拖动条操作提示：传该参数的功能说明（操作哪个提示哪个）

        public RandomParamsWindow(int angle, int offset, Action<int, int> onChange, Action<string> onHint = null)
        {
            InitializeComponent();
            _onChange = onChange;
            _onHint = onHint;
            rowAngle.Value = Clamp(angle, 0, 360);
            rowOffset.Value = Clamp(offset, 0, 200);

            rowAngle.ValueChanged += Row_ValueChanged;
            rowOffset.ValueChanged += Row_ValueChanged;
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
                _onChange((int)rowAngle.Value, (int)rowOffset.Value);
            }
            if (_onHint != null)
            {
                _onHint(sender == rowAngle
                    ? "角度：盖章时随机旋转角度的范围"
                    : "位移：盖章时随机偏移位置的最大距离");
            }
        }
    }
}
