using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
        private readonly Action<int, bool> _onPresetChange;  // 参数号变化回调（idx 0=自定义/1-4=方案N；saved=true 保存/false 加载）
        private readonly Action<string> _onHint;             // 参数名提示回调（操作哪个提示哪个，传"参数名：值"）
        private int _presetIndex = 0;                  // 当前参数号（随章记忆）
        private bool _loadingPreset;                   // 加载参数赋值中，避免被认作手动修改而清除选中
        private bool _updating;   // 斑点→斑块联动中，避免重复回调
        private bool _suppressHint;  // 批量赋值（加载参数/恢复默认）中，抑制逐条参数提示，避免覆盖"已加载参数N"

        public TextureSettingsWindow(int brightness, int blob, int gradient, int white, int spot,
            int radial, int cast, int presetIndex, Action<int, int, int, int, int, int, int> onChange,
            Action<int, bool> onPresetChange, Action<string> onHint = null)
        {
            InitializeComponent();
            _onChange = onChange;
            _onPresetChange = onPresetChange;
            _onHint = onHint;
            _presetIndex = presetIndex;

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

            // 双保险选中态恢复（V137）：传入的参数号无效（0=自定义）时，
            // 用当前 7 个参数值与已保存参数 1~4 比对，完全一致则自动选中该参数。
            // 覆盖"保存后标志位丢失/未同步"场景，保证重开弹窗时"当前参数"提示正确。
            if (_presetIndex < 1 || _presetIndex > 4)
            {
                int detected = DetectPresetFromCurrentValues();
                if (detected != 0)
                {
                    _presetIndex = detected;
                }
            }
            ApplyPresetSelection(_presetIndex);
        }

        /// <summary>用当前 7 个参数值与已保存参数 1~4 比对，完全一致返回对应下标（0=无匹配）。</summary>
        private int DetectPresetFromCurrentValues()
        {
            int[] cur =
            {
                (int)rowBrightness.Value, (int)rowBlob.Value, (int)rowGradient.Value,
                (int)rowWhite.Value, (int)rowSpot.Value, (int)rowRadial.Value, (int)rowCast.Value
            };
            for (int i = 1; i <= 4; i++)
            {
                if (!AppConfig.TexPresetExists(i))
                {
                    continue;
                }
                int[] v = AppConfig.GetTexPreset(i);
                if (v != null && v.Length == cur.Length)
                {
                    bool same = true;
                    for (int k = 0; k < cur.Length; k++)
                    {
                        if (v[k] != cur[k])
                        {
                            same = false;
                            break;
                        }
                    }
                    if (same)
                    {
                        return i;
                    }
                }
            }
            return 0;
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
            // 手动修改参数（非加载方案赋值）→ 清除参数选中，回到自定义
            // （NumericSliderRow V140 起值变化去重，初始化假触发不再广播到这里）
            if (!_loadingPreset && _presetIndex != 0)
            {
                _presetIndex = 0;
                ApplyPresetSelection(0);
                if (_onPresetChange != null)
                {
                    _onPresetChange(0, false);
                }
            }
            FireChange();
            // 操作哪个提示哪个：用户手动拖动/改值时提示该参数的功能说明（批量赋值被 _suppressHint 抑制）
            if (!_suppressHint && _onHint != null)
            {
                string desc = HintDesc(sender);
                if (desc != null)
                {
                    _onHint(desc);
                }
            }
        }

        /// <summary>参数名 → 功能说明映射（操作提示区显示：该参数是什么、管什么用）。</summary>
        private string HintDesc(object sender)
        {
            if (sender == rowBrightness) return "印泥浓淡不均：随机模拟印泥深浅不均的痕迹";
            if (sender == rowGradient) return "渐变：模拟印泥在章面上的渐变过渡效果";
            if (sender == rowRadial) return "径向压印：模拟盖章压力从中心向外扩散、边缘渐浅的效果";
            if (sender == rowSpot) return "内部斑点：在印章内部随机添加细小墨点，模拟印泥颗粒";
            if (sender == rowBlob) return "斑点大小：控制印章内部墨点的大小";
            if (sender == rowCast) return "色调：整体调整印章颜色冷暖";
            if (sender == rowWhite) return "局部露白：模拟印章个别部位印泥不足的露白效果";
            return null;
        }

        private int[] GetCurrentValues()
        {
            return new int[]
            {
                (int)rowBrightness.Value, (int)rowBlob.Value, (int)rowGradient.Value,
                (int)rowWhite.Value, (int)rowSpot.Value, (int)rowRadial.Value, (int)rowCast.Value
            };
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

        /// <summary>渲染参数按钮（参数1~4）：点击弹出菜单——保存当前参数 / 加载参数（未保存过则加载置灰）。</summary>
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
            // 加载项与参数存储状态绑定：有参数=正常色可点；无参数=禁用灰字。
            // 实现：Header 直接用 TextBlock 并显式设置 Foreground（本地值，绝对生效）。
            // 不用模板触发器/TextElement 继承（动态菜单场景不可靠，历史反复 5 次不生效），见规范 §7.1。
            bool exists = AppConfig.TexPresetExists(idx);
            var loadText = new TextBlock { Text = "加载参数" };
            loadText.Foreground = exists
                ? (System.Windows.Media.Brush)Application.Current.Resources["Color.Text"]
                : (System.Windows.Media.Brush)Application.Current.Resources["Color.TextHint"];
            var miLoad = new MenuItem { Header = loadText, IsEnabled = exists };
            miSave.Click += (s2, e2) => SavePreset(idx);
            miLoad.Click += (s2, e2) => LoadPreset(idx);
            menu.Items.Add(miSave);
            menu.Items.Add(miLoad);
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>保存当前参数到参数：
        /// ① 与其他已存参数完全一致 → 弹窗询问是否切换到该参数（不覆盖保存）；
        /// ② 与自身已存参数一致 → 不弹窗，直接提示并保持选中；
        /// ③ 与自身已存参数不同 → 弹窗确认覆盖。
        /// 保存成功/自重复后切换选中态并回调主窗口（saved=true）。</summary>
        private void SavePreset(int idx)
        {
            int[] cur =
            {
                (int)rowBrightness.Value, (int)rowBlob.Value, (int)rowGradient.Value,
                (int)rowWhite.Value, (int)rowSpot.Value, (int)rowRadial.Value, (int)rowCast.Value
            };

            // ① 重复检测：与其他已存参数对比，完全一致 → 询问切换
            for (int i = 1; i <= 4; i++)
            {
                if (i == idx || !AppConfig.TexPresetExists(i))
                {
                    continue;
                }
                int[] v = AppConfig.GetTexPreset(i);
                if (v != null && ArraysEqual(v, cur))
                {
                    var r = MessageBox.Show("当前参数与“参数" + i + "”已保存的参数一致，是否切换到参数" + i + "？",
                        "参数重复", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r == MessageBoxResult.Yes)
                    {
                        LoadPreset(i);
                    }
                    return;
                }
            }

            // ② 与自身已存参数一致 → 不弹窗，直接提示并保持选中
            if (AppConfig.TexPresetExists(idx))
            {
                int[] v = AppConfig.GetTexPreset(idx);
                if (v != null && ArraysEqual(v, cur))
                {
                    _presetIndex = idx;
                    ApplyPresetSelection(idx);
                    if (_onPresetChange != null)
                    {
                        _onPresetChange(idx, true);
                    }
                    return;
                }
                // ③ 与自身已存参数不同 → 确认覆盖
                var r = MessageBox.Show("参数" + idx + " 已保存过参数，是否覆盖？", "确认覆盖",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            AppConfig.SetTexPreset(idx, cur);
            _presetIndex = idx;
            ApplyPresetSelection(idx);
            if (_onPresetChange != null)
            {
                _onPresetChange(idx, true);
            }
        }

        private static bool ArraysEqual(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>加载参数到当前章（拖动条赋值 → 实时回调主窗口保存到当前章参数）。</summary>
        private void LoadPreset(int idx)
        {
            int[] v = AppConfig.GetTexPreset(idx);
            if (v == null) return;
            _loadingPreset = true;
            _suppressHint = true;   // 批量赋值不逐条提示，由 _onPresetChange 提示"已加载参数N"
            try
            {
                rowBrightness.Value = v[0];
                rowBlob.Value = v[1];
                rowGradient.Value = v[2];
                rowWhite.Value = v[3];
                rowSpot.Value = v[4];
                rowRadial.Value = v[5];
                rowCast.Value = v[6];
            }
            finally
            {
                _loadingPreset = false;
                _suppressHint = false;
            }
            ApplySpotBlobLink();
            _presetIndex = idx;
            ApplyPresetSelection(idx);
            if (_onPresetChange != null)
            {
                _onPresetChange(idx, false);
            }
        }

        /// <summary>恢复默认：全部参数为 0（不启用任何质感渲染，ValueChanged 自动实时生效）。</summary>
        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            _suppressHint = true;   // 批量赋值不逐条提示
            rowBrightness.Value = 0;
            rowGradient.Value = 0;
            rowWhite.Value = 0;
            rowSpot.Value = 0;
            rowRadial.Value = 0;
            rowCast.Value = 0;
            rowBlob.Value = 0;
            _suppressHint = false;
            ApplySpotBlobLink();
            _presetIndex = 0;
            ApplyPresetSelection(0);
            if (_onPresetChange != null)
            {
                _onPresetChange(0, false);
            }
        }

        /// <summary>参数选中态：对应按钮切换为选中样式，并更新"当前参数"小灰字。</summary>
        private void ApplyPresetSelection(int idx)
        {
            var selected = (Style)FindResource("Button.PresetSelected");
            var normal = (Style)FindResource("Button.Secondary");
            btnPreset1.Style = idx == 1 ? selected : normal;
            btnPreset2.Style = idx == 2 ? selected : normal;
            btnPreset3.Style = idx == 3 ? selected : normal;
            btnPreset4.Style = idx == 4 ? selected : normal;
            txtCurrentPreset.Text = idx >= 1 && idx <= 4 ? "当前参数：参数" + idx : "当前参数：自定义";
        }
    }
}
