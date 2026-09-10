using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using PDFQFZ.WPF.Controls;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 盖章渲染参数弹窗（非模态、可放大）：七个维度上限（0-100），修改实时生效并回调主窗口；
    /// 调试在预览区直接盖章查看（统一种子=1，参数全 0 即干净原图）。
    /// 内部斑点 = 0 时，斑块大小联动置 0 并禁用（斑块大小属于斑点类效果）。
    /// 参数行使用拖动条规范控件 NumericSliderRow（滑块 + 数值框 + 上下箭头）。
    /// </summary>
    public partial class TextureSettingsWindow : Window
    {
        private readonly Action<int, int, int, int, int, int, int> _onChange;
        private bool _updating;   // 斑点→斑块联动中，避免重复回调

        public TextureSettingsWindow(int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast, Action<int, int, int, int, int, int, int> onChange)
        {
            InitializeComponent();
            _onChange = onChange;

            rowBrightness.Value = Clamp(brightness);
            rowBlob.Value = Clamp(blob);
            rowGradient.Value = Clamp(gradient);
            rowWhite.Value = Clamp(white);
            rowSpot.Value = Clamp(spot);
            rowRadial.Value = Clamp(radial);
            rowCast.Value = ClampCast(cast);

            // 统一订阅：任一拖动条变化 → 斑点联动 + 实时回调
            rowBrightness.ValueChanged += Row_ValueChanged;
            rowBlob.ValueChanged += Row_ValueChanged;
            rowGradient.ValueChanged += Row_ValueChanged;
            rowWhite.ValueChanged += Row_ValueChanged;
            rowSpot.ValueChanged += Row_ValueChanged;
            rowRadial.ValueChanged += Row_ValueChanged;
            rowCast.ValueChanged += Row_ValueChanged;

            ApplySpotBlobLink();
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 100) return 100;
            return v;
        }

        /// <summary>整体色偏双向：-100~+100（0=原色）。</summary>
        private static int ClampCast(int v)
        {
            if (v < -100) return -100;
            if (v > 100) return 100;
            return v;
        }

        /// <summary>内部斑点=0 → 斑块大小联动置 0 并禁用；内部斑点>0 → 斑块大小启用。</summary>
        private void ApplySpotBlobLink()
        {
            _updating = true;
            try
            {
                if ((int)rowSpot.Value == 0)
                {
                    rowBlob.Value = 0;
                    rowBlob.IsEnabled = false;
                }
                else
                {
                    rowBlob.IsEnabled = true;
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private void Row_ValueChanged(object sender, EventArgs e)
        {
            if (_updating)
            {
                return;
            }
            if (sender == rowSpot)
            {
                ApplySpotBlobLink();
            }
            FireChange();
        }

        private void FireChange()
        {
            if (_onChange != null)
            {
                _onChange(
                    (int)rowBrightness.Value, (int)rowBlob.Value, (int)rowGradient.Value,
                    (int)rowWhite.Value, (int)rowSpot.Value, (int)rowRadial.Value, (int)rowCast.Value);
            }
        }

        /// <summary>渲染方案按钮（方案1~4）：点击弹出菜单——保存当前参数 / 加载方案参数（未保存过则加载置灰）。</summary>
        private void BtnPreset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null)
            {
                return;
            }
            int idx;
            if (!int.TryParse(btn.Tag != null ? btn.Tag.ToString() : "", out idx))
            {
                return;
            }
            var menu = new ContextMenu();
            var miSave = new MenuItem { Header = "保存当前参数" };
            var miLoad = new MenuItem { Header = "加载方案参数", IsEnabled = AppConfig.TexPresetExists(idx) };
            miSave.Click += (s2, e2) => SavePreset(idx);
            miLoad.Click += (s2, e2) => LoadPreset(idx);
            menu.Items.Add(miSave);
            menu.Items.Add(miLoad);
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>保存当前参数到方案（已有方案则确认覆盖）。</summary>
        private void SavePreset(int idx)
        {
            if (AppConfig.TexPresetExists(idx))
            {
                var r = MessageBox.Show("方案" + idx + " 已保存过参数，是否覆盖？", "确认覆盖",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            AppConfig.SetTexPreset(idx, new[]
            {
                (int)rowBrightness.Value, (int)rowBlob.Value, (int)rowGradient.Value,
                (int)rowWhite.Value, (int)rowSpot.Value, (int)rowRadial.Value, (int)rowCast.Value
            });
        }

        /// <summary>加载方案参数到当前章（拖动条赋值 → 实时回调主窗口保存到当前章参数）。</summary>
        private void LoadPreset(int idx)
        {
            int[] v = AppConfig.GetTexPreset(idx);
            if (v == null) return;
            rowBrightness.Value = v[0];
            rowBlob.Value = v[1];
            rowGradient.Value = v[2];
            rowWhite.Value = v[3];
            rowSpot.Value = v[4];
            rowRadial.Value = v[5];
            rowCast.Value = v[6];
            ApplySpotBlobLink();
        }

        /// <summary>恢复默认：全部参数为 0（不启用任何质感渲染，ValueChanged 自动实时生效）。</summary>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            rowBrightness.Value = 0;
            rowGradient.Value = 0;
            rowWhite.Value = 0;
            rowSpot.Value = 0;
            rowRadial.Value = 0;
            rowCast.Value = 0;
            rowBlob.Value = 0;
            ApplySpotBlobLink();
        }
    }
}
