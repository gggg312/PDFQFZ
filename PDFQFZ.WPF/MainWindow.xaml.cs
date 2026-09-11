using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PDFQFZ.Library;
using PDFQFZ.WPF.Services;

namespace PDFQFZ.WPF
{
    /// <summary>
    /// 主窗口。WPF 重写版：界面按设计蓝本（卡片式）实现，业务核心复用
    /// PDFQFZ.Library + StampEngine（iTextSharp 盖章 / PDFium 渲染找字）。
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int RenderDpi = 144;
        private const int CustomPlacementStampType = 1;   // 手动点击盖章
        private const int SpecifiedPageStampType = 2;     // 指定范围页盖章

        private string sourcePath;             // 预览/盖章源（文件模式当前文件）
        private string[] sourceFiles;          // 文件模式待处理列表
        private IPdfDocumentRenderer pdfRenderer;
        private PageCache<Bitmap> pageCache;
        private int pageCount;
        private int currentPageIndex;          // 0-based

        private readonly StampPlacementCollection stampPlacements = new StampPlacementCollection();
    private bool _debugPageActive;          // 无 PDF 时预览区显示空白调试页，可盖章调试渲染参数
        private readonly List<AutoStampOperation> autoStampOperations = new List<AutoStampOperation>();
        private readonly Dictionary<int, System.Windows.Controls.Image> overlayImages =
            new Dictionary<int, System.Windows.Controls.Image>();

        private PDFQFZ.Library.PageRange specifiedPageRange;
        private bool specifiedRangeFirstClickPending;
        private int activeSpecifiedBatchId;
        private bool isGenerating;

        // 预览显示换算（px）
        private double displayWidth;
        private double displayHeight;

        // 预览缩放 / 拖动
        private double zoomPercent;               // 放大视图缩放百分比（100-300）
        private double basePageWidthPx;           // 当前页 100% 宽（px）
        private double basePageHeightPx;          // 当前页 100% 高（px）
        private double fitWidthPx;                // 整页适应预览区的宽（px）
        private double fitHeightPx;               // 整页适应预览区的高（px）
        private PreviewViewMode previewViewMode = PreviewViewMode.SinglePage; // 单页/双页/放大视图
        // 双页视图：两页并排间距（px）与左右页显示尺寸
        private const double DoublePageGap = 12;
        private double doublePageLeftW;
        private double doublePageLeftH;
        private double doublePageRightW;
        private double doublePageRightH;
        /// <summary>翻页步进：双页视图一次翻一个跨页（2 页），单页/放大视图逐页。</summary>
        private int PageStep => previewViewMode == PreviewViewMode.DoublePage ? 2 : 1;
        // 预览拖动（对齐原版固定起点式：按下时记录鼠标起点与滚动偏移快照，移动时一次性计算并夹取，避免增量累加在边界处跳动）
        private System.Windows.Point panStartMouse;
        private double panStartOffsetX;
        private double panStartOffsetY;
        private bool isDraggingPreview;
        private bool zoomInitialized;             // 首次加载已计算 fit 基准
        private int previewWheelDeltaRemainder;   // 单页视图滚轮翻页累积
        private int wheelPageScrollMode;          // 放大视图滚轮连续翻页后的滚动位置：0=按比例恢复 / 1=滚到顶部 / 2=滚到底部

        // 保存阶段动态提示（对齐原版：省略号动画 + 已用时）
        private System.Windows.Threading.DispatcherTimer savingTimer;
        private DateTime savingStartTime;
        private int savingDotPhase;

        // 源类型：true=目录模式（拖入/选择文件夹），false=文件模式（拖入/选择文件）
        private bool currentSourceIsDirectory;
        // 程序化填充“当前文件”下拉时抑制 SelectionChanged，避免重复加载
        private bool suppressCurrentFileEvent;

        // 日志区帮助文字（对齐原版 InitialHelpText）：初始只含帮助文字时，首次写日志先清空
        private bool logContainsOnlyHelp = true;

        // 日志区是否钉在底部：追加日志时滚到底；用户手动向上滚动看历史时取消钉底，不强制拉回
        private bool _logPinToBottom = true;

        // 当前选中的印章文件名（用于按印章分别保存/恢复参数）
        private string _currentStampFileName = "";
        /// <summary>印章条目列表（内存数据源，含显示名/路径/勾选状态）。</summary>
        private readonly List<StampPickerItem> _stampItems = new List<StampPickerItem>();
        private bool _suppressStampSelection = false; // Rebuild 设置 SelectedItem 时抑制 SelectionChanged 递归
        /// <summary>勾选印章的显示名集合（按勾选顺序；主章=第一个）。</summary>
        private readonly List<string> _selectedStampNames = new List<string>();
        private bool _maxSplitUserModified;        // 用户是否手动改过骑缝章分割数（自动重置不写入印章记忆）
        private bool _suppressMaxSplitTrack;       // 代码赋值 txtMaxSplit 时抑制 TextChanged 标记
        private bool _suppressStampParamSave;      // ApplyStampParams 加载参数到界面时抑制反向保存
        private bool _suppressCenterOffsetSync;   // 代码赋值中心偏移控件时抑制反向保存
        private bool _layoutTestMode;             // /layouttest 命令行模式：自动遍历窗口高度断言布局，写 layout_test.log 后退出

        public MainWindow(string[] args)
        {
            InitializeComponent();
            // 窗口标题自动携带完整版本（程序集 4 段版本号，如 2.3.1.114），避免打开后不知道是哪版
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            Title = $"PDF盖页面章与骑缝章工具（V{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision} GG优化版）";
            logText.Text = InitialHelpText;   // 初始显示帮助说明（对齐原版）
            logContainsOnlyHelp = true;
            LoadConfigToUi();
            InitFoldState();   // 需在 LoadConfigToUi（内含 AppConfig.LoadFromIni）之后，才能按配置恢复折叠状态
            HookEvents();
            HookAutoKeywordWatermark();
            HookContextFilterEvents();
            HookCenterOffsetEvents();
            UpdateAutoKeywordWatermark();

            // 滚动条：点击轨道直接跳转到点击位置（统一交互）
            settingsScroll.PreviewMouseLeftButtonDown += ScrollViewer_TrackJump;
            logScroll.PreviewMouseLeftButtonDown += ScrollViewer_TrackJump;
            // 日志区：内容重排（换行/滚动条出现改变视口）后自动补滚到底，避免最后一行被视口裁掉
            logScroll.ScrollChanged += LogScroll_ScrollChanged;

            if (args != null && args.Length > 0 && File.Exists(args[0]))
            {
                LoadPdf(args[0]);
            }

            // 布局回归测试模式：命令行带 /layouttest 时自动遍历窗口高度断言布局数值，
            // 结果写入运行目录 layout_test.log 后自动退出（退出前恢复原窗口尺寸，不污染用户配置）。
            foreach (var a in args ?? new string[0])
            {
                if (string.Equals(a, "/layouttest", StringComparison.OrdinalIgnoreCase))
                {
                    _layoutTestMode = true;
                    break;
                }
            }
            if (_layoutTestMode)
                Dispatcher.BeginInvoke(new Action(RunLayoutTest), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===================== 初始化 =====================
        private void InitFoldState()
        {
            // 按配置恢复 3/4/5 的展开收起状态（config.ini 关闭时保存，首次默认：按文字盖章展开、印章参数/其他设置折叠）
            ApplyFoldState(autoTextContent, foldAutoTextArrow, foldAutoTextText, autoTextHeaderGrid, btnFoldAutoText, AppConfig.FoldAutoText == 1);
            ApplyFoldState(sealParamsContent, foldSealParamsArrow, foldSealParamsText, sealParamsHeaderGrid, btnFoldSealParams, AppConfig.FoldSealParams == 1);
            ApplyFoldState(otherContent, foldOtherArrow, foldOtherText, otherHeaderGrid, btnFoldOther, AppConfig.FoldOther == 1);
        }

        /// <summary>按指定展开状态设置折叠区域（内容可见性、箭头、文字、展开红字提醒、标题行间距）。</summary>
        /// <remarks>收起态：标题行底部间距归零，标题行在卡片内上下居中，收起高度更紧凑；展开态：恢复标题与内容的 10px 间距。</remarks>
        private static void ApplyFoldState(System.Windows.Controls.StackPanel content,
                                           System.Windows.Shapes.Path arrow,
                                           System.Windows.Controls.TextBlock text,
                                           System.Windows.Controls.Grid header,
                                           System.Windows.Controls.Button btn,
                                           bool expanded)
        {
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            header.Margin = expanded ? new Thickness(0, 0, 0, 10) : new Thickness(0);
            // 折叠箭头（Icon.FoldArrow=▼）：展开态=▼ 原样；折叠态=同图向左旋转 90° 显示 ▶
            arrow.RenderTransform = expanded ? null : new System.Windows.Media.RotateTransform(-90);
            text.Text = expanded ? "收起设置" : "展开设置";
            // 状态色：展开态（收起设置）=红字红箭头红边框提醒可收起；收起态（展开设置）=蓝字蓝箭头蓝边框（与标准按钮一致）
            System.Windows.Media.Brush brush;
            if (expanded)
                brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0x33, 0x33)); // 红 #CC3333
            else
                brush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Color.Primary"); // 蓝 #1677FF
            arrow.Fill = brush;
            text.Foreground = brush;
            btn.BorderBrush = brush;
        }

        /// <summary>把 config.ini 的值填入界面控件。</summary>
        private void LoadConfigToUi()
        {
            AppConfig.LoadFromIni();
            try
            {
                if (AppConfig.WindowWidth > 0) Width = AppConfig.WindowWidth;
                if (AppConfig.WindowHeight > 0) Height = AppConfig.WindowHeight;
                if (AppConfig.WindowLeft > -1 && AppConfig.WindowTop > -1)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = AppConfig.WindowLeft;
                    Top = AppConfig.WindowTop;
                }

                // 窗口自适应：防止小屏幕或旧配置导致窗口超出屏幕、标题栏跑到屏幕外拖不回来。
                // 逻辑：尺寸超过所在屏幕工作区→压缩到屏幕内；位置在屏幕外→回到屏幕中央。
                FitWindowToScreen();

                // 盖章方式：0=不盖 / 1=手动 / 2=指定范围（WPF 指定范围用按钮，映射到手动）
                comboPageStamp.SelectedIndex = AppConfig.YzType == 0 ? 0 : 1;

                // 骑缝章：显示顺序"不加/加盖/单页/双页/随意"，业务值 1/0/2/3/4
                comboSeam.SelectedIndex = SeamDisplayToBusiness(AppConfig.QfzType);

                // 输出效果：显示"合并/叠加"，业务值 1/0
                comboOutput.SelectedIndex = AppConfig.QfzType == 0 && AppConfig.WjType == 0 ? 0 : 0;

                txtStampSize.Text = AppConfig.Size.ToString();
                txtRotation.Text = AppConfig.Rotation.ToString();
                comboRotationHandle.SelectedIndex = AppConfig.QbFlag;
                txtOpacity.Text = AppConfig.Opacity.ToString();
                chkRandomParams.IsChecked = true;
                UpdateRandomEnabled();
                chkRemoveWhite.IsChecked = false;   // 默认关闭去除白色背景
                txtTolerance.Text = "20";
                UpdateToleranceEnabled();
                comboSeamPosition.SelectedIndex = AppConfig.WzType;
                txtSeamPosPct.Text = AppConfig.WzPercent.ToString();
                txtMaxSplit.Text = AppConfig.MaxFgs.ToString();

                // 输出清晰度：300/200/150/96/72 -> 索引 0/1/2/3/4，默认标准 150
                comboOutputQuality.SelectedIndex = AppConfig.OutputQualityDpi >= 300 ? 0 :
                    AppConfig.OutputQualityDpi >= 200 ? 1 :
                    AppConfig.OutputQualityDpi >= 150 ? 2 :
                    AppConfig.OutputQualityDpi >= 96 ? 3 : 4;
                UpdateOutputQualityEnabled();

                // V145：输出目录锁定——恢复上次锁定的目录与锁定状态（勾选框文字随 IsChecked 自动置灰/恢复）
                if (!string.IsNullOrWhiteSpace(AppConfig.OutputDir))
                    txtOutputDir.Text = AppConfig.OutputDir;
                chkOutputDirLock.IsChecked = AppConfig.OutputDirLocked == 1;

                // 按文字盖章——上下文过滤（从配置恢复）
                chkContextFilter.IsChecked = AppConfig.ContextFilterEnabled;
                txtContextKeywords.Text = string.IsNullOrEmpty(AppConfig.ContextKeywords) ? "盖章,公章" : AppConfig.ContextKeywords;
                txtContextRange.Text = AppConfig.ContextRange.ToString();
                comboContextMatch.SelectedIndex = (AppConfig.ContextMatch == 1) ? 1 : 0;
                chkContextExcludeSpaces.IsChecked = AppConfig.ContextExcludeSpaces;
                UpdateContextFilterEnabled();
                UpdateContextKeywordsWatermark();

                comboSignature.SelectedIndex = AppConfig.QmType;
                txtSignature.Text = AppConfig.QmType == 1 ? AppConfig.SignBuiltInPath : AppConfig.SignCustomPath;
                txtSignaturePass.Password = AppConfig.QmType == 1 ? AppConfig.SignBuiltInPass : AppConfig.SignCustomPass;
                UpdateSignatureEnabled();

                // 输出文件名格式自定义（V132）
                comboOutputNamePos.SelectedIndex = AppConfig.OutputNamePos == 1 ? 1 : 0;
                txtOutputNameMark.Text = string.IsNullOrEmpty(AppConfig.OutputNameMark) ? "" : AppConfig.OutputNameMark;
                comboOutputNameSeqType.SelectedIndex = (AppConfig.OutputNameSeqType >= 0 && AppConfig.OutputNameSeqType <= 2)
                    ? AppConfig.OutputNameSeqType : 0;
                comboOutputNamePad.SelectedIndex = (AppConfig.OutputNamePad >= 1 && AppConfig.OutputNamePad <= 3)
                    ? AppConfig.OutputNamePad - 1 : 0;
                chkOutputNameTs.IsChecked = AppConfig.OutputNameTs;
                comboOutputNameTsFormat.SelectedIndex = TsFormatToIndex(AppConfig.OutputNameTsFormat);
                UpdateOutputNameTsEnabled();
                UpdateOutputNamePreview();

                // 印章列表（yz.log，多印章下拉），并选中上次使用的印章
                LoadStampList();

                // 按文字历史预填上一次
                List<string> history = AppConfig.LoadAutoStampHistory();
                if (history.Count > 0)
                {
                    comboAutoKeyword.Text = history[0];
                }
            }
            catch (Exception ex)
            {
                AppendLog("读取配置失败：" + ex.Message, true);
            }
        }

        /// <summary>
        /// 窗口自适应：防止小屏幕或旧配置导致窗口超出屏幕。
        /// 尺寸超过所在屏幕工作区→压缩到屏幕内（不低于最小尺寸）；位置在屏幕外→回到屏幕中央。
        /// </summary>
        private void FitWindowToScreen()
        {
            try
            {
                // 用窗口中心点确定所在屏幕（支持多显示器）；WPF 窗口坐标是 DIP，需按 DPI 换算成物理像素再查 Screen
                double scaleX = 1.0, scaleY = 1.0;
                try
                {
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                    scaleX = dpi.DpiScaleX;
                    scaleY = dpi.DpiScaleY;
                }
                catch
                {
                    // DPI 获取失败时按 1:1 处理，不影响主流程
                }

                int centerX = (int)((Left + Width / 2.0) * scaleX);
                int centerY = (int)((Top + Height / 2.0) * scaleY);
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(centerX, centerY));
                var workArea = screen.WorkingArea;

                double availLeft = workArea.Left / scaleX;
                double availTop = workArea.Top / scaleY;
                double availWidth = workArea.Width / scaleX;
                double availHeight = workArea.Height / scaleY;

                const double margin = 20.0; // 窗口与屏幕边缘保留的边距

                // 尺寸超屏 → 压缩（不低于最小尺寸，避免布局被压坏）
                if (Width > availWidth - margin)
                    Width = Math.Max(MinWidth, availWidth - margin);
                if (Height > availHeight - margin)
                    Height = Math.Max(MinHeight, availHeight - margin);

                // 位置出屏（窗口整体在屏幕外）→ 回到屏幕中央（CenterScreen 需在 Show 前设置才生效）
                bool offscreen = Left < availLeft - 40 || Top < availTop - 40 ||
                                 Left > availLeft + availWidth - 40 || Top > availTop + availHeight - 40;
                if (offscreen)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            catch
            {
                // 自适应失败时保持默认定位（CenterScreen），不阻塞启动
            }
        }

        /// <summary>骑缝章显示索引 → 业务值（显示顺序：不加/加盖/单页/双页/随意）。</summary>
        private static int SeamDisplayToBusiness(int businessValue)
        {
            // 业务：0加盖 1不加 2单页 3双页 4随意；显示：0不加 1加盖 2单页 3双页 4随意
            switch (businessValue)
            {
                case 0: return 1;   // 加盖 → 显示第2项
                case 1: return 0;   // 不加 → 显示第1项
                default: return businessValue; // 2/3/4 显示相同
            }
        }

        /// <summary>骑缝章显示索引 → 业务值。</summary>
        private static int SeamBusinessFromDisplay(int displayIndex)
        {
            switch (displayIndex)
            {
                case 0: return 1;   // 不加
                case 1: return 0;   // 加盖
                default: return displayIndex; // 2/3/4
            }
        }

        private void UpdateSignatureEnabled()
        {
            bool active = comboSignature.SelectedIndex != 0;
            txtSignature.IsEnabled = active;
            txtSignaturePass.IsEnabled = active;
        }

        private void HookEvents()
        {
            btnSourcePdf.Click += (s, e) => ShowSourcePickMenu();
            btnOutputDir.Click += (s, e) => ChooseOutputDir();
            // V145：输出目录与锁定状态实时同步到配置字段（保存配置时写入 ini；TextChanged 幂等，启动恢复值写回无害）
            txtOutputDir.TextChanged += (s, e) => AppConfig.OutputDir = txtOutputDir.Text?.Trim() ?? "";
            chkOutputDirLock.Checked += (s, e) => AppConfig.OutputDirLocked = 1;
            chkOutputDirLock.Unchecked += (s, e) => AppConfig.OutputDirLocked = 0;
            btnStampFile.Click += (s, e) => ChooseStampFile();
            // 印章下拉：删除/重命名/选择在各自事件里调用 OnStampSelectionChanged
            // 用 Preview 事件确保文件拖放可靠（TextBox 内部会拦截普通 DragOver/Drop）
            txtSourcePdf.PreviewDragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
            txtSourcePdf.PreviewDrop += OnSourceDrop;
            // 预览区无需单独注册拖放：窗口级 AllowDrop 已覆盖整个界面（V85），预览区拖入走窗口 Drop 同一逻辑
            // 输出目录框不接收拖放（V87）：拖入 PDF/文件夹一律视为加载源文件，输出目录只能通过按钮手动选择
            // 全窗口拖放：界面任意空白区域拖入 PDF/文件夹 均可加载（文件夹→目录模式、文件→文件模式）；
            // 用冒泡事件且不拦截：源PDF框/输出目录框/预览区已用 Preview 事件设 Handled=true 自行接管，不会重复加载
            DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
                else { e.Effects = DragDropEffects.None; }
            };
            Drop += OnSourceDrop;
            // 当前文件下拉：切换时加载对应文件预览
            comboCurrentFile.SelectionChanged += OnCurrentFileChanged;

            btnPrev.Click += (s, e) => ChangePage(-1);
            btnNext.Click += (s, e) => ChangePage(1);

            // 页码跳转
            txtPageNow.KeyDown += (s, e) => { if (e.Key == Key.Enter) GoToPageInput(); };
            txtPageNow.LostFocus += (s, e) => GoToPageInput();

            // 缩放工具栏（对齐原版：步进 25，100-300）
            btnZoomIn.Click += (s, e) => ZoomStep(1);
            btnZoomOut.Click += (s, e) => ZoomStep(-1);
            // 单页/双页/放大视图：ToggleButton 互斥三选一，选中态遵循 WPF 系统规范
            btnFitPage.Checked += (s, e) => { if (IsViewToggleHandled()) FitPage(); };
            btnFitWidth.Checked += (s, e) => { if (IsViewToggleHandled()) FitWidth(); };
            btnFitDouble.Checked += (s, e) => { if (IsViewToggleHandled()) FitDouble(); };
            btnFitPage.Unchecked += OnViewToggleUnchecked;
            btnFitWidth.Unchecked += OnViewToggleUnchecked;
            btnFitDouble.Unchecked += OnViewToggleUnchecked;
            txtZoomNow.KeyDown += (s, e) => { if (e.Key == Key.Enter) ApplyZoomInput(); };
            txtZoomNow.LostFocus += (s, e) => ApplyZoomInput();

            // 拖动平移 / 左键单击盖章 / 右键删除印章 / Ctrl+滚轮缩放（单页 overlayCanvas 与双页左右 overlayCanvas 共用同一套处理器）
            overlayCanvas.MouseLeftButtonDown += OnPreviewCanvasMouseDown;
            overlayCanvas.MouseMove += OnPreviewCanvasMouseMove;
            overlayCanvas.MouseLeftButtonUp += OnPreviewCanvasMouseUp;
            overlayCanvas.MouseRightButtonDown += OnPreviewCanvasMouseRightButtonDown;
            overlayCanvasLeft.MouseLeftButtonDown += OnPreviewCanvasMouseDown;
            overlayCanvasLeft.MouseMove += OnPreviewCanvasMouseMove;
            overlayCanvasLeft.MouseLeftButtonUp += OnPreviewCanvasMouseUp;
            overlayCanvasLeft.MouseRightButtonDown += OnPreviewCanvasMouseRightButtonDown;
            overlayCanvasRight.MouseLeftButtonDown += OnPreviewCanvasMouseDown;
            overlayCanvasRight.MouseMove += OnPreviewCanvasMouseMove;
            overlayCanvasRight.MouseLeftButtonUp += OnPreviewCanvasMouseUp;
            overlayCanvasRight.MouseRightButtonDown += OnPreviewCanvasMouseRightButtonDown;
            previewScroll.PreviewMouseWheel += OnPreviewMouseWheel;
            // 工具栏宽度变化（窗口缩放/分隔条拖动）时，按空间分档压缩组间间距，极限时缩短按钮文字，仍放不下才换行。
            // 只响应宽度变化：换行会使工具栏高度变化，若高度变化也触发判定，会形成"换行→高度变→再判定→再换行"的布局振荡。
            previewToolbar.SizeChanged += OnToolbarSizeChanged;

            // 键盘翻页（对齐原版 PreviewNavigation_KeyDown：PageUp/Down、上下左右翻页；输入框聚焦时不拦截）
            PreviewKeyDown += OnWindowPreviewKeyDown;

            btnFoldSealParams.Click += (s, e) => ToggleFold(sealParamsContent, foldSealParamsArrow, foldSealParamsText, sealParamsHeaderGrid, btnFoldSealParams);
            btnFoldOther.Click += (s, e) => ToggleFold(otherContent, foldOtherArrow, foldOtherText, otherHeaderGrid, btnFoldOther);
            btnFoldAutoText.Click += (s, e) => ToggleFold(autoTextContent, foldAutoTextArrow, foldAutoTextText, autoTextHeaderGrid, btnFoldAutoText);
            // 用户手动修改分割数（代码赋值由 _suppressMaxSplitTrack 抑制，不算手动）
            txtMaxSplit.TextChanged += (s, e) =>
            {
                if (_suppressMaxSplitTrack) return;
                _maxSplitUserModified = true;
            };
            // 输出清晰度变化：立即保存配置
            comboOutputQuality.SelectionChanged += (s, e) =>
            {
                AppConfig.OutputQualityDpi = GetOutputQualityDpi();
                AppConfig.SaveUiConfig();
            };
            // 输出效果切换：叠加模式栅格化档位不生效，置灰
            comboOutput.SelectionChanged += (s, e) => UpdateOutputQualityEnabled();
            // 切换骑缝章类型时给出操作提示（单页=正向/奇数页，双页=反向/偶数页）
            comboSeam.SelectionChanged += (s, e) =>
            {
                int qfzType = SeamBusinessFromDisplay(comboSeam.SelectedIndex);
                if (qfzType == 2)
                {
                    SetOperationHint("单页骑缝章：类似于双面打印正向盖章的效果，奇数页（1、3、5...）有骑缝章");
                }
                else if (qfzType == 3)
                {
                    SetOperationHint("双页骑缝章：类似于双面打印反向盖章的效果，偶数页（2、4、6...）有骑缝章");
                }
                else if (qfzType == 4)
                {
                    SetOperationHint("随意骑缝章：所有放置过印章的页面都加盖骑缝章（跟随已盖章页面）");
                }
                // 切换骑缝章类型时分割数跟随文档自动调整（不加/目录模式除外）
                if (qfzType != 1 && !currentSourceIsDirectory)
                {
                    UpdateMaxSplitFromPdf();
                }
            };

            btnGenerate.Click += async (s, e) => await OnGenerateClickAsync();
            btnAutoPlace.Click += (s, e) => OnAutoPlaceClick();
            btnUndoAuto.Click += (s, e) => OnUndoAutoClick();
            btnSpecifiedPage.Click += (s, e) => OnSpecifiedPageClick();

            comboSignature.SelectionChanged += (s, e) => UpdateSignatureEnabled();
            comboPageStamp.SelectionChanged += (s, e) => UpdatePlacementOperationHint();

            // 输出文件名格式自定义（V132）：任一控件变化 → 刷新预览 + 保存配置（启动赋值阶段 IsLoaded=false 不保存）
            comboOutputNamePos.SelectionChanged += (s, e) => OutputName_Changed();
            txtOutputNameMark.TextChanged += (s, e) => OutputName_Changed();
            comboOutputNameSeqType.SelectionChanged += (s, e) => OutputName_Changed();
            comboOutputNamePad.SelectionChanged += (s, e) => OutputName_Changed();
            chkOutputNameTs.Checked += (s, e) => OutputName_Changed();
            chkOutputNameTs.Unchecked += (s, e) => OutputName_Changed();
            comboOutputNameTsFormat.SelectionChanged += (s, e) => OutputName_Changed();

            // 窗口大小变化时延迟重算预览布局：SizeChanged 触发时子控件 previewScroll 可能尚未完成布局，
            // 直接读 ViewportWidth/Height 会拿到旧值，导致全屏/还原后页面大小不更新（需手动切换视图才正常）
            SizeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(RelayoutPreview),
                System.Windows.Threading.DispatcherPriority.Loaded);
            // 窗口高度变化时重新分配设置区/提示区高度（设置区优先显示全）。
            // 必须延迟到布局完成后再读 ActualHeight，否则 SizeChanged 同步阶段拿到的是旧值。
            SizeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateSettingsHeight),
                System.Windows.Threading.DispatcherPriority.Loaded);
            Loaded += (s, e) =>
            {
                UpdateTextureEnabled();   // 启动后按配置/默认勾选态设置盖章渲染按钮（初始文字/置灰在 XAML 声明，此处仅按配置覆盖）
                UpdateSettingsHeight();
                RestoreLeftPanelWidth();
                UpdateToolbarSpacing();
                // 启动后无文件时显示内置调试 PDF（可盖章调试渲染参数），延迟到布局就绪
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (pdfRenderer == null && !_debugPageActive && string.IsNullOrEmpty(sourcePath))
                    {
                        ShowBlankDebugPage();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // 去除白色背景：未勾选时容差不可编辑；操作该控件时提示功能说明（程序赋值经 _suppressStampParamSave 抑制）
            chkRemoveWhite.Checked += (s, e) =>
            {
                UpdateToleranceEnabled();
                SaveCurrentStampParams();
                if (IsLoaded && !_suppressStampParamSave)
                {
                    SetOperationHint("去除图章白色背景：清除印章图片的白色底，让章子透明融入页面");
                }
            };
            chkRemoveWhite.Unchecked += (s, e) =>
            {
                UpdateToleranceEnabled();
                SaveCurrentStampParams();
                if (IsLoaded && !_suppressStampParamSave)
                {
                    SetOperationHint("去除图章白色背景：清除印章图片的白色底，让章子透明融入页面");
                }
            };

            // 印章参数：界面改动立即保存到当前章记忆（改即生效；MaxSplit 遵循“手动修改才记忆”规则，不在此自动保存）
            txtStampSize.TextChanged += (s, e) => SaveCurrentStampParams();
            txtRotation.TextChanged += (s, e) => SaveCurrentStampParams();
            txtOpacity.TextChanged += (s, e) => SaveCurrentStampParams();
            txtTolerance.TextChanged += (s, e) => SaveCurrentStampParams();
            comboRotationHandle.SelectionChanged += (s, e) => SaveCurrentStampParams();
            chkRandomParams.Checked += (s, e) => SaveCurrentStampParams();
            chkRandomParams.Unchecked += (s, e) => SaveCurrentStampParams();

            Closing += (s, e) =>
            {
                SaveCurrentStampParams();
                AppConfig.FoldAutoText = autoTextContent.Visibility == Visibility.Visible ? 1 : 0;
                AppConfig.FoldSealParams = sealParamsContent.Visibility == Visibility.Visible ? 1 : 0;
                AppConfig.FoldOther = otherContent.Visibility == Visibility.Visible ? 1 : 0;
                AppConfig.SaveWindowState(Left, Top, Width, Height);
            };

            // 事件绑定完成后，手动加载一次当前选中印章的保存参数
            // （LoadStampList 在事件绑定前设置了默认选中项，此时 SelectionChanged 未触发）
            OnStampSelectionChanged();
        }

        // ===================== 按文字盖章输入框：占位提示 + 历史下拉 =====================
        private void HookAutoKeywordWatermark()
        {
            comboAutoKeyword.GotKeyboardFocus += (s, e) =>
            {
                autoKeywordWatermark.Visibility = Visibility.Collapsed;
                // 点击输入框即展开历史下拉（可编辑，展开时仍可输入/删除文字）
                comboAutoKeyword.IsDropDownOpen = true;
            };
            comboAutoKeyword.LostKeyboardFocus += (s, e) => UpdateAutoKeywordWatermark();
            // 可编辑 ComboBox 内部是 TextBox，用路由事件监听文字变化以更新占位提示
            comboAutoKeyword.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => UpdateAutoKeywordWatermark()));
            comboAutoKeyword.DropDownOpened += (s, e) => RebuildAutoHistoryItems();
        }

        private void UpdateAutoKeywordWatermark()
        {
            bool empty = string.IsNullOrEmpty(comboAutoKeyword.Text);
            bool focused = comboAutoKeyword.IsKeyboardFocused;
            autoKeywordWatermark.Visibility = empty && !focused ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===================== 上下文过滤：勾选控制 + 占位提示 =====================
        private void HookContextFilterEvents()
        {
            chkContextFilter.Checked += (s, e) =>
            {
                UpdateContextFilterEnabled();
                if (IsLoaded)
                {
                    SetOperationHint("附近关键词：目标文字前后指定范围内出现这些词才盖章，用于精准定位盖章位置");
                }
            };
            chkContextFilter.Unchecked += (s, e) =>
            {
                UpdateContextFilterEnabled();
                if (IsLoaded)
                {
                    SetOperationHint("附近关键词：目标文字前后指定范围内出现这些词才盖章，用于精准定位盖章位置");
                }
            };
            // 忽略空白：匹配时是否忽略空白字符（启动赋值在 LoadConfigToUi，IsLoaded=false 不提示）
            chkContextExcludeSpaces.Checked += (s, e) =>
            {
                if (IsLoaded) SetOperationHint("忽略空白：匹配附近关键词时忽略文字间的空格等空白字符");
            };
            chkContextExcludeSpaces.Unchecked += (s, e) =>
            {
                if (IsLoaded) SetOperationHint("忽略空白：匹配附近关键词时忽略文字间的空格等空白字符");
            };
            txtContextKeywords.TextChanged += (s, e) => UpdateContextKeywordsWatermark();
            txtContextKeywords.GotKeyboardFocus += (s, e) => UpdateContextKeywordsWatermark();
            txtContextKeywords.LostKeyboardFocus += (s, e) => UpdateContextKeywordsWatermark();
        }

        // ===================== 中心偏移：随搜索文字记忆（弹窗设置，同随机角度位移规范） =====================
        private void HookCenterOffsetEvents()
        {
            // chkCenterOffset 的勾选事件在 XAML 挂载（ChkCenterOffset_Changed），此处不再重复挂载
            // 关键词确定（失焦/关闭下拉）后加载该词记忆的偏移；输入击键时不加载，避免打断输入
            comboAutoKeyword.LostKeyboardFocus += (s, e) =>
            {
                UpdateAutoKeywordWatermark();
                LoadCenterOffsetFromKeyword(comboAutoKeyword.Text);
            };
            comboAutoKeyword.DropDownClosed += (s, e) => LoadCenterOffsetFromKeyword(comboAutoKeyword.Text);
        }

        /// <summary>中心偏移勾选变化：未勾选时"中心偏移"按钮置灰（与随机角度位移交互一致）。</summary>
        private void ChkCenterOffset_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCenterOffsetEnabledAndSave();
            if (IsLoaded && !_suppressCenterOffsetSync)
            {
                SetOperationHint("中心偏移：调整盖章位置相对文字识别点的偏移距离，用于微调盖章落点");
            }
        }

        /// <summary>勾选/取消勾选：启用/禁用按钮并保存记忆。</summary>
        private void UpdateCenterOffsetEnabledAndSave()
        {
            bool on = chkCenterOffset.IsChecked == true;
            btnCenterOffset.IsEnabled = on;
            SaveCenterOffsetFromUi();
        }

        /// <summary>把界面当前勾选与横纵值保存到当前关键词记忆（该词不在历史中则静默跳过）。</summary>
        private void SaveCenterOffsetFromUi()
        {
            if (_suppressCenterOffsetSync)
            {
                return;
            }
            string keyword = comboAutoKeyword.Text.Trim();
            if (keyword.Length == 0)
            {
                return;
            }
            AppConfig.SetCenterOffsetForKeyword(keyword, chkCenterOffset.IsChecked == true, _centerOffsetX, _centerOffsetY);
        }

        /// <summary>按关键词加载记忆的偏移到界面；无记忆则恢复默认（不勾选、0/0）。</summary>
        private void LoadCenterOffsetFromKeyword(string keyword)
        {
            if (_suppressCenterOffsetSync)
            {
                return;
            }
            _suppressCenterOffsetSync = true;
            try
            {
                bool enabled;
                float x, y;
                AppConfig.GetCenterOffsetForKeyword(keyword.Trim(), out enabled, out x, out y);
                chkCenterOffset.IsChecked = enabled;
                _centerOffsetX = (int)Math.Round(x);
                _centerOffsetY = (int)Math.Round(y);
                btnCenterOffset.IsEnabled = enabled;
            }
            finally
            {
                _suppressCenterOffsetSync = false;
            }
        }

        /// <summary>根据勾选状态启用/禁用关键词输入框、范围输入框、匹配方式下拉及从属标题（未勾选时整块灰显）。</summary>
        private void UpdateContextFilterEnabled()
        {
            bool enabled = chkContextFilter.IsChecked == true;
            txtContextKeywords.IsEnabled = enabled;
            txtContextRange.IsEnabled = enabled;
            comboContextMatch.IsEnabled = enabled;
            chkContextExcludeSpaces.IsEnabled = enabled;
            lblContextRange.IsEnabled = enabled;
            lblContextMatch.IsEnabled = enabled;
        }

        /// <summary>关键词输入框为空且未聚焦时显示占位提示。</summary>
        private void UpdateContextKeywordsWatermark()
        {
            bool empty = string.IsNullOrEmpty(txtContextKeywords.Text);
            bool focused = txtContextKeywords.IsKeyboardFocused;
            contextKeywordsWatermark.Visibility = empty && !focused ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>刷新历史下拉项（最新在前，最多 10 条），保持输入框当前文字。</summary>
        private void RebuildAutoHistoryItems()
        {
            string current = comboAutoKeyword.Text;
            comboAutoKeyword.ItemsSource = AppConfig.LoadAutoStampHistory();
            comboAutoKeyword.Text = current;
        }

        /// <summary>历史下拉项右侧删除叉：删除该条历史；若删除的正是输入框内当前文字，同步清空输入框。</summary>
        private void HistoryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string keyword)
            {
                e.Handled = true;
                AppConfig.RemoveAutoStampKeyword(keyword);
                if (string.Equals(comboAutoKeyword.Text?.Trim(), keyword, StringComparison.OrdinalIgnoreCase))
                {
                    comboAutoKeyword.Text = string.Empty;
                }
                RebuildAutoHistoryItems();
                SetOperationHint(string.Format("已从历史中删除：“{0}”。", keyword));
            }
        }

        /// <summary>印章下拉项的删除按钮：从配置移除该印章并刷新列表（e.Handled 防止冒泡到下拉选中）。</summary>
        private void StampPickerDelete_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.Button btn && btn.Tag is StampPickerItem item)
            {
                DeleteStampItem(item);
            }
        }

        /// <summary>删除一个印章条目：先保存当前主章参数，再删配置 + 内存列表 + 勾选集合 + 参数节。</summary>
        private void DeleteStampItem(StampPickerItem item)
        {
            if (item == null) return;
            string name = item.DisplayName;
            // 先保存当前主章参数（删除操作会触发主章变化，先保存旧主章参数）
            SaveCurrentStampParams();
            AppConfig.RemoveStampEntry(name);
            _stampItems.RemoveAll(i => string.Equals(i.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            _selectedStampNames.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            // 删除的是当前章且列表仍有章：自动选中第一个
            if (_selectedStampNames.Count == 0 && _stampItems.Count > 0)
            {
                _stampItems[0].IsSelected = true;
                _selectedStampNames.Add(_stampItems[0].DisplayName);
            }
            RebuildStampPickerList();
            OnStampSelectionChanged();
            SetOperationHint(string.Format("已从印章列表删除：“{0}”。", name));
        }

        /// <summary>重命名印章：弹窗输入新名（1-30 字符、不重名），参数记忆与勾选集自动迁移。</summary>
        private void RenameStamp(StampPickerItem item)
        {
            if (item == null) return;
            var dlg = new RenameStampDialog(
                string.Format("为印章“{0}”输入新的名称（1-30 个字符，不能与现有印章重名）：", item.DisplayName),
                item.DisplayName)
            { Owner = this, Title = "重命名印章" };
            if (dlg.ShowDialog() != true) return;
            string newName = dlg.ResultName;
            if (newName.Length == 0 || string.Equals(newName, item.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (newName.IndexOf('|') >= 0 || newName.IndexOf(';') >= 0)
            {
                MessageBox.Show("印章名称不能包含“|”或“；”字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (AppConfig.StampDisplayNameExists(newName))
            {
                MessageBox.Show("已存在同名印章：“" + newName + "”。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string oldName = item.DisplayName;
            AppConfig.RenameStampEntry(oldName, newName);
            item.DisplayName = newName;
            for (int i = 0; i < _selectedStampNames.Count; i++)
            {
                if (string.Equals(_selectedStampNames[i], oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedStampNames[i] = newName;
                }
            }
            if (string.Equals(_currentStampFileName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _currentStampFileName = newName;
            }
            RebuildStampPickerList();
            RefreshStampPickerDisplay();
            SetOperationHint(string.Format("印章已重命名：“{0}”→“{1}”。", oldName, newName));
        }

        // ===================== 折叠 =====================
        private void ToggleFold(System.Windows.Controls.StackPanel content, System.Windows.Shapes.Path arrow, System.Windows.Controls.TextBlock text, System.Windows.Controls.Grid header, System.Windows.Controls.Button btn)
        {
            bool collapsed = content.Visibility != Visibility.Visible;
            content.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            // 收起态：标题行底部间距归零上下居中；展开态：恢复标题与内容的 10px 间距
            header.Margin = collapsed ? new Thickness(0, 0, 0, 10) : new Thickness(0);
            // 折叠箭头（Icon.FoldArrow=▼）：展开态=▼ 原样；折叠态=同图向左旋转 90° 显示 ▶
            arrow.RenderTransform = collapsed ? null : new System.Windows.Media.RotateTransform(-90);
            text.Text = collapsed ? "收起设置" : "展开设置";
            // 状态色：展开态（收起设置）=红字红箭头红边框提醒可收起；收起态（展开设置）=蓝字蓝箭头蓝边框（与标准按钮一致）
            System.Windows.Media.Brush brush;
            if (collapsed)
                brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0x33, 0x33)); // 红 #CC3333
            else
                brush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("Color.Primary"); // 蓝 #1677FF
            arrow.Fill = brush;
            text.Foreground = brush;
            btn.BorderBrush = brush;
            // 内容高度变化后重新分配设置区/提示区高度（延迟到布局完成），并自动滚动到展开区域底部
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateSettingsHeight();
                // 展开后：若内容超出可视区，自动滚动到展开区域底部，让展开内容立即可见，无需手动下滚
                if (collapsed && settingsScroll != null && content.ActualHeight > 0)
                {
                    // 强制同步布局：content 刚设为可见，ActualHeight/TranslatePoint 若用旧值会算小，导致滚不到位
                    settingsScroll.UpdateLayout();
                    double bottom = content.TranslatePoint(
                        new System.Windows.Point(0, content.ActualHeight), settingsContent).Y;
                    // 底部虚化提示（scrollHint，高 22px）会盖住展开区域最后几行，额外多滚 22px 补偿；
                    // ScrollToVerticalOffset 会自动截断到可滚动上限，故"5 其他设置"自然滚到最底部
                    const double hintCompensation = 22;
                    settingsScroll.ScrollToVerticalOffset(
                        Math.Max(0, bottom - settingsScroll.ViewportHeight + hintCompensation));
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===================== 文件选择 / 拖放 =====================
        /// <summary>点击“选择”：弹出小菜单，由用户决定选 PDF 文件还是文件夹（取消模式单选后，两种入口合并到一个按钮）。</summary>
        private void ShowSourcePickMenu()
        {
            var menu = new ContextMenu();
            var miFile = new System.Windows.Controls.MenuItem { Header = "选择 PDF 文件" };
            miFile.Click += (s, a) => ChooseSourceFiles();
            var miDir = new System.Windows.Controls.MenuItem { Header = "选择文件夹" };
            miDir.Click += (s, a) => ChooseSourceDirectory();
            menu.Items.Add(miFile);
            menu.Items.Add(miDir);
            menu.PlacementTarget = btnSourcePdf;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void ChooseSourceFiles()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PDF 文件|*.pdf|所有文件|*.*",
                Title = "选择源 PDF 文件",
                Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) == true)
            {
                if (string.IsNullOrWhiteSpace(txtOutputDir.Text))
                {
                    txtOutputDir.Text = Path.GetDirectoryName(dlg.FileNames[0]);
                }
                LoadSourceFiles(dlg.FileNames);
            }
        }

        private void ChooseSourceDirectory()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择 PDF 所在文件夹", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // 输出目录由 LoadDirectory 统一设为“所选文件夹\已盖章”
                LoadDirectory(dlg.SelectedPath);
            }
        }

        /// <summary>文件模式：载入一个或多个 PDF，填充“当前文件”下拉并加载第一个。</summary>
        private void LoadSourceFiles(string[] files)
        {
            var pdfs = files.Where(f => File.Exists(f) &&
                string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pdfs.Length == 0)
            {
                System.Windows.MessageBox.Show(this, "未选择有效的 PDF 文件。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            currentSourceIsDirectory = false;
            sourcePath = string.Join(",", pdfs);
            sourceFiles = pdfs;
            txtSourcePdf.Text = sourcePath;
            PopulateCurrentFileCombo(pdfs, pdfs[0]);
            LoadPdf(pdfs[0]);
        }

        /// <summary>目录模式：递归枚举文件夹下所有 PDF（排除输出目录），填充下拉并加载第一个。</summary>
        private void LoadDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            // 目录模式：输出目录固定为“上传文件夹\已盖章”（未锁定时无条件；锁定后保持用户指定目录，避免残留旧目录输出到同一层）
            string outDir = Path.Combine(dir, "已盖章");
            if (chkOutputDirLock.IsChecked != true || string.IsNullOrWhiteSpace(txtOutputDir.Text))
            {
                txtOutputDir.Text = outDir;
            }
            var pdfs = Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories)
                .Where(f => !string.Equals(Path.GetDirectoryName(f), outDir, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            currentSourceIsDirectory = true;
            sourcePath = dir;
            sourceFiles = pdfs;
            txtSourcePdf.Text = dir;
            if (pdfs.Length == 0)
            {
                ResetPreview();
                System.Windows.MessageBox.Show(this, "该文件夹下没有找到 PDF 文件。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            PopulateCurrentFileCombo(pdfs, pdfs[0]);
            LoadPdf(pdfs[0]);
        }

        /// <summary>用文件全路径列表填充预览区“当前文件”下拉（显示文件名，Tag 存全路径），并选中指定文件。</summary>
        private void PopulateCurrentFileCombo(string[] pdfs, string selectPath)
        {
            suppressCurrentFileEvent = true;
            comboCurrentFile.Items.Clear();
            foreach (string f in pdfs)
            {
                comboCurrentFile.Items.Add(new ComboBoxItem { Content = Path.GetFileName(f), Tag = f });
            }
            for (int i = 0; i < pdfs.Length; i++)
            {
                if (string.Equals(pdfs[i], selectPath, StringComparison.OrdinalIgnoreCase))
                {
                    comboCurrentFile.SelectedIndex = i;
                    break;
                }
            }
            suppressCurrentFileEvent = false;
        }

        /// <summary>“当前文件”下拉切换：加载所选 PDF 预览。</summary>
        private void OnCurrentFileChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressCurrentFileEvent) return;
            if (comboCurrentFile.SelectedItem is ComboBoxItem item && item.Tag is string path && File.Exists(path))
            {
                LoadPdf(path, keepStampData: true);
            }
        }

        private void ChooseOutputDir()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择输出目录", SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtOutputDir.Text = dlg.SelectedPath;
            }
        }

        private void ChooseStampFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
                Title = "选择印章图片（可多选导入，追加到印章列表）",
                Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true)
            {
                return;
            }
            int imported = 0;
            foreach (string filePath in dlg.FileNames)
            {
                string defaultName = Path.GetFileNameWithoutExtension(filePath);
                if (defaultName.Length > 30) defaultName = defaultName.Substring(0, 30);
                string finalName = defaultName;

                // 重名 → 生成不重名候选名并弹窗让用户确认/改名；取消则跳过该文件
                if (AppConfig.StampDisplayNameExists(finalName))
                {
                    string candidate = BuildUniqueStampName(finalName);
                    var renameDlg = new RenameStampDialog(
                        string.Format("已存在同名印章：“{0}”。\n请输入新的印章名称（1-30 个字符，不能与现有印章重名）：", finalName),
                        candidate)
                    { Owner = this, Title = "印章重名" };
                    if (renameDlg.ShowDialog() != true) continue;
                    finalName = renameDlg.ResultName;
                    if (finalName.Length == 0) continue;
                    if (finalName.IndexOf('|') >= 0 || finalName.IndexOf(';') >= 0)
                    {
                        MessageBox.Show("印章名称不能包含“|”或“；”字符，已跳过该文件：" + filePath,
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        continue;
                    }
                    if (AppConfig.StampDisplayNameExists(finalName))
                    {
                        MessageBox.Show("已存在同名印章：“" + finalName + "”，已跳过该文件。",
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        continue;
                    }
                }

                // 入库：复制进 EXE 同目录"印章库"（重名自动加序号），条目路径指向库内文件
                string libPath = AppConfig.ImportStampToLibrary(filePath);
                AppConfig.AppendStampEntry(finalName, libPath);
                // 导入后不自动勾选：用户需要时手动勾选（勾选集合保持不变）
                var item = new StampPickerItem { DisplayName = finalName, Path = libPath, IsSelected = false };
                _stampItems.Add(item);
                imported++;
            }
            if (imported > 0)
            {
                // 无选中章时自动选中最后导入的印章（自动加载到选择栏并记录，下次打开沿用）
                if (_selectedStampNames.Count == 0 && _stampItems.Count > 0)
                {
                    StampPickerItem last = _stampItems[_stampItems.Count - 1];
                    last.IsSelected = true;
                    _selectedStampNames.Add(last.DisplayName);
                }
                RebuildStampPickerList();
                OnStampSelectionChanged();
                SetOperationHint(string.Format("已导入 {0} 个印章。", imported));
            }
        }

        /// <summary>生成不重名的印章名称候选：原名2、原名3…直到不与现有章重名。</summary>
        private string BuildUniqueStampName(string baseName)
        {
            int i = 2;
            while (AppConfig.StampDisplayNameExists(baseName + i.ToString()))
            {
                i++;
            }
            return baseName + i.ToString();
        }

        /// <summary>加载印章列表（config.ini），恢复上次勾选集合；勾选集为空时兼容旧配置（上次使用章/第一个）。
        /// 启动时先执行一次印章库迁移（旧外部路径复制入库并更新配置）。</summary>
        private void LoadStampList()
        {
            _stampItems.Clear();
            _selectedStampNames.Clear();

            AppConfig.MigrateStampLibrary();   // V82：旧配置外部印章图复制进"印章库"并更新条目

            List<AppConfig.StampEntry> entries = AppConfig.LoadStampEntries();
            if (entries.Count == 0 && !string.IsNullOrWhiteSpace(AppConfig.LastStampImagePath)
                && File.Exists(AppConfig.LastStampImagePath))
            {
                string libPath = AppConfig.ImportStampToLibrary(AppConfig.LastStampImagePath);
                entries.Add(new AppConfig.StampEntry(
                    Path.GetFileNameWithoutExtension(AppConfig.LastStampImagePath), libPath));
                AppConfig.AppendStampEntry(
                    Path.GetFileNameWithoutExtension(AppConfig.LastStampImagePath), libPath);
            }

            foreach (AppConfig.StampEntry e in entries)
            {
                _stampItems.Add(new StampPickerItem { DisplayName = e.DisplayName, Path = e.Path, IsSelected = false });
            }

            // 恢复选中（单选）：配置的章名优先；其次旧配置的上次使用章；仍没有则选第一个
            StampPickerItem selected = null;
            if (AppConfig.LastSelectedStampNames != null && AppConfig.LastSelectedStampNames.Count > 0)
            {
                selected = FindStampItem(AppConfig.LastSelectedStampNames[0]);
            }
            if (selected == null && !string.IsNullOrWhiteSpace(AppConfig.LastStampImagePath))
            {
                selected = _stampItems.FirstOrDefault(i =>
                    string.Equals(i.Path, AppConfig.LastStampImagePath, StringComparison.OrdinalIgnoreCase));
            }
            if (selected == null && _stampItems.Count > 0)
            {
                selected = _stampItems[0];
            }
            if (selected != null)
            {
                selected.IsSelected = true;
                _selectedStampNames.Add(selected.DisplayName);
            }

            RebuildStampPickerList();
        }

        /// <summary>重建印章下拉列表（按显示名排序，恢复当前选中项，刷新显示）。</summary>
        private void RebuildStampPickerList()
        {
            _suppressStampSelection = true;
            try
            {
                comboStampMulti.ItemsSource = _stampItems
                    .OrderBy(i => i.DisplayName, StringComparer.CurrentCulture)
                    .ToList();
                StampPickerItem current = GetPrimaryStampItem();
                comboStampMulti.SelectedItem = current;
            }
            finally
            {
                _suppressStampSelection = false;
            }
            RefreshStampPickerDisplay();
        }

        /// <summary>按显示名查找印章条目（大小写不敏感）。</summary>
        private StampPickerItem FindStampItem(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            return _stampItems.FirstOrDefault(i =>
                string.Equals(i.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>刷新印章下拉框显示：当前选中章名；空则显示灰色占位“请选择印章”。
        /// 同时直接同步 ComboBox 内部文本框（IsEditable 模式下 ComboBox.Text 可能被内部逻辑延迟/覆盖）。</summary>
        private void RefreshStampPickerDisplay()
        {
            string text;
            System.Windows.Media.Brush fg;
            if (_selectedStampNames.Count == 0)
            {
                text = "请选择印章";
                fg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
                comboStampMulti.ToolTip = null;
            }
            else
            {
                text = string.Join("；", _selectedStampNames);
                fg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                comboStampMulti.ToolTip = text;
            }
            comboStampMulti.Text = text;
            comboStampMulti.Foreground = fg;
            if (comboStampMulti.Template != null &&
                comboStampMulti.Template.FindName("PART_EditableTextBox", comboStampMulti)
                    is System.Windows.Controls.TextBox tb)
            {
                tb.Text = text;
                tb.Foreground = fg;
            }
        }

        /// <summary>主章路径（勾选集合第一个）；未勾选时返回空字符串。</summary>
        private string SelectedStampPath()
        {
            StampPickerItem primary = GetPrimaryStampItem();
            return primary != null ? primary.Path : string.Empty;
        }

        /// <summary>当前选中章条目 = 选中集合第一个（单选时即当前章）。</summary>
        private StampPickerItem GetPrimaryStampItem()
        {
            foreach (string name in _selectedStampNames)
            {
                StampPickerItem it = FindStampItem(name);
                if (it != null) return it;
            }
            return null;
        }

                /// <summary>当前选中章变化（下拉选择/删除/导入/重命名）后：保存旧章参数，选中章变化时加载新章参数，落盘配置。</summary>
        private void OnStampSelectionChanged()
        {
            StampPickerItem primary = GetPrimaryStampItem();
            string primaryName = primary != null ? primary.DisplayName : null;
            bool primaryChanged = !string.Equals(_currentStampFileName, primaryName, StringComparison.OrdinalIgnoreCase);

            // 主章变化时先保存旧主章参数（首次时 _currentStampFileName 为空，跳过）
            if (primaryChanged)
            {
                SaveCurrentStampParams();
            }

            if (primary == null)
            {
                _currentStampFileName = null;
                AppConfig.LastStampImagePath = "";
                AppConfig.LastSelectedStampNames = new List<string>();
                AppConfig.SaveUiConfig();
                RefreshStampPickerDisplay();
                return;
            }

            _currentStampFileName = primary.DisplayName;

            // 仅主章变化时加载该章参数（同主章勾选变化不重载，避免覆盖已自动调整的分割数等）
            if (primaryChanged)
            {
                // 加载主章上次保存的参数（显示名 key；旧完整文件名 key 自动迁移；无记录用默认值）
                AppConfig.StampParams p = AppConfig.LoadStampParamsMigrate(
                    primary.DisplayName, Path.GetFileName(primary.Path));
                ApplyStampParams(p);
            }

            AppConfig.LastStampImagePath = primary.Path;
            AppConfig.LastSelectedStampNames = new List<string>(_selectedStampNames);
            AppConfig.SaveUiConfig();
            RefreshStampPickerDisplay();
        }

        /// <summary>当前选中的印章（用于盖章）；选中章文件不存在时返回 null。</summary>
        private StampPickerItem GetSelectedStampItem()
        {
            StampPickerItem primary = GetPrimaryStampItem();
            if (primary != null && File.Exists(primary.Path))
            {
                return primary;
            }
            return null;
        }

        /// <summary>读取某印章的参数：有记忆（显示名或旧文件名 key）则加载并迁移；无记忆则用当前界面参数。</summary>
        private AppConfig.StampParams LoadParamsForStampItem(StampPickerItem item)
        {
            if (item == null) return null;
            string legacyKey = Path.GetFileName(item.Path);
            if (AppConfig.HasStampParams(item.DisplayName) || AppConfig.HasStampParams(legacyKey))
            {
                return AppConfig.LoadStampParamsMigrate(item.DisplayName, legacyKey);
            }
            return GetCurrentStampParamsFromUi();
        }

        /// <summary>印章下拉框：点击文本框区域打开下拉（IsReadOnly 时文本框拦截了点击，这里补开；箭头交给原生切换）。</summary>
        private void ComboStamp_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (comboStampMulti.IsDropDownOpen) return;
            // 若点击落在 ToggleButton（箭头）内，交给原生 ToggleButton 处理；其余区域（文本框）由这里打开
            System.Windows.DependencyObject src = e.OriginalSource as System.Windows.DependencyObject;
            while (src != null && !(src is System.Windows.Controls.Primitives.ToggleButton))
            {
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }
            if (src == null)
            {
                comboStampMulti.IsDropDownOpen = true;
            }
        }

        /// <summary>印章下拉框（单选）：选中一项 → 设为当前章；为空时仅刷新显示。</summary>
        private void ComboStamp_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressStampSelection)
            {
                return;
            }
            if (comboStampMulti.SelectedItem is StampPickerItem item)
            {
                bool changed = !_selectedStampNames.Contains(item.DisplayName);
                _selectedStampNames.Clear();
                _selectedStampNames.Add(item.DisplayName);
                if (changed)
                {
                    OnStampSelectionChanged();
                }
                else
                {
                    RefreshStampPickerDisplay();
                }
            }
            else
            {
                RefreshStampPickerDisplay();
            }
        }


        /// <summary>容差输入框启用状态：只有勾选"去除白色背景"时才能编辑；
        /// 未勾选时"去除图章白色背景"文字与"容差"标题置灰（AutoContextLabel 灰显，§4.6/§7.1）。
        /// 注意：IsEnabled 必须与状态同向（勾选=true），AutoContextLabel 灰色由 IsEnabled=False 触发（V113 修正写反）。</summary>
        private void UpdateToleranceEnabled()
        {
            bool rw = chkRemoveWhite.IsChecked == true;
            txtTolerance.IsEnabled = rw;
            lblTolerance.Style = rw ? null : (Style)Application.Current.FindResource("TextBlock.AutoContextLabel");
            lblTolerance.IsEnabled = rw;    // 勾选=可编辑黑字；未勾选=IsEnabled false 触发 AutoContextLabel 灰
            lblRemoveWhite.IsEnabled = rw;  // 勾选框文字同样随勾选状态置灰/恢复（Content 内 TextBlock，§4.6）
        }

        /// <summary>随机参数勾选变化：未勾选时"随机角度与位移"按钮置灰（与盖章渲染交互一致）。</summary>
        private void ChkRandomParams_Changed(object sender, RoutedEventArgs e)
        {
            UpdateRandomEnabled();
            SaveCurrentStampParams();
            if (IsLoaded && !_suppressStampParamSave)
            {
                SetOperationHint("随机角度与位移：盖章时随机旋转角度并偏移位置，模拟手工盖章效果");
            }
        }

        private void UpdateRandomEnabled()
        {
            btnRandomParams.IsEnabled = chkRandomParams.IsChecked == true;
        }

        // 中心偏移（弹窗设置、随搜索文字记忆）：横向/纵向 ±100mm
        private int _centerOffsetX = 0;
        private int _centerOffsetY = 0;
        private CenterOffsetWindow _centerOffsetDlg;

        /// <summary>弹窗默认定位（V131）：弹窗中心与"左上方区域"中心对齐——左上方区域 = 红色"盖章并生成文件"按钮上方的设置区
        /// （左栏外框顶部 → 盖章按钮顶部之间的矩形，宽同左栏）。
        /// 盖章按钮被滚动出可视区时退化为左栏整体中心；最终均夹取在屏幕工作区内。</summary>
        private void PositionDialogOverLeftRegion(Window dlg, double dlgW, double dlgH)
        {
            if (leftFrame == null || btnGenerate == null)
            {
                dlg.Left = this.Left + (this.Width - dlgW) / 2.0;
                dlg.Top = this.Top + (this.Height - dlgH) / 2.0;
                return;
            }
            System.Windows.Point frameTL = leftFrame.PointToScreen(new System.Windows.Point(0, 0));
            System.Windows.Point btnTL = btnGenerate.PointToScreen(new System.Windows.Point(0, 0));
            double regionLeft = frameTL.X;
            double regionTop = frameTL.Y;
            double regionRight = frameTL.X + leftFrame.ActualWidth;
            double regionBottom = btnTL.Y;
            double regionH = regionBottom - regionTop;
            if (regionH <= 0)
            {
                // 盖章按钮不在可视区（设置区已滚动）：退化为左栏整体中心对齐
                regionBottom = frameTL.Y + leftFrame.ActualHeight;
                regionH = regionBottom - regionTop;
            }
            double left = regionLeft + ((regionRight - regionLeft) - dlgW) / 2.0;
            double top = regionTop + (regionH - dlgH) / 2.0;
            System.Windows.Rect wa = SystemParameters.WorkArea;
            if (left < wa.Left) left = wa.Left;
            if (top < wa.Top) top = wa.Top;
            if (left + dlgW > wa.Right) left = wa.Right - dlgW;
            if (top + dlgH > wa.Bottom) top = wa.Bottom - dlgH;
            dlg.Left = left;
            dlg.Top = top;
        }

        /// <summary>打开中心偏移弹窗（非模态，与随机角度位移弹窗同规范）：已打开时激活。</summary>
        private void BtnCenterOffset_Click(object sender, RoutedEventArgs e)
        {
            if (_centerOffsetDlg != null && _centerOffsetDlg.IsVisible)
            {
                _centerOffsetDlg.Activate();
                return;
            }
            _centerOffsetDlg = new CenterOffsetWindow(_centerOffsetX, _centerOffsetY, OnCenterOffsetChanged, (string t) => SetOperationHint(t))
            {
                Owner = this
            };
            _centerOffsetDlg.Closed += (s2, e2) => _centerOffsetDlg = null;
            _centerOffsetDlg.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double dlgW = 400;
            double dlgH = _centerOffsetDlg.DesiredSize.Height > 10 ? _centerOffsetDlg.DesiredSize.Height : 280;
            PositionDialogOverLeftRegion(_centerOffsetDlg, dlgW, dlgH);
            _centerOffsetDlg.Show();
        }

        /// <summary>中心偏移实时变化：更新当前关键词记忆（盖章时按此偏移基准放置，不影响已放置章）。</summary>
        private void OnCenterOffsetChanged(int x, int y)
        {
            _centerOffsetX = x;
            _centerOffsetY = y;
            SaveCenterOffsetFromUi();
        }

        // 随机角度与位移（弹窗设置、随章记忆）：角度 0-360°、位移 0-200mm
        private int _randomAngle = 0;
        private int _randomOffsetMm = 0;
        private RandomParamsWindow _randomDlg;

        /// <summary>打开随机角度与位移弹窗（非模态，与盖章渲染弹窗同规范）：已打开时激活。</summary>
        private void BtnRandomParams_Click(object sender, RoutedEventArgs e)
        {
            if (_randomDlg != null && _randomDlg.IsVisible)
            {
                _randomDlg.Activate();
                return;
            }
            _randomDlg = new RandomParamsWindow(_randomAngle, _randomOffsetMm, OnRandomChanged, (string t) => SetOperationHint(t))
            {
                Owner = this
            };
            _randomDlg.Closed += (s2, e2) => _randomDlg = null;
            _randomDlg.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double dlgW = 400;
            double dlgH = _randomDlg.DesiredSize.Height > 10 ? _randomDlg.DesiredSize.Height : 280;
            PositionDialogOverLeftRegion(_randomDlg, dlgW, dlgH);
            _randomDlg.Show();
        }

        /// <summary>随机参数实时变化：更新当前章记忆（盖章时按此范围随机旋转/位移，不影响已放置章）。</summary>
        private void OnRandomChanged(int angle, int offsetMm)
        {
            _randomAngle = angle;
            _randomOffsetMm = offsetMm;
            SaveCurrentStampParams();
        }

        // 盖章渲染四维上限缓存（UI 无直接控件，由弹窗修改、随印章记忆）
        private int _textureBrightness = 0;
        private int _textureBlob = 0;
        private int _textureGradient = 0;
        private int _textureWhite = 0;
        private int _textureSpot = 0;
        private int _textureRadial = 0;
        private int _textureCast = 0;
        private int _texturePresetIndex = 0;   // 当前渲染参数号（0=自定义，1-4=参数N，随章记忆）
        private TextureSettingsWindow _textureDlg;

        /// <summary>盖章渲染勾选变化：未勾选时"设置参数"按钮置灰（与随机/去除白色背景交互一致）。</summary>
        private void ChkTextureQuality_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTextureEnabled();
            SaveCurrentStampParams();
            if (IsLoaded && !_suppressStampParamSave)
            {
                SetOperationHint("盖章渲染：随机叠加印泥浓淡、斑点、压印等质感效果，模拟真实盖章");
            }
        }

        private void UpdateTextureEnabled()
        {
            bool on = chkTextureQuality.IsChecked == true;
            btnTextureSettings.IsEnabled = on;
            btnTextureSettings.Content = "盖章渲染";   // 文字始终显示，未勾选时仅置灰（与撤销放置按钮一致）
        }

        /// <summary>盖章取章：始终返回当前主章（子印章功能 V128 起删除，原随机选章逻辑存档于 备份/2026-09-11_子印章功能存档与删除）。</summary>
        private Tuple<StampPickerItem, AppConfig.StampParams> PickPlacementStamp()
        {
            StampPickerItem primary = GetSelectedStampItem();
            if (primary == null) return null;
            return Tuple.Create(primary, LoadParamsForStampItem(primary));
        }

        /// <summary>滚动条：点击轨道（非滑块区域）直接跳到点击位置，而不是默认翻一页。</summary>
        private void ScrollViewer_TrackJump(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var sv = sender as ScrollViewer;
            if (sv == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                return;
            }
            var bar = sv.Template.FindName("PART_VerticalScrollBar", sv) as System.Windows.Controls.Primitives.ScrollBar;
            if (bar == null || !bar.IsVisible)
            {
                return;
            }
            System.Windows.Point pInBar = e.GetPosition(bar);
            if (pInBar.X < 0 || pInBar.X > bar.ActualWidth || pInBar.Y < 0 || pInBar.Y > bar.ActualHeight)
            {
                return; // 不在滚动条上
            }
            var track = bar.Template.FindName("PART_Track", bar) as System.Windows.Controls.Primitives.Track;
            if (track == null || track.Thumb == null)
            {
                return;
            }
            System.Windows.Point p = e.GetPosition(track);
            System.Windows.Point thumbOrigin = track.Thumb.TranslatePoint(new System.Windows.Point(0, 0), track);
            if (p.Y >= thumbOrigin.Y - 1 && p.Y <= thumbOrigin.Y + track.Thumb.ActualHeight + 1)
            {
                return; // 在滑块上：交给默认拖拽
            }
            double value = track.ValueFromPoint(p);
            if (double.IsNaN(value))
            {
                return;
            }
            sv.ScrollToVerticalOffset(value);
            e.Handled = true;
        }

        /// <summary>打开盖章渲染参数弹窗（非模态）：修改实时生效，可边调边在预览区盖章调试；已打开时激活。
        /// 打开前：若当前无选中参数，用当前 7 个渲染参数值与已保存的参数 1~4 比对，
        /// 完全一致则自动恢复该参数选中（兜底记忆丢失，V136 修复），保证"当前参数"提示正确。</summary>
        private void BtnTextureSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_textureDlg != null && _textureDlg.IsVisible)
            {
                _textureDlg.Activate();
                return;
            }
            if (_texturePresetIndex < 1 || _texturePresetIndex > 4)
            {
                int detected = DetectTexturePresetFromCurrentValues();
                if (detected != 0)
                {
                    _texturePresetIndex = detected;
                }
            }
            _textureDlg = new TextureSettingsWindow(_textureBrightness, _textureBlob, _textureGradient,
                _textureWhite, _textureSpot, _textureRadial, _textureCast, _texturePresetIndex,
                OnTextureSettingsChanged, OnTexturePresetChanged, OnTextureSettingsHint)
            {
                Owner = this
            };
            _textureDlg.Closed += (s2, e2) => _textureDlg = null;
            // Show 前完成定位，避免先显示默认位置再跳转的闪现：宽固定 500，高用 Measure 期望值（取不到则按 420 估算）
            _textureDlg.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double dlgW = 500;
            double dlgH = _textureDlg.DesiredSize.Height > 10 ? _textureDlg.DesiredSize.Height : 420;
            PositionDialogOverLeftRegion(_textureDlg, dlgW, dlgH);
            _textureDlg.Show();
        }

        /// <summary>用当前 7 个渲染参数值与已保存参数 1~4 比对，完全一致返回对应下标（0=无匹配）。</summary>
        private int DetectTexturePresetFromCurrentValues()
        {
            int[] cur =
            {
                _textureBrightness, _textureBlob, _textureGradient,
                _textureWhite, _textureSpot, _textureRadial, _textureCast
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

        /// <summary>弹窗参数实时变化：更新当前章记忆，并把预览中已放置的章同步为新参数（种子不变），刷新预览。</summary>
        private void OnTextureSettingsChanged(int brightness, int blob, int gradient, int white,
            int spot, int radial, int cast)
        {
            _textureBrightness = brightness;
            _textureBlob = blob;
            _textureGradient = gradient;
            _textureWhite = white;
            _textureSpot = spot;
            _textureRadial = radial;
            _textureCast = cast;
            SaveCurrentStampParams();
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                foreach (int page in stampPlacements.DistinctPages(sourcePath))
                {
                    foreach (StampPlacement placement in stampPlacements.ForPage(sourcePath, page))
                    {
                        placement.TextureBrightness = brightness;
                        placement.TextureBlob = blob;
                        placement.TextureGradient = gradient;
                        placement.TextureWhite = white;
                        placement.TextureSpot = spot;
                        placement.TextureRadial = radial;
                        placement.TextureCast = cast;
                        // 强度系数与分布种子固定于本章，调参只更新参数上限 → 分布稳定、强度平滑变化
                    }
                }
                RefreshPreviewOverlays();
            }
        }

        /// <summary>渲染参数号变化（弹窗内加载/保存/手动修改/恢复默认触发）：随当前章保存，并给操作反馈。
        /// saved=true 表示保存到参数，false 表示加载参数或清选中。</summary>
        private void OnTexturePresetChanged(int idx, bool saved)
        {
            _texturePresetIndex = idx;
            SaveCurrentStampParams();
            if (idx >= 1 && idx <= 4)
            {
                SetOperationHint(saved ? "已保存到参数" + idx : "已加载参数" + idx + "参数");
            }
        }

        /// <summary>盖章渲染参数提示：弹窗内操作某参数时，操作提示区显示该参数的功能说明（操作哪个提示哪个）。</summary>
        private void OnTextureSettingsHint(string text)
        {
            SetOperationHint(text);
        }

        /// <summary>盖章渲染分布种子：每枚章盖章时独立随机（决定斑块/斑点位置，固定于本章）。</summary>
        private int NewTextureSeed()
        {
            lock (StampRandomGenerator)
            {
                return StampRandomGenerator.Next(1, 1000000);
            }
        }

        /// <summary>盖章渲染强度系数：每枚章盖章时随机 0.85~1.15（参数主控、±15% 微调，章与章略有差异）。</summary>
        private float NewTextureK()
        {
            lock (StampRandomGenerator)
            {
                return (float)(0.85 + 0.30 * StampRandomGenerator.NextDouble());
            }
        }

        /// <summary>从界面控件读取当前印章参数快照（MaxSplit 遵循“未手动修改不更新记忆”规则）。</summary>
        private AppConfig.StampParams GetCurrentStampParamsFromUi()
        {
            return new AppConfig.StampParams
            {
                Size = TryParseInt(txtStampSize.Text, 1, 500, out int s) ? s : 40,
                Rotation = TryParseInt(txtRotation.Text, -360, 360, out int r) ? r : 0,
                RotationHandle = comboRotationHandle.SelectedIndex >= 0 ? comboRotationHandle.SelectedIndex : 0,
                Opacity = TryParseInt(txtOpacity.Text, 0, 100, out int o) ? o : 60,
                RandomParams = chkRandomParams.IsChecked == true,
                RandomRange = _randomAngle,
                RandomOffsetMm = _randomOffsetMm,
                RemoveWhite = chkRemoveWhite.IsChecked == true,
                Tolerance = TryParseInt(txtTolerance.Text, 0, 50, out int t) ? t : 20,
                TextureQuality = chkTextureQuality.IsChecked == true,
                TextureBrightness = _textureBrightness,
                TextureBlob = _textureBlob,
                TextureGradient = _textureGradient,
                TextureWhite = _textureWhite,
                TextureSpot = _textureSpot,
                TextureRadial = _textureRadial,
                TextureCast = _textureCast,
                TexturePresetIndex = _texturePresetIndex,
                MaxSplit = _maxSplitUserModified
                    ? (TryParseInt(txtMaxSplit.Text, 1, 10000, out int ms) ? ms : 500)
                    : -1   // 未手动修改：-1 表示不更新印章记忆中的分割数
            };
        }

        /// <summary>把当前界面的印章参数保存到当前主章名下（显示名 key）。</summary>
        private void SaveCurrentStampParams()
        {
            if (_suppressStampParamSave || string.IsNullOrEmpty(_currentStampFileName))
            {
                return;
            }
            try
            {
                AppConfig.SaveStampParams(_currentStampFileName, GetCurrentStampParamsFromUi());
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>骑缝章分割数跟随文档自动调整：非“不加骑缝章”且非目录模式时，
        /// 按当前骑缝章类型的实际骑缝章页数重置分割数（单页=奇数页数、双页=偶数页数、加盖/随意=总页数）。
        /// 自动重置不算手动修改，不写入印章记忆。</summary>
        private void UpdateMaxSplitFromPdf()
        {
            if (currentSourceIsDirectory || pdfRenderer == null || pageCount <= 0)
            {
                return; // 目录模式各文件页数不同，不自动重置
            }
            int qfzType = SeamBusinessFromDisplay(comboSeam.SelectedIndex);
            if (qfzType == 1)
            {
                return; // 不加骑缝章：保留原值
            }
            // 所有骑缝章类型统一按总页数作为默认分割数：
            // 引擎按实际骑缝章页数分配章条，单页/双页无需减半，直接给总页数即可。
            int pages = pageCount;
            if (pages < 1) pages = 1;
            _suppressMaxSplitTrack = true;
            _maxSplitUserModified = false;
            txtMaxSplit.Text = pages.ToString();
            _suppressMaxSplitTrack = false;
        }

        /// <summary>把印章参数应用到界面控件。</summary>
        private void ApplyStampParams(AppConfig.StampParams p)
        {
            if (p == null)
            {
                return;
            }
            _suppressStampParamSave = true;
            txtStampSize.Text = p.Size.ToString();
            txtRotation.Text = p.Rotation.ToString();
            comboRotationHandle.SelectedIndex = (p.RotationHandle >= 0 && p.RotationHandle <= 1) ? p.RotationHandle : 0;
            txtOpacity.Text = p.Opacity.ToString();
            chkRandomParams.IsChecked = p.RandomParams;
            _randomAngle = p.RandomRange;
            _randomOffsetMm = p.RandomOffsetMm;
            UpdateRandomEnabled();
            chkRemoveWhite.IsChecked = p.RemoveWhite;
            txtTolerance.Text = p.Tolerance.ToString();
            chkTextureQuality.IsChecked = p.TextureQuality;
            _textureBrightness = p.TextureBrightness;
            _textureBlob = p.TextureBlob;
            _textureGradient = p.TextureGradient;
            _textureWhite = p.TextureWhite;
            _textureSpot = p.TextureSpot;
            _textureRadial = p.TextureRadial;
            _textureCast = p.TextureCast;
            _texturePresetIndex = p.TexturePresetIndex;
            UpdateTextureEnabled();
            if (p.MaxSplit > 0)
            {
                _suppressMaxSplitTrack = true;
                _maxSplitUserModified = false;
                txtMaxSplit.Text = p.MaxSplit.ToString();
                _suppressMaxSplitTrack = false;
            }
            UpdateToleranceEnabled();
            _suppressStampParamSave = false;
        }

        private void OnSourceDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    // 拖入文件夹 → 目录模式；拖入文件 → 文件模式（自动识别，不再需要模式单选）
                    if (Directory.Exists(files[0]))
                    {
                        // 输出目录由 LoadDirectory 统一设为“拖入文件夹\已盖章”
                        LoadDirectory(files[0]);
                    }
                    else if (File.Exists(files[0]))
                    {
                        // 拖入源 PDF：自动补保存目录为源文件同目录
                        if (string.IsNullOrWhiteSpace(txtOutputDir.Text))
                        {
                            txtOutputDir.Text = Path.GetDirectoryName(files[0]);
                        }
                        LoadSourceFiles(files);
                    }
                }
            }
        }

        // ===================== 加载 PDF / 渲染预览 =====================
        private void ResetPreview()
        {
            stampPlacements.Clear();
            autoStampOperations.Clear();
            ClearOverlayImages();
            ReleasePdfResources();
            ShowBlankDebugPage();
            comboCurrentFile.Items.Clear();
            txtFileTotalPages.Text = "";
            btnUndoAuto.IsEnabled = false;
            logText.Text = InitialHelpText;
            logContainsOnlyHelp = true;
            _logPinToBottom = true;
            UpdatePlacementOperationHint();
        }

        /// <summary>无 PDF 时加载内置调试 PDF（A4 白页 + "拖入或选择：PDF文件/文件夹"），走真实 pdfium 渲染链路，
        /// 预览区未加载用户文件也可盖章、缩放、拖动，用于调试盖章渲染参数。
        /// 调试页不进入用户文件列表，不会参与输出；调试章与正常章同用 stampPlacements（加载新文件/重置时自动清空）。</summary>
        private void ShowBlankDebugPage()
        {
            try
            {
                string debugPath = ExtractDebugPdf();
                if (string.IsNullOrEmpty(debugPath) || !File.Exists(debugPath))
                {
                    _debugPageActive = false;
                    return;
                }
                _debugPageActive = true;
                ReleasePdfResources();
                pdfRenderer = PdfiumDocumentRenderer.Open(debugPath);
                pageCount = pdfRenderer.PageCount;
                pageCache = new PageCache<System.Drawing.Bitmap>(i => pdfRenderer.RenderPage(i, RenderDpi));
                currentPageIndex = 0;
                sourcePath = debugPath;
                zoomPercent = 0;
                zoomInitialized = false;
                RelayoutPreview();
                UpdatePageInfo();
                UpdatePlacementOperationHint();
            }
            catch
            {
                _debugPageActive = false;
                previewImage.Visibility = Visibility.Collapsed;
                overlayCanvas.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>从嵌入资源提取内置调试 PDF 到 %TEMP%\PDFQFZ\debug_page.pdf（已存在则跳过）。</summary>
        private string ExtractDebugPdf()
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PDFQFZ");
                Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "debug_page.pdf");
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("PDFQFZ.WPF.assets.debug_page.pdf"))
                {
                    if (stream == null)
                    {
                        return null;
                    }
                    // 资源读入内存
                    byte[] resBytes;
                    using (var ms = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(ms);
                        resBytes = ms.ToArray();
                    }
                    // 缓存失效：磁盘缓存与嵌入资源内容一致才复用；不一致（版本更新/旧文字残留）则强制覆盖
                    if (File.Exists(path))
                    {
                        byte[] diskBytes = File.ReadAllBytes(path);
                        if (diskBytes.Length == resBytes.Length)
                        {
                            bool same = true;
                            for (int i = 0; i < resBytes.Length; i++)
                            {
                                if (diskBytes[i] != resBytes[i]) { same = false; break; }
                            }
                            if (same)
                            {
                                return path;
                            }
                        }
                    }
                    File.WriteAllBytes(path, resBytes);
                }
                return path;
            }
            catch
            {
                return null;
            }
        }

        private void LoadPdf(string path, bool keepStampData = false)
        {
            try
            {
                // 目录模式内切换“当前文件”：保留各文件已放置的印章数据（多文件按文字盖章后切换预览仍需显示）；
                // 拖入新源文件/新目录（默认 false）：全新任务，清空所有印章记录。
                if (!keepStampData)
                {
                    stampPlacements.Clear();
                    autoStampOperations.Clear();
                }
                ClearOverlayImages();
                ReleasePdfResources();
                logText.Text = InitialHelpText;   // 拖入新文件：预览与日志区都恢复初始帮助说明
                logContainsOnlyHelp = true;
                _logPinToBottom = true;

                sourcePath = path;
                pdfRenderer = PdfiumDocumentRenderer.Open(path);
                pageCount = pdfRenderer.PageCount;
                pageCache = new PageCache<Bitmap>(i => pdfRenderer.RenderPage(i, RenderDpi));
                currentPageIndex = 0;
                // 同步“当前文件”下拉的高亮（下拉列表由 LoadSourceFiles/LoadDirectory 统一填充，这里不重建）
                suppressCurrentFileEvent = true;
                for (int i = 0; i < comboCurrentFile.Items.Count; i++)
                {
                    if (comboCurrentFile.Items[i] is ComboBoxItem ci && ci.Tag is string tp &&
                        string.Equals(tp, path, StringComparison.OrdinalIgnoreCase))
                    {
                        comboCurrentFile.SelectedIndex = i;
                        break;
                    }
                }
                suppressCurrentFileEvent = false;
                txtFileTotalPages.Text = "共 " + pageCount + " 页";
                _debugPageActive = false;
                previewImage.Visibility = Visibility.Visible;
                overlayCanvas.Visibility = Visibility.Visible;
                btnUndoAuto.IsEnabled = keepStampData
                    ? autoStampOperations.Any(o => string.Equals(o.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                    : false;
                zoomPercent = 0;
                zoomInitialized = false;
                UpdatePageInfo();
                RelayoutPreview();
                UpdatePlacementOperationHint();
                UpdateMaxSplitFromPdf();
            }
            catch (Exception ex)
            {
                // 完整异常链（含 InnerException 底层原因）弹窗提示，便于定位引擎加载问题
                string chain = PDFQFZ.WPF.Services.PdfiumBootstrap.BuildExceptionChain(ex);
                AppendLog("加载失败：" + ex.Message, true);
                MessageBox.Show("加载 PDF 失败：" + Environment.NewLine + chain, "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ReleasePdfResources()
        {
            if (pageCache != null) { pageCache.Clear(); pageCache = null; }
            if (pdfRenderer != null) { pdfRenderer.Dispose(); pdfRenderer = null; }
            previewImage.Source = null;
            previewImageLeft.Source = null;
            previewImageRight.Source = null;
        }

        private void ChangePage(int delta)
        {
            if (pdfRenderer == null) return;
            int target = currentPageIndex + delta * PageStep;
            if (target < 0 || target >= pageCount) return;
            currentPageIndex = target;
            UpdatePageInfo();
            RelayoutPreview();
        }

        private void UpdatePageInfo()
        {
            if (previewViewMode == PreviewViewMode.DoublePage)
            {
                int left = currentPageIndex + 1;
                int right = currentPageIndex + 2;
                txtPageNow.Text = right <= pageCount ? left + "-" + right : left.ToString();
            }
            else
            {
                txtPageNow.Text = (pageCount == 0 ? 0 : currentPageIndex + 1).ToString();
            }
            txtPageTotal.Text = "/ " + pageCount + " 页";
            UpdatePageNavButtons();
        }

        /// <summary>按预览区大小 + 当前视图模式布局页面，并刷新叠加层。</summary>
        private void RelayoutPreview()
        {
            if (pdfRenderer == null || pageCache == null)
            {
                return;
            }

            // 读取视口前先强制同步布局：视图切换（放大→单页）时滚动条刚从 Auto 改为 Disabled，
            // 布局尚未刷新，此时 ViewportWidth/Height 仍是带滚动条的旧值，会导致页面按窄视口重算、比正常单页小。
            if (previewScroll != null) previewScroll.UpdateLayout();
            // 用 ScrollViewer 真正的内容视口尺寸（ViewportWidth/Height），而非控件 ActualWidth/Height
            // 避免 ScrollViewer 模板边框导致视口与控件尺寸不一致
            double vw = previewScroll.ViewportWidth > 0 ? previewScroll.ViewportWidth : previewScroll.ActualWidth;
            double vh = previewScroll.ViewportHeight > 0 ? previewScroll.ViewportHeight : previewScroll.ActualHeight;
            if (vw <= 20 || vh <= 20)
            {
                return;
            }

            try
            {
                // 放大视图：记录当前滚动比例（偏移/可滚动范围），切换页面后按比例恢复，避免页面跳动（对齐原版 LayoutPreviewPage 的 ratioX/ratioY）
                double oldScrollableW = previewScroll.ScrollableWidth;
                double oldScrollableH = previewScroll.ScrollableHeight;
                double oldOffsetX = previewScroll.HorizontalOffset;
                double oldOffsetY = previewScroll.VerticalOffset;
                double ratioX = oldScrollableW > 0 ? oldOffsetX / oldScrollableW : 0;
                double ratioY = oldScrollableH > 0 ? oldOffsetY / oldScrollableH : 0;
                bool isScrollMode = previewViewMode == PreviewViewMode.Scroll;

                Bitmap bmp = pageCache.GetPage(currentPageIndex);
                if (bmp == null) return;
                previewImage.Source = ToBitmapSource(bmp);
                basePageWidthPx = bmp.Width;
                basePageHeightPx = bmp.Height;

                // 整页适应预览区（四周围 18 边距）＝单页视图尺寸、放大视图 100% 基准（对齐原版 LayoutPreviewPage）
                System.Drawing.Size fitSize = PreviewViewportLayout.Calculate(
                    (int)vw, (int)vh, (int)basePageWidthPx, (int)basePageHeightPx, 18);
                fitWidthPx = fitSize.Width;
                fitHeightPx = fitSize.Height;

                if (!zoomInitialized)
                {
                    // 首次加载：单页视图，整页完整显示，zoom 显示 100%
                    zoomPercent = PreviewZoomPolicy.MinimumPercent;
                    previewViewMode = PreviewViewMode.SinglePage;
                    zoomInitialized = true;
                    UpdateViewModeButtons();
                }

                if (previewViewMode == PreviewViewMode.SinglePage)
                {
                    // 单页视图：整页完整显示，zoom 显示 100%，缩小按钮禁用，禁用滚动条
                    if (previewScroll != null)
                    {
                        previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    }
                    displayWidth = fitWidthPx;
                    displayHeight = fitHeightPx;
                    txtZoomNow.Text = PreviewZoomPolicy.MinimumPercent.ToString();
                    btnZoomOut.IsEnabled = false;
                    btnZoomIn.IsEnabled = true;
                }
                else if (previewViewMode == PreviewViewMode.DoublePage)
                {
                    // 双页视图：左右两页并排整页适应（间距 12px），固定 100%，禁用滚动条与缩放按钮
                    if (previewScroll != null)
                    {
                        previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    }
                    Bitmap rightBmp = currentPageIndex + 1 < pageCount ? pageCache.GetPage(currentPageIndex + 1) : null;
                    previewImageLeft.Source = ToBitmapSource(bmp);
                    if (rightBmp != null) previewImageRight.Source = ToBitmapSource(rightBmp);

                    double gap = DoublePageGap;
                    // 两页并排整体适应预览区（四周围 18 边距）：先按宽度，超高再按高度
                    double pageW = (vw - 18 * 2 - gap) / 2;
                    if (pageW < 50) pageW = 50;
                    double leftH = pageW * basePageHeightPx / basePageWidthPx;
                    double rightH = rightBmp != null ? pageW * rightBmp.Height / rightBmp.Width : leftH;
                    double maxH = Math.Max(leftH, rightH);
                    if (maxH > vh - 36)
                    {
                        maxH = vh - 36;
                        pageW = maxH * basePageWidthPx / basePageHeightPx;
                        leftH = maxH;
                        rightH = rightBmp != null ? pageW * rightBmp.Height / rightBmp.Width : maxH;
                    }
                    double totalW = rightBmp != null ? pageW * 2 + gap : pageW;

                    doublePageLeftW = pageW;
                    doublePageLeftH = leftH;
                    doublePageRightW = pageW;
                    doublePageRightH = rightH;

                    leftPagePane.Width = pageW;
                    leftPagePane.Height = leftH;
                    previewImageLeft.Width = pageW;
                    previewImageLeft.Height = leftH;
                    overlayCanvasLeft.Width = pageW;
                    overlayCanvasLeft.Height = leftH;
                    rightPagePane.Width = pageW;
                    rightPagePane.Height = rightH;
                    previewImageRight.Width = pageW;
                    previewImageRight.Height = rightH;
                    overlayCanvasRight.Width = pageW;
                    overlayCanvasRight.Height = rightH;
                    rightPagePane.Visibility = rightBmp != null ? Visibility.Visible : Visibility.Collapsed;

                    displayWidth = pageW;
                    displayHeight = leftH;
                    txtZoomNow.Text = PreviewZoomPolicy.MinimumPercent.ToString();
                    btnZoomOut.IsEnabled = false;
                    btnZoomIn.IsEnabled = false;

                    // 双页容器显示，单页元素隐藏
                    doublePaneHost.Visibility = Visibility.Visible;
                    previewImage.Visibility = Visibility.Collapsed;
                    overlayCanvas.Visibility = Visibility.Collapsed;
                    previewHost.Width = Math.Max(totalW, vw);
                    previewHost.Height = Math.Max(maxH, vh);
                    previewHost.HorizontalAlignment = HorizontalAlignment.Center;
                    previewHost.VerticalAlignment = VerticalAlignment.Center;
                }
                else
                {
                    // 放大视图：fit 基础上按百分比缩放（100-300），可滚动，启用滚动条
                    if (previewScroll != null)
                    {
                        previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                        previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                    displayWidth = fitWidthPx * zoomPercent / 100.0;
                    displayHeight = fitHeightPx * zoomPercent / 100.0;
                    txtZoomNow.Text = ((int)Math.Round(zoomPercent)).ToString();
                    btnZoomOut.IsEnabled = zoomPercent > PreviewZoomPolicy.MinimumPercent;
                    btnZoomIn.IsEnabled = zoomPercent < PreviewZoomPolicy.MaximumPercent;
                }

                // 单页/放大视图共用的单页元素布局；双页视图已在上面分支自行布局，跳过
                if (previewViewMode != PreviewViewMode.DoublePage)
                {
                    // 内容撑开并居中：预览页面不足视口时居中，超过视口时可滚动
                    previewHost.Width = Math.Max(displayWidth, vw);
                    previewHost.Height = Math.Max(displayHeight, vh);
                    // 单页视图：previewHost 居中对齐，确保页面始终在预览区正中间；放大视图：左上对齐，支持滚动拖动
                    if (previewViewMode == PreviewViewMode.SinglePage)
                    {
                        previewHost.HorizontalAlignment = HorizontalAlignment.Center;
                        previewHost.VerticalAlignment = VerticalAlignment.Center;
                    }
                    else
                    {
                        previewHost.HorizontalAlignment = HorizontalAlignment.Left;
                        previewHost.VerticalAlignment = VerticalAlignment.Top;
                    }
                    previewImage.Width = displayWidth;
                    previewImage.Height = displayHeight;
                    previewImage.HorizontalAlignment = HorizontalAlignment.Center;
                    previewImage.VerticalAlignment = VerticalAlignment.Center;
                    previewImage.Margin = new Thickness(0);

                    overlayCanvas.Width = displayWidth;
                    overlayCanvas.Height = displayHeight;
                    overlayCanvas.HorizontalAlignment = HorizontalAlignment.Center;
                    overlayCanvas.VerticalAlignment = VerticalAlignment.Center;

                    // 双页容器隐藏，单页元素显示
                    doublePaneHost.Visibility = Visibility.Collapsed;
                    previewImage.Visibility = Visibility.Visible;
                    overlayCanvas.Visibility = Visibility.Visible;
                }

                // 放大视图：布局完成后恢复滚动位置
                // 滚轮连续翻页（wheelPageScrollMode≠0）：新页强制滚到顶部/底部（连续阅读）；
                // 其余场景（按钮翻页/指定范围跳页/缩放）按旧滚动比例恢复，保持页面相对位置不变（对齐原版）
                if (isScrollMode && (oldScrollableW > 0 || oldScrollableH > 0) && wheelPageScrollMode == 0)
                {
                    double rx = ratioX;
                    double ry = ratioY;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (previewViewMode != PreviewViewMode.Scroll) return;
                        double newX = rx * previewScroll.ScrollableWidth;
                        double newY = ry * previewScroll.ScrollableHeight;
                        previewScroll.ScrollToHorizontalOffset(Math.Max(0, Math.Min(newX, previewScroll.ScrollableWidth)));
                        previewScroll.ScrollToVerticalOffset(Math.Max(0, Math.Min(newY, previewScroll.ScrollableHeight)));
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                if (wheelPageScrollMode != 0)
                {
                    int mode = wheelPageScrollMode;
                    wheelPageScrollMode = 0;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (previewViewMode != PreviewViewMode.Scroll) return;
                        if (mode == 1) previewScroll.ScrollToVerticalOffset(0);
                        else if (mode == 2) previewScroll.ScrollToVerticalOffset(previewScroll.ScrollableHeight);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                UpdatePageNavButtons();
                RefreshPreviewOverlays();
            }
            catch (Exception ex)
            {
                AppendLog("渲染失败：" + ex.Message, true);
            }
        }

        // ===================== 缩放控制 =====================
        /// <summary>缩放后根据百分比同步视图模式：超过 100% 自动进入放大视图；从放大视图缩小回 100% 自动回到单页视图（无滚动条、整页居中、预览区不被滚动条压缩）。</summary>
        private void SyncViewModeAfterZoom(int percent)
        {
            if (previewViewMode == PreviewViewMode.SinglePage && percent > PreviewZoomPolicy.MinimumPercent)
            {
                previewViewMode = PreviewViewMode.Scroll;
            }
            else if (previewViewMode == PreviewViewMode.Scroll && percent <= PreviewZoomPolicy.MinimumPercent)
            {
                previewViewMode = PreviewViewMode.SinglePage;
                if (previewScroll != null)
                {
                    previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    previewScroll.ScrollToHome();
                }
            }
        }

        /// <summary>缩放一步（对齐原版：步进 25，范围 100-300；放大视图时超出 100 自动切放大视图）。</summary>
        private void ZoomStep(int direction)
        {
            int next = PreviewZoomPolicy.Step((int)Math.Round(zoomPercent), direction);
            zoomPercent = next;
            SyncViewModeAfterZoom(next);
            UpdateViewModeButtons();
            RelayoutPreview();
            UpdatePlacementOperationHint();
        }

        private void FitPage()
        {
            // 单页视图：整页完整显示，zoom 固定 100%，禁用滚动条
            zoomPercent = PreviewZoomPolicy.MinimumPercent;
            previewViewMode = PreviewViewMode.SinglePage;
            if (previewScroll != null)
            {
                previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                previewScroll.ScrollToHome();
            }
            UpdateViewModeButtons();
            RelayoutPreview();
            UpdatePlacementOperationHint();
        }

        /// <summary>双页视图：两页并排整页适应，固定 100% 不缩放不拖动；翻页按跨页（2 页）步进。</summary>
        private void FitDouble()
        {
            previewViewMode = PreviewViewMode.DoublePage;
            // 规整到包含当前页的跨页左页（0-based 偶数页），保证双页从奇数页码开始
            currentPageIndex = (currentPageIndex / 2) * 2;
            if (previewScroll != null)
            {
                previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                previewScroll.ScrollToHome();
            }
            UpdateViewModeButtons();
            UpdatePageInfo();
            RelayoutPreview();
            UpdatePlacementOperationHint();
        }

        private void FitWidth()
        {
            // 放大视图：进入时自动放大一档到 125%（整页 fit 的 125%，页面超出预览区，滚轮滚动/拖动立即有反馈），可继续放大到 300%
            zoomPercent = PreviewZoomPolicy.FitWidthEntryPercent;
            previewViewMode = PreviewViewMode.Scroll;
            if (previewScroll != null)
            {
                previewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                previewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            UpdateViewModeButtons();
            RelayoutPreview();
            UpdatePlacementOperationHint();
        }

        // 单页视图 / 双页视图 / 放大视图 三选一选中态（ToggleButton 系统规范）
        private bool suppressViewToggle;

        private void UpdateViewModeButtons()
        {
            suppressViewToggle = true;
            btnFitPage.IsChecked = previewViewMode == PreviewViewMode.SinglePage;
            btnFitDouble.IsChecked = previewViewMode == PreviewViewMode.DoublePage;
            btnFitWidth.IsChecked = previewViewMode == PreviewViewMode.Scroll;
            suppressViewToggle = false;
        }

        // 代码同步选中态时返回 false，避免再次触发 FitPage/FitWidth
        private bool IsViewToggleHandled()
        {
            return !suppressViewToggle;
        }

        // 已选中的 ToggleButton 被再次点击（Unchecked）时，强制保持选中（单页/放大必须二选一）
        private void OnViewToggleUnchecked(object sender, System.Windows.RoutedEventArgs e)
        {
            if (suppressViewToggle) return;
            var tb = (System.Windows.Controls.Primitives.ToggleButton)sender;
            if (tb.IsChecked != true)
            {
                suppressViewToggle = true;
                tb.IsChecked = true;
                suppressViewToggle = false;
            }
        }

        private void ApplyZoomInput()
        {
            if (!PreviewZoomPolicy.TryParse(txtZoomNow.Text, out int percent, out string error))
            {
                txtZoomNow.Text = ((int)Math.Round(zoomPercent)).ToString();
                AppendLog(error, true);
                return;
            }
            zoomPercent = percent;
            SyncViewModeAfterZoom(percent);
            UpdateViewModeButtons();
            RelayoutPreview();
            UpdatePlacementOperationHint();
        }

        // ===================== 页码跳转 =====================
        private void GoToPageInput()
        {
            if (pdfRenderer == null || pageCount <= 0) return;
            if (!int.TryParse(txtPageNow.Text.Trim(), out int v))
            {
                // 双页视图页码格式 "3-4"：取 '-' 前的左页页码
                int dash = txtPageNow.Text.IndexOf('-');
                if (dash > 0 && int.TryParse(txtPageNow.Text.Substring(0, dash).Trim(), out int l))
                {
                    v = l;
                }
                else
                {
                    UpdatePageInfo();
                    return;
                }
            }
            if (v < 1) v = 1;
            if (v > pageCount) v = pageCount;
            // 双页视图：跳到包含第 v 页的跨页（左页为 0-based 偶数）
            currentPageIndex = previewViewMode == PreviewViewMode.DoublePage ? ((v - 1) / 2) * 2 : v - 1;
            UpdatePageInfo();
            RelayoutPreview();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (pdfRenderer == null || pageCount < 1 || e.Delta == 0) return;

            // Ctrl+滚轮：缩放（步进 10，对齐原版 PreviewZoomPolicy.StepByWheel；双页视图固定不缩放，禁用）
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                if (previewViewMode == PreviewViewMode.DoublePage) return;
                int direction = e.Delta > 0 ? 1 : -1;
                int next = PreviewZoomPolicy.StepByWheel((int)Math.Round(zoomPercent), direction);
                zoomPercent = next;
                SyncViewModeAfterZoom(next);
                UpdateViewModeButtons();
                RelayoutPreview();
                UpdatePlacementOperationHint();
                return;
            }

            // 放大视图：普通滚轮滚动页面；滚到边缘继续滚则翻页（连续阅读，对齐 PDF 阅读器通用行为）
            if (previewViewMode == PreviewViewMode.Scroll)
            {
                if (previewScroll == null) return;
                const double edge = 2.0;   // 边缘判定容差（px），抵消浮点精度
                bool atBottom = previewScroll.VerticalOffset >= previewScroll.ScrollableHeight - edge;
                bool atTop = previewScroll.VerticalOffset <= edge;
                // 向下滚到底：翻下一页（新页从顶部开始，便于继续向下滚连续阅读）
                if (e.Delta < 0 && atBottom && currentPageIndex < pageCount - 1)
                {
                    e.Handled = true;
                    currentPageIndex++;
                    wheelPageScrollMode = 1;
                    UpdatePageInfo();
                    RelayoutPreview();
                    UpdatePlacementOperationHint();
                    return;
                }
                // 向上滚到顶：翻上一页（回到上一页底部，便于继续向上滚连续回翻）
                if (e.Delta > 0 && atTop && currentPageIndex > 0)
                {
                    e.Handled = true;
                    currentPageIndex--;
                    wheelPageScrollMode = 2;
                    UpdatePageInfo();
                    RelayoutPreview();
                    UpdatePlacementOperationHint();
                    return;
                }
                return;   // 未到边缘：交给 ScrollViewer 正常滚动
            }

            // 单页/双页视图：滚轮翻页（对齐原版：累积 delta 达到一步翻一页；双页视图一步翻一个跨页）
            e.Handled = true;
            previewWheelDeltaRemainder += e.Delta;
            const int wheelThreshold = 120;
            if (Math.Abs(previewWheelDeltaRemainder) < wheelThreshold) return;
            int wheelStep = Math.Sign(previewWheelDeltaRemainder) * wheelThreshold;
            previewWheelDeltaRemainder -= wheelStep;
            int targetPage = currentPageIndex + (wheelStep > 0 ? -1 : 1) * PageStep;
            if (targetPage >= 0 && targetPage < pageCount)
            {
                currentPageIndex = targetPage;
                UpdatePageInfo();
                RelayoutPreview();
                UpdatePlacementOperationHint();
            }
        }

        /// <summary>上一页/下一页边界禁用（对齐原版：第一页禁用上一页、最后一页禁用下一页；双页视图按跨页步进）。</summary>
        private void UpdatePageNavButtons()
        {
            if (btnPrev == null || btnNext == null) return;
            btnPrev.IsEnabled = pdfRenderer != null && currentPageIndex >= PageStep;
            btnNext.IsEnabled = pdfRenderer != null && currentPageIndex + PageStep < pageCount;
        }

        /// <summary>键盘翻页（对齐原版 PreviewNavigation_KeyDown）。</summary>
        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.PageUp && e.Key != Key.PageDown &&
                e.Key != Key.Up && e.Key != Key.Down && e.Key != Key.Left && e.Key != Key.Right) return;
            // 焦点在输入控件时不拦截，避免影响输入
            var focused = Keyboard.FocusedElement as System.Windows.Controls.Control;
            if (focused is System.Windows.Controls.TextBox ||
                focused is System.Windows.Controls.Primitives.TextBoxBase ||
                focused is System.Windows.Controls.ComboBox ||
                focused is System.Windows.Controls.PasswordBox) return;
            if (pdfRenderer == null || pageCount < 1) return;

            if (e.Key == Key.PageUp || e.Key == Key.Up || e.Key == Key.Left)
            {
                if (currentPageIndex >= PageStep)
                {
                    currentPageIndex -= PageStep;
                    UpdatePageInfo();
                    RelayoutPreview();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown || e.Key == Key.Down || e.Key == Key.Right)
            {
                if (currentPageIndex + PageStep < pageCount)
                {
                    currentPageIndex += PageStep;
                    UpdatePageInfo();
                    RelayoutPreview();
                }
                e.Handled = true;
            }
        }

        private static BitmapSource ToBitmapSource(Bitmap bmp)
        {
            // 统一转成 32bppArgb 后按 Bgra32 直接复制像素，避免预乘 alpha 转换引入的显示问题
            Bitmap source = bmp;
            bool disposeSource = false;
            if (source.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            {
                source = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(source))
                {
                    g.DrawImage(bmp, 0, 0, source.Width, source.Height);
                }
                disposeSource = true;
            }
            BitmapData bd = source.LockBits(
                new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                byte[] pixels = new byte[stride * source.Height];
                Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
                return BitmapSource.Create(source.Width, source.Height, 96, 96,
                    PixelFormats.Bgra32, null, pixels, stride);
            }
            finally
            {
                source.UnlockBits(bd);
                if (disposeSource) source.Dispose();
            }
        }

        // ===================== 预览叠加（Canvas） =====================
        private void RefreshPreviewOverlays()
        {
            ClearOverlayImages();
            if (pdfRenderer == null || string.IsNullOrWhiteSpace(sourcePath) || displayWidth <= 0)
            {
                return;
            }

            if (previewViewMode == PreviewViewMode.DoublePage)
            {
                // 双页视图：左页印章画到左 overlayCanvas，右页印章画到右 overlayCanvas
                RenderPageOverlays(currentPageIndex, overlayCanvasLeft, doublePageLeftW, doublePageLeftH);
                if (currentPageIndex + 1 < pageCount)
                {
                    RenderPageOverlays(currentPageIndex + 1, overlayCanvasRight, doublePageRightW, doublePageRightH);
                }
            }
            else
            {
                RenderPageOverlays(currentPageIndex, overlayCanvas, displayWidth, displayHeight);
            }
        }

        /// <summary>把指定页的印章按显示尺寸渲染到指定 Canvas 上（单页/双页共用）。</summary>
        private void RenderPageOverlays(int pageIndex, System.Windows.Controls.Canvas canvas, double dispW, double dispH)
        {
            if (dispW <= 0 || dispH <= 0) return;
            Bitmap current = pageCache.GetPage(pageIndex);
            if (current == null) return;
            // 页面物理宽度（pt）→ 显示换算基准
            float pdfWidthPoints = current.Width * 72f / RenderDpi;

            foreach (StampPlacement placement in stampPlacements.ForPage(sourcePath, pageIndex + 1))
            {
                try
                {
                    using (Bitmap stampBitmap = StampEngine.CreatePlacementBitmap(placement))
                    {
                        System.Drawing.Size overlaySize = PreviewStampLayout.CalculateOverlaySize(
                            placement.SizeMm,
                            stampBitmap.HorizontalResolution,
                            stampBitmap.Width,
                            stampBitmap.Height,
                            (float)dispW,
                            pdfWidthPoints,
                            80);
                        BitmapSource stampSource = ToBitmapSource(stampBitmap);
                        var image = new System.Windows.Controls.Image
                        {
                            Width = overlaySize.Width,
                            Height = overlaySize.Height,
                            Stretch = Stretch.Fill,
                            Source = stampSource,
                            Tag = placement.Id,
                            Cursor = Cursors.Hand
                        };
                        // 随机旋转：绘制层中心旋转（布局尺寸恒定=SizeMm，预览与输出角度一致，命中检测按旋转后区域）
                        if (placement.RandomRotation && placement.Rotation != 0)
                        {
                            image.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                            image.RenderTransform = new RotateTransform(placement.Rotation);
                        }
                        double overlayLeft = placement.CenterRatio
                            ? dispW * placement.X - overlaySize.Width / 2.0
                            : (dispW - overlaySize.Width) * placement.X;
                        double overlayTop = placement.CenterRatio
                            ? dispH * placement.Y - overlaySize.Height / 2.0
                            : (dispH - overlaySize.Height) * placement.Y;
                        // 随机位移：mm → 显示像素（页面物理宽 pdfWidthPoints(pt) ↔ 显示宽 dispW(px)）
                        if (placement.OffsetXmm != 0f || placement.OffsetYmm != 0f)
                        {
                            double mmToPx = (72.0 / 25.4) * (dispW / pdfWidthPoints);
                            overlayLeft += placement.OffsetXmm * mmToPx;
                            overlayTop += placement.OffsetYmm * mmToPx;
                        }
                        // 出界自动移回页面内（按维度：章子比页面还大时保持中心出界裁剪，水印大章不受影响）
                        if (overlaySize.Width <= dispW)
                        {
                            overlayLeft = Math.Min(Math.Max(overlayLeft, 0.0), dispW - overlaySize.Width);
                        }
                        if (overlaySize.Height <= dispH)
                        {
                            overlayTop = Math.Min(Math.Max(overlayTop, 0.0), dispH - overlaySize.Height);
                        }
                        System.Windows.Controls.Canvas.SetLeft(image, overlayLeft);
                        System.Windows.Controls.Canvas.SetTop(image, overlayTop);
                        canvas.Children.Add(image);
                        overlayImages[placement.Id] = image;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("叠加显示异常:" + ex.Message, true);
                }
            }
        }

        private void ClearOverlayImages()
        {
            foreach (var image in overlayImages.Values)
            {
                overlayCanvas.Children.Remove(image);
                overlayCanvasLeft.Children.Remove(image);
                overlayCanvasRight.Children.Remove(image);
            }
            overlayImages.Clear();
        }

        /// <summary>删除印章：由 overlayCanvas 右键事件统一调用（e.OriginalSource 是印章 Image）。</summary>
        private void DeleteStampByImage(System.Windows.Controls.Image image)
        {
            if (image == null || image.Tag == null) return;

            int placementId = Convert.ToInt32(image.Tag);
            StampPlacement placement = stampPlacements.Find(placementId);
            if (placement == null) return;

            bool removed = false;
            if (placement.BatchId > 0)
            {
                bool isAutoStamp = autoStampOperations.Any(o => o.BatchId == placement.BatchId);
                MessageBoxResult choice = MessageBox.Show(
                    isAutoStamp
                        ? "这是由“按文字盖章”添加的印章。点击“是”删除整个批次，点击“否”仅删除这一枚，点击“取消”保留。"
                        : "这是由“指定范围页盖章”添加的批量印章。点击“是”删除整个批次，点击“否”仅删除当前页，点击“取消”保留。",
                    "删除印章",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (choice == MessageBoxResult.Yes)
                {
                    removed = stampPlacements.RemoveBatch(sourcePath, placement.BatchId) > 0;
                }
                else if (choice == MessageBoxResult.No)
                {
                    if (isAutoStamp)
                    {
                        removed = stampPlacements.Remove(placementId);
                    }
                    else
                    {
                        removed = stampPlacements.RemoveBatchOnPage(sourcePath, placement.Page, placement.BatchId) > 0;
                    }
                }
            }
            else
            {
                removed = stampPlacements.Remove(placementId);
            }

            if (removed)
            {
                if (placement.BatchId > 0 && !stampPlacements.HasBatch(sourcePath, placement.BatchId))
                {
                    autoStampOperations.RemoveAll(o => o.BatchId == placement.BatchId);
                    btnUndoAuto.IsEnabled = autoStampOperations.Count > 0;
                }
                RefreshPreviewOverlays();
            }
        }

        // ===================== 点击盖章 + 拖动平移 =====================

        /// <summary>
        /// 实时命中检测：返回指定坐标下带 Tag 的印章 Image（单页/双页左右 Canvas 共用）。
        /// 不依赖 e.OriginalSource——盖章会在鼠标下方动态创建新 Image，
        /// WPF 缓存的命中元素在鼠标移动前不会刷新，会导致不移动鼠标时命中检测失效。
        /// </summary>
        private System.Windows.Controls.Image HitTestStampImage(System.Windows.Point canvasPos, System.Windows.Controls.Canvas canvas)
        {
            System.Windows.Controls.Image found = null;
            VisualTreeHelper.HitTest(
                canvas,
                null,
                result =>
                {
                    if (result.VisualHit is System.Windows.Controls.Image img && img.Tag != null)
                    {
                        found = img;
                        return HitTestResultBehavior.Stop;
                    }
                    return HitTestResultBehavior.Continue;
                },
                new PointHitTestParameters(canvasPos));
            return found;
        }

        private void OnPreviewCanvasMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isGenerating) return;
            if (pdfRenderer == null && !_debugPageActive) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;

            // 按下时记录鼠标固定起点（屏幕坐标，对齐原版 Control.MousePosition）与滚动偏移快照
            // 左键单击已有印章也会再盖一个（支持叠加盖章），故不做命中拦截
            panStartMouse = Mouse.GetPosition(null);
            panStartOffsetX = previewScroll.HorizontalOffset;
            panStartOffsetY = previewScroll.VerticalOffset;
            isDraggingPreview = false;
            ((System.Windows.Controls.Canvas)sender).CaptureMouse();
            e.Handled = true;
        }

        private void OnPreviewCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            // 屏幕坐标，对齐原版 Control.MousePosition，避免控件边界/捕获变化导致跳动
            var pos = Mouse.GetPosition(null);
            double dx = pos.X - panStartMouse.X;
            double dy = pos.Y - panStartMouse.Y;
            if (!isDraggingPreview && (Math.Abs(dx) + Math.Abs(dy)) > 4)
            {
                isDraggingPreview = true;
                // 拖动开始的瞬间，重新记录起点（用当前实际位置，避免 MouseDown 后页面位置变化导致跳动）
                panStartMouse = pos;
                panStartOffsetX = previewScroll.HorizontalOffset;
                panStartOffsetY = previewScroll.VerticalOffset;
                e.Handled = true;
                return;
            }
            if (isDraggingPreview)
            {
                // 固定起点式：始终从"拖动开始时的偏移快照"一次性计算目标（对齐原版 PreviewPage_MouseMove）
                double targetX = Math.Max(0, Math.Min(previewScroll.ScrollableWidth, panStartOffsetX - dx));
                double targetY = Math.Max(0, Math.Min(previewScroll.ScrollableHeight, panStartOffsetY - dy));
                previewScroll.ScrollToHorizontalOffset(targetX);
                previewScroll.ScrollToVerticalOffset(targetY);
            }
            e.Handled = true;
        }

        private void OnPreviewCanvasMouseUp(object sender, MouseButtonEventArgs e)
        {
            var canvas = (System.Windows.Controls.Canvas)sender;
            canvas.ReleaseMouseCapture();
            if (isDraggingPreview)
            {
                isDraggingPreview = false;
                return;
            }
            var upPos = e.GetPosition(canvas);
            // 左键单击 → 立即放置印章（包括单击已有印章可叠加盖章）
            if (comboPageStamp.SelectedIndex == 0 && !specifiedRangeFirstClickPending) return;
            int stampType = specifiedRangeFirstClickPending ? SpecifiedPageStampType : CustomPlacementStampType;
            AddPreviewStampAtPoint(upPos, stampType, canvas);
            e.Handled = true;
        }

        /// <summary>右键删除印章：命中已有印章则删除（批量印章触发原有的三选项弹窗），空白区域不做操作。</summary>
        private void OnPreviewCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isGenerating) return;
            if (pdfRenderer == null && !_debugPageActive) return;
            var canvas = (System.Windows.Controls.Canvas)sender;
            var clickedImage = HitTestStampImage(e.GetPosition(canvas), canvas);
            if (clickedImage != null)
            {
                DeleteStampByImage(clickedImage);
            }
            e.Handled = true;
        }

        private void AddPreviewStampAtPoint(System.Windows.Point pos, int stampType, System.Windows.Controls.Canvas sourceCanvas)
        {
            string stampPath = SelectedStampPath();
            if (!File.Exists(stampPath))
            {
                MessageBox.Show("请先选择印章！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            System.Drawing.Size overlaySize = CalculateCurrentOverlaySize();
            int targetPage = currentPageIndex + 1;
            double dispW = displayWidth;
            double dispH = displayHeight;
            // 双页视图：落在哪个 canvas 就盖到对应页（右页坐标换算为该页内部坐标）
            if (previewViewMode == PreviewViewMode.DoublePage)
            {
                if (sourceCanvas == overlayCanvasRight)
                {
                    targetPage = currentPageIndex + 2;
                    dispW = doublePageRightW;
                    dispH = doublePageRightH;
                }
                else
                {
                    dispW = doublePageLeftW;
                    dispH = doublePageLeftH;
                }
            }
            // 中心比例语义：点击点即印章中心（章可超出页面，超出部分由页面边界自然裁剪）；
            // 与按文字盖章的"章完整在页面内"语义通过 CenterRatio 标记区分
            float px = (float)Math.Max(0.0, Math.Min(1.0, pos.X / dispW));
            float py = (float)Math.Max(0.0, Math.Min(1.0, pos.Y / dispH));

            AddPreviewStamp(px, py, stampPath, stampType, targetPage);
            RefreshPreviewOverlays();
        }

        /// <summary>计算当前印章在预览显示区的尺寸（按当前尺寸参数与印章图片比例）。</summary>
        private System.Drawing.Size CalculateCurrentOverlaySize()
        {
            string stampPath = SelectedStampPath();
            int sizeMm = GetSizeValue();
            try
            {
                using (Bitmap probe = new Bitmap(stampPath))
                {
                    Bitmap current = pageCache != null ? pageCache.GetPage(currentPageIndex) : null;
                    float pdfWidthPoints = current != null ? current.Width * 72f / RenderDpi : (float)displayWidth;
                    return PreviewStampLayout.CalculateOverlaySize(
                        sizeMm, probe.HorizontalResolution, probe.Width, probe.Height,
                        (float)displayWidth, pdfWidthPoints, 80);
                }
            }
            catch
            {
                return new System.Drawing.Size(80, 80);
            }
        }

        private static readonly Random StampRandomGenerator = new Random();

        /// <summary>生成随机位移向量（mm）：方向任意（0~360°）、距离 0~maxMm 均匀随机。
        /// randomOn=false 或 maxMm&lt;=0 时返回零位移。随机值在放置时生成并固定，预览=输出。</summary>
        private void GetRandomOffset(bool randomOn, float maxMm, out float dxMm, out float dyMm)
        {
            dxMm = 0f;
            dyMm = 0f;
            if (!randomOn || maxMm <= 0f) return;
            lock (StampRandomGenerator)
            {
                double angle = StampRandomGenerator.NextDouble() * 2.0 * Math.PI;
                double dist = StampRandomGenerator.NextDouble() * maxMm;
                dxMm = (float)(dist * Math.Cos(angle));
                dyMm = (float)(dist * Math.Sin(angle));
            }
        }

        /// <summary>计算含随机旋转的最终角度：randomOn 时在基础角度上叠加 ±range° 的随机值。</summary>
        private int GetEffectiveRotation(int baseRotation, bool randomOn, int range)
        {
            if (randomOn && range > 0)
            {
                lock (StampRandomGenerator)
                {
                    return baseRotation + StampRandomGenerator.Next(-range, range + 1);
                }
            }
            return baseRotation;
        }

        private void AddPreviewStamp(float px, float py, string stampPath, int stampType, int pageNumber = 0)
        {
            // 取章：当前主章（每个章用各自记忆的参数）
            Tuple<StampPickerItem, AppConfig.StampParams> pickInfo = PickPlacementStamp();
            if (pickInfo == null) return;
            StampPickerItem pick = pickInfo.Item1;
            AppConfig.StampParams sp = pickInfo.Item2;
            if (sp == null) return;
            int sizeMm = sp.Size;
            int currentOpacity = sp.Opacity;
            int currentRotation = sp.Rotation;
            int whiteTolerance = sp.Tolerance;
            bool useWhiteTransparency = sp.RemoveWhite;
            bool useOriginalRotationCrop = sp.RotationHandle == 0;
            bool randomOn = sp.RandomParams;
            int randomRange = sp.RandomRange;

            if (stampType == SpecifiedPageStampType && specifiedRangeFirstClickPending &&
                specifiedPageRange != null && IsAtSpecifiedEndPage())
            {
                float maxOffsetMm = sp.RandomOffsetMm;
                activeSpecifiedBatchId = stampPlacements.CreateBatchId();
                for (int page = specifiedPageRange.StartPage; page <= specifiedPageRange.EndPage; page++)
                {
                    // 每个页面在放置时各自随机一次印章与位移（随机值固定进该页印章）
                    Tuple<StampPickerItem, AppConfig.StampParams> bpInfo = PickPlacementStamp();
                    if (bpInfo == null) break;
                    StampPickerItem batchPick = bpInfo.Item1;
                    AppConfig.StampParams batchSp = bpInfo.Item2;
                    if (batchSp == null) break;
                    GetRandomOffset(batchSp.RandomParams, batchSp.RandomOffsetMm, out float dxMm, out float dyMm);
                    stampPlacements.Add(sourcePath, page, px, py, batchPick.Path, batchSp.Size,
                        batchSp.Opacity, GetEffectiveRotation(batchSp.Rotation, batchSp.RandomParams, batchSp.RandomRange),
                        batchSp.Tolerance, batchSp.RemoveWhite,
                        batchSp.RotationHandle == 0, randomRotation: batchSp.RandomParams && batchSp.RandomRange > 0,
                        batchId: activeSpecifiedBatchId, centerRatio: true,
                        offsetXmm: dxMm, offsetYmm: dyMm,
                        textureEnabled: batchSp.TextureQuality, textureSeed: NewTextureSeed(),
                        textureKb: NewTextureK(), textureKblob: NewTextureK(), textureKgrad: NewTextureK(),
                        textureKwhite: NewTextureK(), textureKspot: NewTextureK(),
                        textureKradial: NewTextureK(), textureKcast: NewTextureK(),
                        textureBrightness: batchSp.TextureBrightness, textureBlob: batchSp.TextureBlob,
                        textureGradient: batchSp.TextureGradient, textureWhite: batchSp.TextureWhite,
                        textureSpot: batchSp.TextureSpot,
                        textureRadial: batchSp.TextureRadial,
                        textureCast: batchSp.TextureCast);
                }
                specifiedRangeFirstClickPending = false;
                activeSpecifiedBatchId = 0;
                comboPageStamp.SelectedIndex = 1;
                // 原版：完成提示只写右侧操作提示区，不写左侧日志区
                SetOperationHint("指定范围页印章已添加，现已回到手动点击盖章；右键印章可删除。需要再添加一批时，请重新点击“指定范围页盖章”。");
                return;
            }

            GetRandomOffset(randomOn, sp.RandomOffsetMm, out float mdxMm, out float mdyMm);
            int targetPage = pageNumber > 0 ? pageNumber : currentPageIndex + 1;
            stampPlacements.Add(sourcePath, targetPage, px, py, pick.Path, sizeMm,
                currentOpacity, GetEffectiveRotation(currentRotation, randomOn, randomRange), whiteTolerance, useWhiteTransparency,
                useOriginalRotationCrop, randomRotation: randomOn && randomRange > 0, batchId: 0, centerRatio: true,
                offsetXmm: mdxMm, offsetYmm: mdyMm,
                textureEnabled: sp.TextureQuality, textureSeed: NewTextureSeed(),
                textureKb: NewTextureK(), textureKblob: NewTextureK(), textureKgrad: NewTextureK(),
                textureKwhite: NewTextureK(), textureKspot: NewTextureK(),
                textureKradial: NewTextureK(), textureKcast: NewTextureK(),
                textureBrightness: sp.TextureBrightness, textureBlob: sp.TextureBlob,
                textureGradient: sp.TextureGradient, textureWhite: sp.TextureWhite,
                textureSpot: sp.TextureSpot,
                textureRadial: sp.TextureRadial,
                textureCast: sp.TextureCast);
        }

        /// <summary>判断当前视图是否正显示指定范围盖章的最后一页（单页=当前页；双页=左页或右页）。</summary>
        private bool IsAtSpecifiedEndPage()
        {
            if (specifiedPageRange == null) return false;
            int end = specifiedPageRange.EndPage;
            if (currentPageIndex + 1 == end) return true;
            return previewViewMode == PreviewViewMode.DoublePage && currentPageIndex + 2 == end;
        }

        // ===================== 按文字盖章 =====================
        private void OnAutoPlaceClick()
        {
            try
            {
                // 调试页（未加载用户文件）视为未加载：提示先加载 PDF，不进入文字搜索（避免误报"图片型 PDF"）
                if (_debugPageActive || pdfRenderer == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    MessageBox.Show("请先加载 PDF 文件再放置印章。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string stampPath = SelectedStampPath();
                if (!File.Exists(stampPath))
                {
                    MessageBox.Show("请先选择印章图片。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string keyword = comboAutoKeyword.Text.Trim();
                if (keyword.Length == 0)
                {
                    MessageBox.Show("请输入要识别的盖章文字。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 目录模式多文件 → 询问是否对所有文件按文字盖章
                if (currentSourceIsDirectory && sourceFiles != null && sourceFiles.Length > 1)
                {
                    MessageBoxResult choice = MessageBox.Show(
                        string.Format("当前目录下共有 {0} 个 PDF 文件。\n\n“是”：对所有文件都按文字放置印章\n“否”：仅对当前文件放置\n“取消”：不放置", sourceFiles.Length),
                        "按文字盖章",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (choice == MessageBoxResult.Cancel)
                    {
                        SetOperationHint("已取消按文字放置印章。");
                        return;
                    }
                    if (choice == MessageBoxResult.Yes)
                    {
                        int okCount = 0;
                        int totalAdded = 0;
                        var skipped = new List<string>();
                        foreach (string f in sourceFiles)
                        {
                            AutoPlaceFileResult r = ApplyAutoPlaceToFile(f, keyword, false);
                            if (r.Cancelled)
                            {
                                AppendLog(string.Format("按文字盖章（全部文件）已中止：已处理 {0} 个文件，共新增 {1} 处。", okCount, totalAdded));
                                SetOperationHint("按文字盖章已中止，详见下方日志");
                                RefreshPreviewOverlays();
                                return;
                            }
                            if (r.Success)
                            {
                                okCount++;
                                totalAdded += r.Added;
                            }
                            else
                            {
                                skipped.Add(r.SkipReason + "：" + Path.GetFileName(f));
                            }
                        }
                        if (skipped.Count > 0)
                        {
                            AppendLog(string.Format(
                                "按文字盖章（全部文件）完成：{0} 个文件已放置，共新增 {1} 处；{2} 个文件未处理：{3}",
                                okCount, totalAdded, skipped.Count, string.Join("；", skipped)));
                        }
                        else
                        {
                            AppendLog(string.Format("按文字盖章（全部文件）完成：{0} 个文件已放置，共新增 {1} 处。", okCount, totalAdded));
                        }
                        SetOperationHint(string.Format("按文字盖章完成：{0} 个文件已放置，详见下方日志", okCount));
                        RefreshPreviewOverlays();
                        return;
                    }
                }

                ApplyAutoPlaceToFile(sourcePath, keyword, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("放置印章失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>对单个 PDF 执行按文字盖章（搜索 → 上下文过滤 → 放置）。失败是否弹窗由 showFailureDialog 控制
        /// （单文件弹窗提示；多文件汇总不弹窗，结果由调用方汇总到日志）。</summary>
        private AutoPlaceFileResult ApplyAutoPlaceToFile(string path, string keyword, bool showFailureDialog)
        {
            List<PdfTextMatch> allMatches;
            using (PdfTextSearcher searcher = new PdfTextSearcher(path))
            {
                if (!searcher.HasAnyText())
                {
                    if (showFailureDialog)
                    {
                        MessageBox.Show("图片型 PDF 无法搜索到文字，无法放置印章", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return new AutoPlaceFileResult { Success = false, SkipReason = "图片型 PDF 无文字层" };
                }
                allMatches = searcher.FindAll(keyword);
            }

            if (allMatches == null || allMatches.Count == 0)
            {
                if (showFailureDialog)
                {
                    MessageBox.Show("没找到您指定的盖章文字", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return new AutoPlaceFileResult { Success = false, SkipReason = "未找到指定文字" };
            }

            // 解析上下文过滤参数（未勾选则不过滤）
            bool useContextFilter = chkContextFilter.IsChecked == true;
            string[] contextKeywords = useContextFilter ? ParseContextKeywords(txtContextKeywords.Text) : new string[0];
            int contextRange = ParseContextRange(txtContextRange.Text);
            bool requireAll = comboContextMatch.SelectedIndex == 1;
            bool excludeSpaces = chkContextExcludeSpaces.IsChecked == true;

            List<PdfTextMatch> matches;
            if (contextKeywords.Length == 0)
            {
                // 关键词留空 → 不过滤，全部盖
                matches = allMatches;
            }
            else
            {
                // 有关键词 → 二次搜索并过滤
                using (PdfTextSearcher searcher = new PdfTextSearcher(path))
                {
                    matches = searcher.FindAll(keyword, contextKeywords, contextRange, requireAll, excludeSpaces);
                }
                if (matches == null || matches.Count == 0)
                {
                    if (showFailureDialog)
                    {
                        string kwDisplay = string.Join("，", contextKeywords);
                        string modeDisplay = requireAll ? "全部关键词（且）" : "任一关键词（或）";
                        MessageBox.Show(
                            string.Format("找到了 {0} 处“{1}”，但附近都没有指定关键词，未盖章。\n\n关键词（共 {2} 个）：{3}\n匹配模式：{4}",
                                allMatches.Count, keyword, contextKeywords.Length, kwDisplay, modeDisplay),
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return new AutoPlaceFileResult { Success = false, SkipReason = "附近无指定关键词" };
                }
            }

            // 相同文字已放置过 → 确认后撤销旧批、按最新参数重盖（每个文件独立判断）
            int batchId = 0;
            bool appendToExistingBatch = false;
            AutoStampOperation existingOp = autoStampOperations.LastOrDefault(
                o => string.Equals(o.Keyword, keyword, StringComparison.Ordinal)
                    && string.Equals(o.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existingOp != null)
            {
                MessageBoxResult confirm = MessageBox.Show(
                    string.Format("已用“{0}”放置过印章。\n\n点击“是”：删除上次的印章，重新放置\n点击“否”：保留上次的印章，只盖新增的位置\n点击“取消”：不放置", keyword),
                    "重复放置确认",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Cancel)
                {
                    SetOperationHint("已取消放置，保留原有印章。");
                    return new AutoPlaceFileResult { Success = false, Cancelled = true };
                }
                if (confirm == MessageBoxResult.Yes)
                {
                    stampPlacements.RemoveBatch(path, existingOp.BatchId);
                    autoStampOperations.Remove(existingOp);
                    batchId = stampPlacements.CreateBatchId();
                }
                else
                {
                    // 选"否"：归到旧批次，只盖上次没盖过的新位置
                    batchId = existingOp.BatchId;
                    appendToExistingBatch = true;
                }
            }
            else
            {
                batchId = stampPlacements.CreateBatchId();
            }

            System.Drawing.Size overlaySize = CalculateCurrentOverlaySize();
            double previewW = Math.Max(1, displayWidth);
            double previewH = Math.Max(1, displayHeight);
            float ratioW = (float)(overlaySize.Width / previewW);
            float ratioH = (float)(overlaySize.Height / previewH);

            // 中心偏移（随搜索文字记忆，mm）：作为该次盖章的基准偏移，与随机位移叠加
            bool coEnabled;
            float coBaseX, coBaseY;
            AppConfig.GetCenterOffsetForKeyword(keyword, out coEnabled, out coBaseX, out coBaseY);
            if (!coEnabled)
            {
                coBaseX = 0f;
                coBaseY = 0f;
            }

            int addedCount = 0;
            var addedPages = new List<int>();
            foreach (PdfTextMatch match in matches)
            {
                // 取章：当前主章（每个章用各自记忆的参数）
                Tuple<StampPickerItem, AppConfig.StampParams> pickInfo = PickPlacementStamp();
                if (pickInfo == null) break;
                StampPickerItem pick = pickInfo.Item1;
                AppConfig.StampParams sp = pickInfo.Item2;
                if (sp == null) break;

                AutoStampPositionResult pos = AutoStampPositionCalculator.Calculate(
                    match.CenterX, match.CenterY, match.PageWidth, match.PageHeight, ratioW, ratioH);

                // 选"否"追加模式：旧批次同页已有坐标接近的印章则跳过，不重复叠加
                if (appendToExistingBatch)
                {
                    bool alreadyPlaced = stampPlacements.ForPage(path, match.PageIndex + 1)
                        .Any(p => p.BatchId == batchId
                            && Math.Abs(p.X - pos.Px) < 0.03
                            && Math.Abs(p.Y - pos.Py) < 0.03);
                    if (alreadyPlaced) continue;
                }

                // 每个匹配在放置时各自随机一次印章与位移（随机值固定进该印章），叠加到中心偏移之上
                GetRandomOffset(sp.RandomParams, sp.RandomOffsetMm, out float dxMm, out float dyMm);
                stampPlacements.Add(path, match.PageIndex + 1, pos.Px, pos.Py, pick.Path, sp.Size,
                        sp.Opacity, GetEffectiveRotation(sp.Rotation, sp.RandomParams, sp.RandomRange),
                        sp.Tolerance, sp.RemoveWhite,
                        sp.RotationHandle == 0, randomRotation: sp.RandomParams && sp.RandomRange > 0,
                        batchId: batchId,
                        offsetXmm: coBaseX + dxMm, offsetYmm: coBaseY + dyMm,
                        textureEnabled: sp.TextureQuality, textureSeed: NewTextureSeed(),
                        textureKb: NewTextureK(), textureKblob: NewTextureK(), textureKgrad: NewTextureK(),
                        textureKwhite: NewTextureK(), textureKspot: NewTextureK(),
                        textureKradial: NewTextureK(), textureKcast: NewTextureK(),
                        textureBrightness: sp.TextureBrightness, textureBlob: sp.TextureBlob,
                        textureGradient: sp.TextureGradient, textureWhite: sp.TextureWhite,
                textureSpot: sp.TextureSpot,
                textureRadial: sp.TextureRadial,
                textureCast: sp.TextureCast);
                addedCount++;
                addedPages.Add(match.PageIndex + 1);
            }

            if (appendToExistingBatch && addedCount == 0)
            {
                AppendLog(string.Format("按文字盖章完成：关键词“{0}”，本次搜索到 {1} 处，位置上次都已盖过，无新增印章。", keyword, matches.Count));
                SetOperationHint("按文字盖章完成：无新增，详见下方日志");
                RefreshPreviewOverlays();
                return new AutoPlaceFileResult { Success = true, Added = 0 };
            }

            if (!appendToExistingBatch)
            {
                autoStampOperations.Add(new AutoStampOperation { Keyword = keyword, BatchId = batchId, FilePath = path });
            }
            btnUndoAuto.IsEnabled = true;

            // 成功找到并完成盖章的文字才记入历史
            AppConfig.RecordAutoStampKeyword(keyword);
            // 同步该词的中心偏移记忆（界面当前勾选与弹窗横纵值）
            AppConfig.SetCenterOffsetForKeyword(keyword, chkCenterOffset.IsChecked == true, _centerOffsetX, _centerOffsetY);

            RefreshPreviewOverlays();

            // 页码列表（去重排序，全部列出）
            string pageDisplay = "第" + string.Join("、", addedPages.Distinct().OrderBy(p => p)) + "页";

            if (appendToExistingBatch)
            {
                // 追加模式：详细结果写左侧，右侧简短提示
                int skippedCount = matches.Count - addedCount;
                AppendLog(string.Format(
                    "按文字盖章完成：关键词“{0}”，本次搜索到 {1} 处，新增盖了 {2} 个，已在 {3} 盖章，其余 {4} 处上次已盖过。右键单个章可删除，或点击“撤销放置”逐步撤销。",
                    keyword, matches.Count, addedCount, pageDisplay, skippedCount));
                SetOperationHint(string.Format("按文字盖章完成：新增 {0} 个，详见下方日志", addedCount));
            }
            else if (contextKeywords.Length > 0 && matches.Count < allMatches.Count)
            {
                string kwDisplay = string.Join("，", contextKeywords);
                string modeDisplay = requireAll ? "全部关键词（且）" : "任一关键词（或）";
                AppendLog(string.Format(
                    "按文字盖章完成：关键词“{0}”，共找到 {1} 处，其中 {2} 处附近有关键词，已在 {3} 盖章。关键词（共 {4} 个）：{5} | 匹配模式：{6}。上下文示例：{7}。右键单个章可删除，或点击“撤销放置”逐步撤销。",
                    keyword, allMatches.Count, matches.Count, pageDisplay, contextKeywords.Length, kwDisplay, modeDisplay,
                    BuildContextSample(matches[0].Context)));
                SetOperationHint(string.Format("按文字盖章完成：{0} 处，详见下方日志", matches.Count));
            }
            else
            {
                string ctxInfo = useContextFilter && matches.Count > 0 && !string.IsNullOrEmpty(matches[0].Context)
                    ? string.Format("上下文示例：{0}。", BuildContextSample(matches[0].Context))
                    : "";
                AppendLog(string.Format(
                    "按文字盖章完成：关键词“{0}”，共找到 {1} 处，已在 {2} 盖章。{3}右键单个章可删除，或点击“撤销放置”逐步撤销。",
                    keyword, matches.Count, pageDisplay, ctxInfo));
                SetOperationHint(string.Format("按文字盖章完成：{0} 处，详见下方日志", matches.Count));
            }
            return new AutoPlaceFileResult { Success = true, Added = addedCount };
        }

        // 撤销放置：每次撤销最近一次"按文字放置"
        private void OnUndoAutoClick()
        {
            if (autoStampOperations.Count == 0)
            {
                return;
            }

            // 只撤销当前预览文件的最近一次"按文字放置"（多文件时各文件批次互不影响）
            int idx = -1;
            for (int i = autoStampOperations.Count - 1; i >= 0; i--)
            {
                if (string.Equals(autoStampOperations[i].FilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
            {
                return;
            }
            AutoStampOperation op = autoStampOperations[idx];
            autoStampOperations.RemoveAt(idx);

            int removed = stampPlacements.RemoveBatch(sourcePath, op.BatchId);
            btnUndoAuto.IsEnabled = autoStampOperations.Any(o =>
                string.Equals(o.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase));

            RefreshPreviewOverlays();
            if (removed > 0)
            {
                SetOperationHint(string.Format("已撤销放置：“{0}”的印章已移除（{1} 个）。", op.Keyword, removed));
            }
            else
            {
                SetOperationHint(string.Format("已撤销放置：“{0}”（原印章已不存在）。", op.Keyword));
            }
        }

        // 使用说明按钮：打开使用说明窗口
        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            HelpWindow help = new HelpWindow();
            help.Owner = this;
            help.ShowDialog();
        }

        /// <summary>把匹配上下文原文转为可读诊断文本：空格→·、换行→⏎、制表→⇥，超长截断（用于排查关键词匹配/忽略空白逻辑）。</summary>
        private static string BuildContextSample(string ctx)
        {
            if (string.IsNullOrEmpty(ctx)) return "（无）";
            string s = ctx.Replace(" ", "·").Replace("\t", "⇥").Replace("\r", "").Replace("\n", "⏎");
            return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
        }

        /// <summary>解析“附近关键词”输入：按逗号/分号/空格/顿号分隔，去空白去重，返回非空词数组。</summary>
        private static string[] ParseContextKeywords(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new string[0];
            return input
                .Split(new[] { ',', '，', ';', '；', ' ', '\t', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>解析“上下文范围”输入：合法正整数，默认 50，限制在 1~500 之间。</summary>
        private static int ParseContextRange(string input)
        {
            if (int.TryParse(input?.Trim(), out int v) && v >= 1)
            {
                return Math.Min(v, 500);
            }
            return 50;
        }

        // 指定范围页盖章
        private void OnSpecifiedPageClick()
        {
            // 调试页（未加载用户文件）视为未加载
            if (_debugPageActive || pdfRenderer == null || pageCount < 1)
            {
                MessageBox.Show("请先加载 PDF 文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!File.Exists(SelectedStampPath()))
            {
                MessageBox.Show("请先选择印章！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new PageRangeDialog(pageCount)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
            {
                return;
            }

            specifiedPageRange = dlg.Result;
            specifiedRangeFirstClickPending = true;
            // 对齐原版：跳转到指定范围最后一页，等待单击设置位置（双页视图跳到包含最后一页的跨页）
            currentPageIndex = previewViewMode == PreviewViewMode.DoublePage
                ? ((specifiedPageRange.EndPage - 1) / 2) * 2
                : specifiedPageRange.EndPage - 1;
            UpdatePageInfo();
            RelayoutPreview();
            // 原版：等待单击的提示由 UpdatePlacementOperationHint 写入右侧操作提示区，不写左侧日志区
            UpdatePlacementOperationHint();
        }

        // ===================== 生成文件 =====================
        private async Task OnGenerateClickAsync()
        {
            if (isGenerating)
            {
                return;
            }

            // 调试页（未加载用户文件）不能输出，提示先加载 PDF
            if (_debugPageActive || pdfRenderer == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show("请先加载 PDF 文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 盖章时自动收起印章参数和其他设置，给预览区留更多空间
            if (sealParamsContent.Visibility == Visibility.Visible)
                ToggleFold(sealParamsContent, foldSealParamsArrow, foldSealParamsText, sealParamsHeaderGrid, btnFoldSealParams);
            if (otherContent.Visibility == Visibility.Visible)
                ToggleFold(otherContent, foldOtherArrow, foldOtherText, otherHeaderGrid, btnFoldOther);

            // 汇总本次是否实际会产生盖章效果（骑缝章 / 数字签名 / 预览中已放置的章）
            int qfzType = SeamBusinessFromDisplay(comboSeam.SelectedIndex);
            int qmType = comboSignature.SelectedIndex;
            bool wantsSeam = qfzType != 1;
            bool wantsSignature = qmType != 0;
            bool hasPreviewStamps = stampPlacements.Count > 0;

            if (!wantsSeam && !wantsSignature && !hasPreviewStamps)
            {
                MessageBoxResult choice = MessageBox.Show(
                    "您没有进行任何盖章操作，确定要生成文件吗？",
                    "确认生成",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (choice != MessageBoxResult.Yes)
                {
                    return;
                }
            }
            else if (!File.Exists(SelectedStampPath()))
            {
                MessageBox.Show("请先选择印章！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string src = txtSourcePdf.Text.Trim();
            string outDir = txtOutputDir.Text.Trim();
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(outDir))
            {
                MessageBox.Show("文件路径不能为空，请先选择路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 参数校验
            if (!TryParseInt(txtStampSize.Text, 1, 500, out int size))
            {
                MessageBox.Show("印章尺寸设置错误，请输入正确的尺寸（1-500）。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseInt(txtRotation.Text, -360, 360, out int rotation))
            {
                MessageBox.Show("印章角度设置错误，请输入正确的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseInt(txtOpacity.Text, 0, 100, out int opacity))
            {
                MessageBox.Show("不透明度设置错误，请输入 0-100 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseInt(txtSeamPosPct.Text, 0, 100, out int wz))
            {
                MessageBox.Show("骑缝章位置设置错误，请输入 100 以内的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(txtMaxSplit.Text.Trim(), out int maxfgs) || maxfgs < 1)
            {
                MessageBox.Show("最大分割数设置错误，请输入正确的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppConfig.Size = size;
            AppConfig.Rotation = rotation;
            AppConfig.Opacity = opacity;
            AppConfig.WzPercent = wz;
            AppConfig.MaxFgs = maxfgs;
            AppConfig.QfzType = qfzType;
            AppConfig.QmType = qmType;
            AppConfig.WzType = comboSeamPosition.SelectedIndex;
            AppConfig.QbFlag = comboRotationHandle.SelectedIndex;
            AppConfig.YzType = comboPageStamp.SelectedIndex == 0 ? 0 : 1;
            AppConfig.SignText = txtSignature.Text.Trim();
            AppConfig.Password = txtSignaturePass.Password;
            AppConfig.LastStampImagePath = SelectedStampPath();
            AppConfig.ContextFilterEnabled = chkContextFilter.IsChecked == true;
            AppConfig.ContextKeywords = txtContextKeywords.Text?.Trim() ?? "";
            AppConfig.ContextRange = ParseContextRange(txtContextRange.Text);
            AppConfig.ContextMatch = comboContextMatch.SelectedIndex == 1 ? 1 : 0;
            AppConfig.ContextExcludeSpaces = chkContextExcludeSpaces.IsChecked == true;

            bool needStampImage = wantsSeam || wantsSignature || hasPreviewStamps;
            if (needStampImage && !File.Exists(SelectedStampPath()))
            {
                MessageBox.Show("印章文件读取失败，请重新导入印章。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RunStampBatchAsync(src, outDir, qfzType, qmType, size, rotation, opacity, wz, maxfgs,
                wantsSeam, wantsSignature, hasPreviewStamps, needStampImage);
        }

        private async Task RunStampBatchAsync(string src, string outDir, int qfzType, int qmType,
            int size, int rotation, int opacity, int wz, int maxfgs,
            bool wantsSeam, bool wantsSignature, bool hasPreviewStamps, bool needStampImage)
        {
            isGenerating = true;
            btnGenerate.IsEnabled = false;
            try
            {
                var options = new StampOptions
                {
                    StampImagePath = SelectedStampPath(),
                    OutputPath = outDir,
                    QfzType = qfzType,
                    QmType = qmType,
                    SignText = txtSignature.Text.Trim(),
                    SignPassword = txtSignaturePass.Password,
                    RemoveWhite = chkRemoveWhite.IsChecked == true,
                    Opacity = opacity,
                    Rotation = rotation,
                    OriginalRotationCrop = comboRotationHandle.SelectedIndex == 0,
                    SizeMm = size,
                    PdfPassword = txtPdfPass.Password,
                    WzType = comboSeamPosition.SelectedIndex,
                    WzPercent = wz,
                    MaxSplit = maxfgs,
                    Placements = stampPlacements,
                    CertDefaultPath = AppConfig.CertDefaultPath,
                    FixType = AppConfig.FixType,
                    FixStr = AppConfig.FixStr,
                    FixStr2 = AppConfig.FixStr2
                };

                // 在 UI 线程准备印章与证书
                if (!StampEngine.PrepareStampResources(options, out Bitmap seamImage, out float xzbl,
                        out X509Certificate2 cert, msg => AppendLog(msg)))
                {
                    AppendLog("准备失败，未开始盖章，请检查上面的提示。", true);
                    return;
                }

                using (seamImage)
                using (cert)
                {
                    int djType = comboOutput.SelectedIndex == 0 ? 1 : 0; // 合并=1 / 叠加=0（UI 线程缓存）
                    _outputQualityDpi = GetOutputQualityDpi(); // 输出清晰度同法缓存，避免工作线程访问控件
                    bool dirMode = currentSourceIsDirectory;
                    await Task.Run(() =>
                        RunStampCore(options, seamImage, xzbl, src, outDir, djType, dirMode));

                    // 全部处理完成：停止保存动态提示，恢复默认操作说明
                    StopSavingIndicator();
                }

                if (needStampImage)
                {
                    AppConfig.SaveUiConfig();
                    AppConfig.SaveSuccessfulStampConfig();
                }
            }
            catch (Exception ex)
            {
                AppendLog("盖章过程中发生错误：" + ex.Message, true);
            }
            finally
            {
                isGenerating = false;
                btnGenerate.IsEnabled = true;
            }
        }

        /// <summary>后台批量盖章（目录模式遍历所有 PDF；文件模式逐个处理）。</summary>
        /// <summary>合并模式输出 DPI（UI 线程缓存，工作线程只读字段，避免跨线程访问控件）。</summary>
        private int _outputQualityDpi = 150;

        /// <summary>输出清晰度下拉 -> 栅格化 DPI（0极高300/1高200/2标准150/3低96/4极低72）。</summary>
        private int GetOutputQualityDpi()
        {
            switch (comboOutputQuality.SelectedIndex)
            {
                case 0: return 300;
                case 1: return 200;
                case 3: return 96;
                case 4: return 72;
                default: return 150;
            }
        }

        /// <summary>叠加模式（index=1）下输出清晰度不生效，置灰不可选。</summary>
        private void UpdateOutputQualityEnabled()
        {
            if (comboOutputQuality == null) return;
            comboOutputQuality.IsEnabled = comboOutput.SelectedIndex == 0;
        }

        // ==================== 输出文件名格式自定义（V132） ====================

        /// <summary>从 AppConfig 静态字段构建命名选项（盖章输出时使用）。
        /// 注意：RunStampCore 在后台线程执行，禁止在此读取 UI 控件（跨线程异常，V134 修复）；
        /// AppConfig 字段由 OutputName_Changed（UI 线程）实时同步，后台线程读静态字段安全。</summary>
        private OutputNamingOptions BuildOutputNamingOptions()
        {
            return new OutputNamingOptions
            {
                Mark = AppConfig.OutputNameMark ?? "",
                BeforeName = AppConfig.OutputNamePos == 1,
                SeqType = AppConfig.OutputNameSeqType,
                Pad = AppConfig.OutputNamePad,
                UseTimestamp = AppConfig.OutputNameTs,
                TsFormat = AppConfig.OutputNameTsFormat
            };
        }

        /// <summary>时间戳格式下拉索引 → 格式串。</summary>
        private static string TsFormatFromIndex(int index)
        {
            switch (index)
            {
                case 1: return "yyyy-MM-dd";
                case 2: return "yyyyMMdd-HHmm";
                case 3: return "yyyy-MM-dd-HH-mm";
                default: return "yyyyMMdd";
            }
        }

        /// <summary>格式串 → 时间戳格式下拉索引（未知格式回 0）。</summary>
        private static int TsFormatToIndex(string format)
        {
            switch (format)
            {
                case "yyyy-MM-dd": return 1;
                case "yyyyMMdd-HHmm": return 2;
                case "yyyy-MM-dd-HH-mm": return 3;
                default: return 0;
            }
        }

        /// <summary>时间戳勾选后格式下拉可选。</summary>
        private void UpdateOutputNameTsEnabled()
        {
            if (comboOutputNameTsFormat == null || chkOutputNameTs == null) return;
            comboOutputNameTsFormat.IsEnabled = chkOutputNameTs.IsChecked == true;
        }

        /// <summary>实时刷新输出文件名效果预览（用当前第一个源文件名，无则用示例名）。</summary>
        private void UpdateOutputNamePreview()
        {
            if (txtOutputNamePreview == null) return;
            try
            {
                string sample = GetFirstSourceDisplayName();
                string first = OutputFileNamingPolicy.GetNextFileName(sample, BuildOutputNamingOptions(), Enumerable.Empty<string>());
                // 第二次编号：把第一次结果视为已存在，模拟递增
                string second = OutputFileNamingPolicy.GetNextFileName(sample, BuildOutputNamingOptions(),
                    new[] { Path.GetFileName(first) });
                txtOutputNamePreview.Text = "▸ 效果预览：" + first + "　再盖一次 → " + second;
            }
            catch (Exception ex)
            {
                txtOutputNamePreview.Text = "▸ 效果预览：（生成失败：" + ex.Message + "）";
            }
        }

        /// <summary>效果预览的源文件名占位：固定为"《文件名》"，不随实际导入文件名变动（V133 起）。</summary>
        private string GetFirstSourceDisplayName()
        {
            return "《文件名》";
        }

        /// <summary>输出命名控件变化：刷新预览 + 保存配置（启动赋值阶段 IsLoaded=false 不保存）。</summary>
        private void OutputName_Changed()
        {
            if (!IsLoaded) return;
            UpdateOutputNameTsEnabled();
            UpdateOutputNamePreview();
            try
            {
                AppConfig.OutputNameMark = txtOutputNameMark.Text.Trim();
                AppConfig.OutputNamePos = comboOutputNamePos.SelectedIndex == 1 ? 1 : 0;
                AppConfig.OutputNameSeqType = Math.Max(0, Math.Min(2, comboOutputNameSeqType.SelectedIndex));
                AppConfig.OutputNamePad = Math.Max(1, Math.Min(3, comboOutputNamePad.SelectedIndex + 1));
                AppConfig.OutputNameTs = chkOutputNameTs.IsChecked == true;
                AppConfig.OutputNameTsFormat = TsFormatFromIndex(comboOutputNameTsFormat.SelectedIndex);
                AppConfig.SaveUiConfig();
            }
            catch (Exception ex)
            {
                AppendLog("保存输出命名配置失败：" + ex.Message, true);
            }
        }

        private bool RunStampCore(StampOptions options, Bitmap seamImage, float xzbl,
            string src, string outDir, int djType, bool dirMode)
        {
            bool hasFailures = false;
            try
            {
                if (dirMode)
                {
                    DirectoryInfo dir = new DirectoryInfo(src);
                    var targets = dir.GetFiles("*.pdf", SearchOption.AllDirectories)
                        .Where(f => !string.Equals(f.DirectoryName, outDir, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    int total = targets.Count;
                    int done = 0;
                    foreach (var fileInfo in targets)
                    {
                        done++;
                        string source = fileInfo.FullName;
                        string output = OutputFileNamingPolicy.GetNextOutputPath(outDir, source, BuildOutputNamingOptions());
                        AppendLog(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, fileInfo.Name));
                        bool success = StampEngine.PDFWatermark(options, seamImage, xzbl,
                            source, output, source,
                            msg => AppendLog(msg),
                            (d, t) => SetOperationHint(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, fileInfo.Name)),
                            active => { if (active) StartSavingIndicator(); });
                        if (success && djType == 1)
                        {
                            StampEngine.PDFToiPDF(output, options.QmType, options.Cert,
                                _outputQualityDpi);
                        }
                        if (success)
                        {
                            AppendLog(OutputFileNamingPolicy.BuildSuccessMessage(fileInfo.Name, output));
                        }
                        else
                        {
                            hasFailures = true;
                            AppendLog("失败！“" + fileInfo.Name + "”盖章失败！", true);
                        }
                    }
                }
                else
                {
                    string[] fileArray = src.Split(',');
                    int total = fileArray.Length;
                    int done = 0;
                    foreach (string file in fileArray)
                    {
                        done++;
                        string filename = Path.GetFileName(file);
                        string output = OutputFileNamingPolicy.GetNextOutputPath(outDir, file, BuildOutputNamingOptions());
                        string actualOutput = output;
                        AppendLog(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, filename));
                        bool success = StampEngine.PDFWatermark(options, seamImage, xzbl,
                            file, output, file,
                            msg => AppendLog(msg),
                            (d, t) => SetOperationHint(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, filename)),
                            active => { if (active) StartSavingIndicator(); });
                        if (success)
                        {
                            if (djType == 1)
                            {
                                StampEngine.PDFToiPDF(output, options.QmType, options.Cert,
                                    _outputQualityDpi);
                            }
                            if (!string.IsNullOrEmpty(options.PdfPassword))
                            {
                                string jmoutput = OutputFileNamingPolicy.AddSuffix(output, AppConfig.FixStr2);
                                StampEngine.EncryptPDF(output, jmoutput, options.PdfPassword);
                                if (File.Exists(jmoutput))
                                {
                                    actualOutput = jmoutput;
                                }
                            }
                            AppendLog(OutputFileNamingPolicy.BuildSuccessMessage(filename, actualOutput));
                        }
                        else
                        {
                            hasFailures = true;
                            AppendLog("失败！“" + filename + "”盖章失败！", true);
                        }
                    }
                }
                return hasFailures;
            }
            catch (Exception ex)
            {
                AppendLog("处理过程中发生错误：" + ex.Message, true);
                return true;
            }
        }

        // ===================== 取值助手 =====================
        private int GetSizeValue()
        {
            return TryParseInt(txtStampSize.Text, 1, 500, out int v) ? v : 40;
        }

        private int GetOpacityValue()
        {
            return TryParseInt(txtOpacity.Text, 0, 100, out int v) ? v : 100;
        }

        private int GetRotationValue()
        {
            return TryParseInt(txtRotation.Text, -360, 360, out int v) ? v : 0;
        }

        private int GetToleranceValue()
        {
            return TryParseInt(txtTolerance.Text, 0, 50, out int v) ? v : 20;
        }

        private static bool TryParseInt(string text, int min, int max, out int value)
        {
            value = 0;
            return int.TryParse(text.Trim(), out value) && value >= min && value <= max;
        }

        // ===================== 提示区 =====================

        /// <summary>日志区初始帮助文字（软件打开时显示，用户首次操作后清空）。</summary>
        private const string InitialHelpText =
            "提示：\n" +
            "1、建议使用472像素以上且背景透明的印章图片。\n" +
            "2、手动盖章：选择\"点击盖章\"后，单击页面添加印章；右键已有印章可删除。\n" +
            "3、指定范围页盖章：点击按钮后选择页码范围，在相同位置批量盖章。\n" +
            "4、按文字盖章：输入文字后自动定位并盖章；可勾选\"附近关键词\"精准过滤。\n" +
            "5、骑缝章：可根据需要选择是否加盖，支持上/下/左/右四个位置。\n" +
            "6、预览操作：单页视图下滚轮翻页，Ctrl+滚轮缩放；放大视图下按住左键拖动页面。\n" +
            "7、确认预览无误后，点击\"盖章并生成文件\"输出最终文件。";

        private void AppendLog(string line, bool isError = false)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(() => AppendLog(line, isError)));
                return;
            }
            if (logContainsOnlyHelp)
            {
                logText.Inlines.Clear();
                logContainsOnlyHelp = false;
            }
            // 每条日志前缀时间戳 [HH:mm:ss]，日志之间用单换行+较小行高实现半行间距；错误红色，其余黑色
            string timestampedLine = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line;
            var run = new System.Windows.Documents.Run(timestampedLine);
            if (isError)
            {
                run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB2, 0x22, 0x22));
            }
            if (logText.Inlines.Count == 0)
            {
                logText.Inlines.Add(run);
            }
            else
            {
                logText.Inlines.Add(new System.Windows.Documents.Run("\n"));
                logText.Inlines.Add(run);
            }
            // 追加文字后自动滚动到底部，确保最新内容可见。
            // 注意：文本追加后立即 ScrollToEnd 时，ScrollViewer 内部可能尚未完成内容测量，
            // 且自动换行/滚动条出现会改变视口导致 ExtentHeight 延迟变化，需多级延迟补滚；
            // ScrollChanged 兜底逻辑见 LogScroll_ScrollChanged。
            if (logScroll != null)
            {
                if (_logPinToBottom)
                {
                    logScroll.UpdateLayout();
                    logScroll.ScrollToEnd();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (logScroll != null) logScroll.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (logScroll != null) logScroll.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        /// <summary>日志滚动兜底：换行重排/滚动条出现使 ExtentHeight 变大且当前钉底时，自动补滚到底；
        /// 用户手动向上滚动时取消钉底，追加日志不强拉回底部。
        /// 容差 20px：重排发生时滚动位置尚未跟上属正常，按“在底部附近”处理并补滚。</summary>
        private void LogScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (logScroll == null) return;
            bool atBottom = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight) <= 20;
            _logPinToBottom = atBottom;
            if (_logPinToBottom && e.ExtentHeightChange > 0)
            {
                logScroll.ScrollToEnd();
            }
        }

        /// <summary>操作提示区：显示动态操作说明（对齐原版 PreviewOperationHintPolicy.Build）。</summary>
        private void UpdatePlacementOperationHint()
        {
            bool hasPreview = pdfRenderer != null;
            bool directoryMode = currentSourceIsDirectory;
            bool directorySelected = directoryMode && Directory.Exists(txtSourcePdf.Text);
            bool specifiedPending = specifiedRangeFirstClickPending && specifiedPageRange != null;
            bool pageStampEnabled = comboPageStamp.SelectedIndex == 1;
            SetOperationHint(PreviewOperationHintPolicy.Build(
                hasPreview, directoryMode, directorySelected, specifiedPending, pageStampEnabled, previewViewMode));
        }

        // ===== 工具栏响应式布局（V154：三块独立压缩。每个功能区一个块，块宽由内部形态决定；文字优先逐级压缩，判定纯算术）=====
        // 块1 翻页：文字(268) / 图标(176)；块2 缩放：文字(240) / 图标(148)；块3 视图：四字(252) / 两字(174) / 一字(132)。
        // 块间：两条分隔线（V152 恢复），每处 = 线两侧各 8px 间距 + 1px 线 = 17px，两处共 34px。
        // （V153：总页数控件固定宽 60→48；V154：txtPageTotal/txtPercent 右对齐，使分隔线在组间视觉居中；
        //   视图两字档 50→54 防第二个字截断，块3 两字 162→174）
        // 档位（文字优先，逐级压缩）：S0≥794 三块全文字+视图四字 | S1≥702 翻页→图标 | S2≥610 翻页+缩放→图标（视图四字）
        //   | S3≥532 视图→两字 | S4≥490 视图→一字 | <490 保持最小，右侧裁剪兜底（与左侧设置区一致，不换行）。
        // 全部元素固定宽（按钮 Width 76/30、视图 80/54/40），判定纯算术，零测量零布局判定——根治 V146-V150 的测量/振荡问题。
        private bool toolbarSpacingBusy;      // 防重入：判定过程中忽略后续 SizeChanged
        private bool tbB1Icon;                // 块1（翻页）按钮是否图标态
        private bool tbB2Icon;                // 块2（缩放）按钮是否图标态
        private int tbViewLevel;              // 块3（视图）文字档：0=四字 1=两字 2=一字
        private const double ToolbarHysteresis = 12; // 降档滞回：切更紧凑档需跨过阈值-该值，防临界抖动
        private static readonly double[] LevelThreshold = { 794, 702, 610, 532, 490 }; // 各档进入阈值（含块间距 34）

        /// <summary>工具栏尺寸变化入口：只响应宽度变化，同步重算形态。</summary>
        private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || toolbarSpacingBusy) return;
            toolbarSpacingBusy = true;
            try { UpdateToolbarSpacing(); }
            finally { toolbarSpacingBusy = false; }
        }

        /// <summary>按档位切换三个块的整体形态。块1/块2：文字↔图标（Content 不变，只切内部元素 Visibility，Width 固定 76/30）；
        /// 块3：四字/两字/一字（Content 与 Width 同步切换，Width 固定使 DesiredSize 与内容无关，无测量缓存问题）。</summary>
        private void SetToolbarStates(bool b1Icon, bool b2Icon, int viewLevel)
        {
            if (tbB1Icon == b1Icon && tbB2Icon == b2Icon && tbViewLevel == viewLevel) return;
            tbB1Icon = b1Icon; tbB2Icon = b2Icon; tbViewLevel = viewLevel;
            double w1 = b1Icon ? 30 : 76;
            SetDualButton(btnPrev, txtPrevText, pathPrev, !b1Icon, w1);
            SetDualButton(btnNext, txtNextText, pathNext, !b1Icon, w1);
            double w2 = b2Icon ? 30 : 76;
            SetDualButton(btnZoomOut, txtZoomOutText, pathZoomOut, !b2Icon, w2);
            SetDualButton(btnZoomIn, txtZoomInText, pathZoomIn, !b2Icon, w2);
            double vw = viewLevel == 0 ? 80 : (viewLevel == 1 ? 54 : 40); // V154：两字档 50→54，防止第二个字渲染截断
            btnFitPage.Content = viewLevel == 0 ? "单页视图" : (viewLevel == 1 ? "单页" : "单");
            btnFitPage.Width = vw;
            btnFitWidth.Content = viewLevel == 0 ? "放大视图" : (viewLevel == 1 ? "放大" : "放");
            btnFitWidth.Width = vw;
            btnFitDouble.Content = viewLevel == 0 ? "双页视图" : (viewLevel == 1 ? "双页" : "双");
            btnFitDouble.Width = vw;
        }

        private void SetDualButton(System.Windows.Controls.Button btn, System.Windows.Controls.TextBlock txt,
                                   System.Windows.Shapes.Path path, bool text, double w)
        {
            txt.Visibility = text ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            path.Visibility = text ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            btn.Width = w;
        }

        /// <summary>形态判定（V152）：按可用宽度算出目标档位（放得下且文字最多的档）。
        /// 升档（目标更宽松=恢复文字）立即执行；降档（目标更紧凑）需跨过当前档阈值-滞回（防临界抖动）。
        /// 全部元素固定宽，宽度变化由 Grid/StackPanel 引擎自动布局，无需 Measure。</summary>
        private void UpdateToolbarSpacing()
        {
            if (previewToolbar == null || previewToolbar.ActualWidth <= 0) return;
            double avail = previewToolbar.ActualWidth - 18; // Border Padding 左右各 8 + BorderThickness 左右各 1
            if (avail <= 40) return;

            int want = 0;
            while (want < LevelThreshold.Length - 1 && avail < LevelThreshold[want]) want++;

            // 由当前状态反推当前档位
            int cl;
            if (tbViewLevel == 2) cl = 4;
            else if (tbViewLevel == 1) cl = 3;
            else if (tbB1Icon && tbB2Icon) cl = 2;
            else if (tbB1Icon) cl = 1;
            else cl = 0;

            // V152 修正：升档（want<cl，目标更宽松）立即切换；降档（want>cl，目标更紧凑）带滞回
            if (want < cl || (want > cl && avail < LevelThreshold[cl] - ToolbarHysteresis))
            {
                SetToolbarStates(want >= 1, want >= 2, want >= 3 ? (want == 4 ? 2 : 1) : 0);
            }
            previewToolbarInner.UpdateLayout(); // 一次布局应用本帧（若形态切换过）
        }

        /// <summary>操作提示行（左下角顶部固定行）：显示最新一条操作说明/反馈/进度，新替换旧；isError 时红色。</summary>
        private void SetOperationHint(string text, bool isError = false)
        {
            if (txtOperationHint == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetOperationHint(text, isError)));
                return;
            }
            txtOperationHint.Text = text;
            txtOperationHint.Foreground = isError
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB2, 0x22, 0x22))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x4E, 0x79));
        }

        /// <summary>分隔条拖动完成：把左栏宽度写入配置，下次启动恢复。</summary>
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // 拖动结束瞬间列宽可能尚未重新布局（ActualWidth 还是旧值），延迟到布局完成后读取，再立即写盘
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (leftPanelColumn == null) return;
                AppConfig.LeftPanelWidth = (int)Math.Round(leftPanelColumn.ActualWidth);
                AppConfig.SaveLeftPanelWidth();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>启动时恢复上次拖动的左栏宽度（限制在合理范围，避免窗口过窄时挤压预览区）。</summary>
        private void RestoreLeftPanelWidth()
        {
            try
            {
                if (leftPanelColumn == null || AppConfig.LeftPanelWidth <= 0) return;
                double minLeft = leftPanelColumn.MinWidth > 0 ? leftPanelColumn.MinWidth : 420;
                double maxLeft = Math.Max(minLeft, this.ActualWidth * 0.6);
                double w = Math.Max(minLeft, Math.Min(AppConfig.LeftPanelWidth, maxLeft));
                leftPanelColumn.Width = new GridLength(w);
            }
            catch
            {
            }
        }

        // ===================== 保存阶段动态提示（对齐原版省略号动画 + 已用时） =====================
        private void StartSavingIndicator()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(StartSavingIndicator));
                return;
            }
            StopSavingIndicator();
            savingStartTime = DateTime.Now;
            savingDotPhase = 0;
            savingTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            savingTimer.Tick += (s, e) => UpdateSavingIndicatorTick();
            savingTimer.Start();
            UpdateSavingIndicatorTick();
        }

        private void StopSavingIndicator()
        {
            if (savingTimer != null)
            {
                savingTimer.Stop();
                savingTimer = null;
            }
            UpdatePlacementOperationHint();
        }

        private void UpdateSavingIndicatorTick()
        {
            if (savingTimer == null) return;
            savingDotPhase = (savingDotPhase + 1) % 8;
            int dotCount = 3 + (savingDotPhase < 4 ? savingDotPhase : 7 - savingDotPhase);
            TimeSpan elapsed = DateTime.Now - savingStartTime;
            SetOperationHint(string.Format("正在生成盖章文件中{0}\n已用时 {1:mm\\:ss}",
                new string('.', dotCount), elapsed));
        }

        /// <summary>设置区滚动变化：内容未显示完时显示底部虚化，顶部内容滚出时显示顶部虚化，滚到底/顶自动隐藏。</summary>
        /// <remarks>此处不再调用 UpdateSettingsHeight：设置行高会再次触发本事件形成递归。
        /// 高度重算只由 Loaded / SizeChanged / 折叠切换触发。</remarks>
        private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (settingsScroll == null) return;
            bool canScrollDown = settingsScroll.ScrollableHeight > 1 &&
                                 settingsScroll.VerticalOffset + settingsScroll.ViewportHeight
                                 < settingsScroll.ExtentHeight - 2;
            if (scrollHint != null)
                scrollHint.Visibility = canScrollDown ? Visibility.Visible : Visibility.Collapsed;
            bool canScrollUp = settingsScroll.VerticalOffset > 1;
            if (scrollHintTop != null)
                scrollHintTop.Visibility = canScrollUp ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>设置区理想高度缓存：按"3按文字盖章展开、4印章参数收起、5其他设置收起"时内容完整显示的高度测量一次。</summary>
        private double _settingsIdealHeight = -1;

        /// <summary>
        /// 左栏上下两区高度分配（固定阈值，只依赖窗口高度一个变量，不随折叠/内容变化重算）：
        /// 拉高时设置区优先长到理想高度，封顶后剩余空间才给日志区（日志区随窗口继续拉高而变高）；
        /// 压缩时若日志区高于最小高度则先压缩日志区，日志区到达最小高度后压缩全部落在设置区（设置区有滚动条，不怕压缩）。
        /// 可用空间直接读 leftGrid 布局后的实际高度（= 窗口客户区 - 外框顶部边距），不再按固定值估算。
        /// </summary>
        private void UpdateSettingsHeight()
        {
            if (settingsRow == null || logRow == null || settingsContent == null || leftGrid == null) return;
            EnsureSettingsIdealHeight();
            const double minLog = 300; // 日志区最小高度（操作提示蓝条 + 日志文字框），保证初始 7 条说明完整可读
            // 固定行：盖章按钮行(Auto) + 分隔线(1)，布局完成后读实际高度，比估算值准
            double fixedRows = leftGrid.RowDefinitions.Count > 3
                ? leftGrid.RowDefinitions[1].ActualHeight + leftGrid.RowDefinitions[2].ActualHeight
                : 51;
            // 可用空间 = 窗口真实客户区高度（rootGrid 撑满客户区，不受 leftGrid 行高反向影响）
            // 之前误用 leftGrid.ActualHeight：leftGrid 高度被自身 Pixel 行总和撑住，窗口变矮时保持旧值，
            // 导致 space 不变、行高永不更新（死锁），日志区底部溢出被窗口裁掉。
            double frameBorder = leftFrame == null ? 0
                : leftFrame.BorderThickness.Top + leftFrame.BorderThickness.Bottom;
            double space = Math.Max(0, rootGrid.ActualHeight - frameBorder - fixedRows);
            if (space <= 0) return;
            double ideal = _settingsIdealHeight > 1 ? _settingsIdealHeight : Math.Max(0, space - minLog);

            double targetSettings, targetLog;
            if (space >= ideal + minLog)
            {
                // 窗口足够高：设置区封顶到理想高度，剩余全部给日志区
                targetSettings = ideal;
                targetLog = space - ideal;
            }
            else
            {
                // 空间不足：日志区保底最小高度，压缩全部落在设置区（设置区有滚动条）
                targetLog = minLog;
                targetSettings = space - minLog;
            }
            if (targetSettings < 0) { targetSettings = 0; targetLog = space; }

            bool changed =
                settingsRow.Height.GridUnitType != GridUnitType.Pixel ||
                Math.Abs(settingsRow.Height.Value - targetSettings) > 1 ||
                logRow.Height.GridUnitType != GridUnitType.Pixel ||
                Math.Abs(logRow.Height.Value - targetLog) > 1;
            if (changed)
            {
                settingsRow.Height = new GridLength(targetSettings);
                logRow.Height = new GridLength(targetLog);
            }
        }

        /// <summary>
        /// 布局回归测试（/layouttest 启动）：遍历预设窗口高度，断言：
        /// ① 日志区最小高度 300 始终保底；② 行高总和不超过窗口客户区（不溢出）；
        /// ③ 最低窗口高度下日志区恰为 300；④ 高窗口下设置区封顶到理想高度。
        /// 结果写运行目录 layout_test.log，退出前恢复原窗口尺寸。
        /// </summary>
        private void RunLayoutTest()
        {
            double initW = Width, initH = Height, initT = Top, initL = Left;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("PDFQFZ 布局回归测试 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("窗口 MinHeight=" + MinHeight.ToString("F0") + " MinWidth=" + MinWidth.ToString("F0")
                              + " | 设置区理想高度(3展开/4,5收起)=" + _settingsIdealHeight.ToString("F0"));
                double[] heights = { 680, 850, 1030, 1200, 1500 };
                int failCount = 0;
                foreach (double h in heights)
                {
                    Height = h;
                    WaitForLayout();
                    double winH = ActualHeight, rootH = rootGrid.ActualHeight;
                    double sH = settingsRow.ActualHeight, r1 = leftGrid.RowDefinitions[1].ActualHeight,
                           r2 = leftGrid.RowDefinitions[2].ActualHeight, lH = logRow.ActualHeight;
                    double sum = sH + r1 + r2 + lH;
                    bool logMinOk = lH >= 299.5;
                    bool noOverflow = sum <= rootH + 1.5;
                    bool lowOk = h <= 700 ? Math.Abs(lH - 300) <= 2 : true;
                    bool highOk = h >= 1400 ? Math.Abs(sH - _settingsIdealHeight) <= 10 : true;
                    bool pass = logMinOk && noOverflow && lowOk && highOk;
                    if (!pass) failCount++;
                    sb.AppendLine($"[{(pass ? "PASS" : "FAIL")}] 窗口H={h:F0}(实际{winH:F0}) rootGridH={rootH:F0} " +
                        $"设置区={sH:F0} 固定行={r1:F0}+{r2:F0} 日志区={lH:F0} 总和={sum:F0} | " +
                        $"日志>=300:{logMinOk} 无溢出:{noOverflow} 最低保底:{lowOk} 高窗封顶:{highOk}");
                }
                sb.AppendLine(failCount == 0 ? "== 全部通过 ==" : "== 存在失败项: " + failCount + " ==");
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout_test.log"), sb.ToString());
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout_test.log"),
                        "异常: " + ex + Environment.NewLine);
                }
                catch { }
            }
            finally
            {
                // 恢复初始窗口尺寸后关闭，避免测试尺寸被写入用户配置
                Width = initW; Height = initH; Top = initT; Left = initL;
                WaitForLayout();
                Close();
            }
        }

        /// <summary>等待布局与 Loaded 优先级的 UpdateSettingsHeight 执行完成（Background 优先级低于 Loaded）。</summary>
        private void WaitForLayout()
        {
            Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 静默测量一次设置区理想高度。若当前折叠状态恰好是"3展开、4、5收起"则直接读取，零切换零闪烁；
        /// 否则临时切到该组合读取内容高度后立即恢复（毫秒级、一次性的，ApplyFoldState 无副作用）。
        /// </summary>
        private void EnsureSettingsIdealHeight()
        {
            if (_settingsIdealHeight > 1 || settingsContent == null || settingsScroll == null) return;
            double padV = settingsScroll.Padding.Top + settingsScroll.Padding.Bottom;
            bool alreadyIdeal = AppConfig.FoldAutoText == 1 && AppConfig.FoldSealParams != 1 && AppConfig.FoldOther != 1;
            if (!alreadyIdeal)
            {
                bool a = AppConfig.FoldAutoText == 1, s = AppConfig.FoldSealParams == 1, o = AppConfig.FoldOther == 1;
                ApplyFoldState(autoTextContent, foldAutoTextArrow, foldAutoTextText, autoTextHeaderGrid, btnFoldAutoText, true);
                ApplyFoldState(sealParamsContent, foldSealParamsArrow, foldSealParamsText, sealParamsHeaderGrid, btnFoldSealParams, false);
                ApplyFoldState(otherContent, foldOtherArrow, foldOtherText, otherHeaderGrid, btnFoldOther, false);
                settingsContent.UpdateLayout();
                _settingsIdealHeight = settingsContent.DesiredSize.Height + padV;
                ApplyFoldState(autoTextContent, foldAutoTextArrow, foldAutoTextText, autoTextHeaderGrid, btnFoldAutoText, a);
                ApplyFoldState(sealParamsContent, foldSealParamsArrow, foldSealParamsText, sealParamsHeaderGrid, btnFoldSealParams, s);
                ApplyFoldState(otherContent, foldOtherArrow, foldOtherText, otherHeaderGrid, btnFoldOther, o);
                settingsContent.UpdateLayout();
            }
            else
            {
                _settingsIdealHeight = settingsContent.DesiredSize.Height + padV;
            }
            if (_settingsIdealHeight <= 1) _settingsIdealHeight = 600; // 兜底，避免极端情况死循环
        }

        protected override void OnClosed(EventArgs e)
        {
            StopSavingIndicator();
            ReleasePdfResources();
            AppConfig.SaveUiConfig();
            base.OnClosed(e);
        }

        private sealed class AutoStampOperation
        {
            public string Keyword;
            public int BatchId;
            public string FilePath;   // 所属 PDF 文件（多文件按文字盖章时按文件区分）
        }

        /// <summary>单文件按文字盖章结果（多文件汇总用）。</summary>
        private sealed class AutoPlaceFileResult
        {
            public bool Success;       // 是否完成放置
            public bool Cancelled;     // 用户取消（重复放置确认选“取消”）
            public int Added;          // 新增印章数
            public string SkipReason;  // 未处理原因（图片型/未找到文字/关键词）
        }

        /// <summary>印章下拉项数据对象：显示名（不含后缀，可重命名）+ 路径 + 选中状态。</summary>
        private sealed class StampPickerItem
        {
            public string DisplayName { get; set; }
            public string Path { get; set; }
            public bool IsSelected { get; set; }
            public override string ToString() => DisplayName ?? "";
        }
    }
}
