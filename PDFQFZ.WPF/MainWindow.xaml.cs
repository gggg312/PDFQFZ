using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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

        // 当前选中的印章文件名（用于按印章分别保存/恢复参数）
        private string _currentStampFileName = "";

        public MainWindow(string[] args)
        {
            InitializeComponent();
            logText.Text = InitialHelpText;   // 初始显示帮助说明（对齐原版）
            logContainsOnlyHelp = true;
            LoadConfigToUi();
            InitFoldState();   // 需在 LoadConfigToUi（内含 AppConfig.LoadFromIni）之后，才能按配置恢复折叠状态
            HookEvents();
            HookAutoKeywordWatermark();
            HookContextFilterEvents();
            UpdateAutoKeywordWatermark();

            if (args != null && args.Length > 0 && File.Exists(args[0]))
            {
                LoadPdf(args[0]);
            }
        }

        // ===================== 初始化 =====================
        private void InitFoldState()
        {
            // 按配置恢复 3/4/5 的展开收起状态（config.ini 关闭时保存，首次默认：按文字盖章展开、印章参数/其他设置折叠）
            ApplyFoldState(autoTextContent, foldAutoTextArrow, foldAutoTextText, AppConfig.FoldAutoText == 1);
            ApplyFoldState(sealParamsContent, foldSealParamsArrow, foldSealParamsText, AppConfig.FoldSealParams == 1);
            ApplyFoldState(otherContent, foldOtherArrow, foldOtherText, AppConfig.FoldOther == 1);
        }

        /// <summary>按指定展开状态设置折叠区域（内容可见性、箭头、文字、展开红字提醒）。</summary>
        private static void ApplyFoldState(System.Windows.Controls.StackPanel content,
                                           System.Windows.Controls.TextBlock arrow,
                                           System.Windows.Controls.TextBlock text,
                                           bool expanded)
        {
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = expanded ? "▼" : "▶";
            text.Text = expanded ? "收起设置" : "展开设置";
            System.Windows.Media.Brush redBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0x33, 0x33));
            System.Windows.Media.Brush defaultBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
            arrow.Foreground = expanded ? redBrush : defaultBrush;
            text.Foreground = expanded ? redBrush : defaultBrush;
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
                chkRemoveWhite.IsChecked = false;   // 默认关闭去除白色背景
                txtTolerance.Text = "20";
                UpdateToleranceEnabled();
                comboSeamPosition.SelectedIndex = AppConfig.WzType;
                txtSeamPosPct.Text = AppConfig.WzPercent.ToString();
                txtMaxSplit.Text = AppConfig.MaxFgs.ToString();

                // 按文字盖章——上下文过滤（从配置恢复）
                chkContextFilter.IsChecked = AppConfig.ContextFilterEnabled;
                txtContextKeywords.Text = string.IsNullOrEmpty(AppConfig.ContextKeywords) ? "盖章,公章" : AppConfig.ContextKeywords;
                txtContextRange.Text = AppConfig.ContextRange.ToString();
                comboContextMatch.SelectedIndex = (AppConfig.ContextMatch == 1) ? 1 : 0;
                UpdateContextFilterEnabled();
                UpdateContextKeywordsWatermark();

                comboSignature.SelectedIndex = AppConfig.QmType;
                txtSignature.Text = AppConfig.QmType == 1 ? AppConfig.SignBuiltInPath : AppConfig.SignCustomPath;
                txtSignaturePass.Password = AppConfig.QmType == 1 ? AppConfig.SignBuiltInPass : AppConfig.SignCustomPass;
                UpdateSignatureEnabled();

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
            btnStampFile.Click += (s, e) => ChooseStampFile();
            comboStamp.SelectionChanged += (s, e) => OnStampSelectionChanged();
            // 用 Preview 事件确保文件拖放可靠（TextBox 内部会拦截普通 DragOver/Drop）
            txtSourcePdf.PreviewDragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
            txtSourcePdf.PreviewDrop += OnSourceDrop;
            txtOutputDir.PreviewDragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
            txtOutputDir.PreviewDrop += OnOutputDrop;
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

            btnFoldSealParams.Click += (s, e) => ToggleFold(sealParamsContent, foldSealParamsArrow, foldSealParamsText);
            btnFoldOther.Click += (s, e) => ToggleFold(otherContent, foldOtherArrow, foldOtherText);
            btnFoldAutoText.Click += (s, e) => ToggleFold(autoTextContent, foldAutoTextArrow, foldAutoTextText);
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
            };

            btnGenerate.Click += async (s, e) => await OnGenerateClickAsync();
            btnAutoPlace.Click += (s, e) => OnAutoPlaceClick();
            btnUndoAuto.Click += (s, e) => OnUndoAutoClick();
            btnSpecifiedPage.Click += (s, e) => OnSpecifiedPageClick();

            comboSignature.SelectionChanged += (s, e) => UpdateSignatureEnabled();
            comboPageStamp.SelectionChanged += (s, e) => UpdatePlacementOperationHint();

            // 窗口大小变化时延迟重算预览布局：SizeChanged 触发时子控件 previewScroll 可能尚未完成布局，
            // 直接读 ViewportWidth/Height 会拿到旧值，导致全屏/还原后页面大小不更新（需手动切换视图才正常）
            SizeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(RelayoutPreview),
                System.Windows.Threading.DispatcherPriority.Loaded);
            // 窗口高度变化时重新分配设置区/提示区高度（设置区优先显示全）。
            // 必须延迟到布局完成后再读 ActualHeight，否则 SizeChanged 同步阶段拿到的是旧值。
            SizeChanged += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateSettingsHeight),
                System.Windows.Threading.DispatcherPriority.Loaded);
            Loaded += (s, e) => { UpdateSettingsHeight(); RestoreLeftPanelWidth(); UpdateToolbarSpacing(); };

            // 去除白色背景：未勾选时容差不可编辑
            chkRemoveWhite.Checked += (s, e) => UpdateToleranceEnabled();
            chkRemoveWhite.Unchecked += (s, e) => UpdateToleranceEnabled();

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
            chkContextFilter.Checked += (s, e) => UpdateContextFilterEnabled();
            chkContextFilter.Unchecked += (s, e) => UpdateContextFilterEnabled();
            txtContextKeywords.TextChanged += (s, e) => UpdateContextKeywordsWatermark();
            txtContextKeywords.GotKeyboardFocus += (s, e) => UpdateContextKeywordsWatermark();
            txtContextKeywords.LostKeyboardFocus += (s, e) => UpdateContextKeywordsWatermark();
        }

        /// <summary>根据勾选状态启用/禁用关键词输入框、范围输入框、匹配方式下拉。</summary>
        private void UpdateContextFilterEnabled()
        {
            bool enabled = chkContextFilter.IsChecked == true;
            txtContextKeywords.IsEnabled = enabled;
            txtContextRange.IsEnabled = enabled;
            comboContextMatch.IsEnabled = enabled;
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

        /// <summary>历史下拉项右侧删除叉：删除该条历史，不填入输入框。</summary>
        private void HistoryDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string keyword)
            {
                e.Handled = true;
                AppConfig.RemoveAutoStampKeyword(keyword);
                RebuildAutoHistoryItems();
                SetOperationHint(string.Format("已从历史中删除：“{0}”。", keyword));
            }
        }

        /// <summary>印章下拉项的删除按钮：从 yz.log 移除该印章，并刷新列表。</summary>
        private void StampDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string path)
            {
                e.Handled = true;
                string fileName = Path.GetFileName(path);

                // 从配置文件删除
                AppConfig.RemoveStampPath(path);

                // 从下拉列表移除对应项
                StampItem itemToRemove = null;
                foreach (var obj in comboStamp.Items)
                {
                    if (obj is StampItem item && item.Tag is string p
                        && string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                    {
                        itemToRemove = item;
                        break;
                    }
                }
                bool removedSelected = (itemToRemove != null && comboStamp.SelectedItem == itemToRemove);
                if (itemToRemove != null)
                {
                    comboStamp.Items.Remove(itemToRemove);
                }

                // 删除的是当前选中项 → 自动切到第一个（或清空）
                if (removedSelected)
                {
                    if (comboStamp.Items.Count > 0)
                    {
                        comboStamp.SelectedIndex = 0;
                    }
                    else
                    {
                        comboStamp.SelectedIndex = -1;
                        _currentStampFileName = null;
                    }
                }

                comboStamp.IsDropDownOpen = false;
                SetOperationHint(string.Format("已从印章列表删除：“{0}”。", fileName));
            }
        }

        // ===================== 折叠 =====================
        private void ToggleFold(System.Windows.Controls.StackPanel content, System.Windows.Controls.TextBlock arrow, System.Windows.Controls.TextBlock text)
        {
            bool collapsed = content.Visibility != Visibility.Visible;
            content.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = collapsed ? "▼" : "▶";
            text.Text = collapsed ? "收起设置" : "展开设置";
            // 展开状态（显示"收起设置"）时文字和箭头用红色，提醒用户可以点击收起；收起状态恢复默认色
            System.Windows.Media.Brush redBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0x33, 0x33));
            System.Windows.Media.Brush defaultBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66));
            arrow.Foreground = collapsed ? redBrush : defaultBrush;
            text.Foreground = collapsed ? redBrush : defaultBrush;
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
                Multiselect = true
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
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择 PDF 所在文件夹" };
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
            // 目录模式：输出目录固定为“上传文件夹\已盖章”（无条件，避免残留文件模式的旧目录导致输出到同一层）
            string outDir = Path.Combine(dir, "已盖章");
            txtOutputDir.Text = outDir;
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
                LoadPdf(path);
            }
        }

        private void ChooseOutputDir()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "选择输出目录" };
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
                Title = "选择印章图片（可多选，追加到印章列表）",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                var added = new List<string>();
                foreach (string filePath in dlg.FileNames)
                {
                    comboStamp.Items.Add(new StampItem { Content = Path.GetFileName(filePath), Tag = filePath });
                    added.Add(filePath);
                }
                if (added.Count > 0)
                {
                    AppConfig.AppendStampPaths(added);
                    // 对齐原版：导入后选中最新一张
                    comboStamp.SelectedIndex = comboStamp.Items.Count - 1;
                }
            }
        }

        /// <summary>加载印章列表（yz.log），并选中上次使用的印章；列表为空时用配置路径补一条。</summary>
        private void LoadStampList()
        {
            comboStamp.Items.Clear();
            List<string> paths = AppConfig.LoadStampPaths();
            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(AppConfig.LastStampImagePath)
                && File.Exists(AppConfig.LastStampImagePath))
            {
                paths.Add(AppConfig.LastStampImagePath);
                AppConfig.AppendStampPaths(new[] { AppConfig.LastStampImagePath });
            }

            StampItem lastUsed = null;
            foreach (string path in paths)
            {
                var item = new StampItem { Content = Path.GetFileName(path), Tag = path };
                comboStamp.Items.Add(item);
                if (string.Equals(path, AppConfig.LastStampImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    lastUsed = item;
                }
            }
            if (lastUsed != null)
            {
                comboStamp.SelectedItem = lastUsed;
            }
            else if (comboStamp.Items.Count > 0)
            {
                comboStamp.SelectedIndex = 0;
            }
        }

        /// <summary>当前选中的印章路径；未选中时返回空字符串。</summary>
        private string SelectedStampPath()
        {
            if (comboStamp.SelectedItem is StampItem item && item.Tag is string path)
            {
                return path;
            }
            return string.Empty;
        }

        private void OnStampSelectionChanged()
        {
            string path = SelectedStampPath();
            if (path.Length == 0)
            {
                return;
            }

            // 先保存上一个印章的参数（首次切换时 _currentStampFileName 为空，跳过）
            SaveCurrentStampParams();

            string fileName = Path.GetFileName(path);
            _currentStampFileName = fileName;

            // 加载新印章上次保存的参数（未保存过则用默认值）
            AppConfig.StampParams p = AppConfig.LoadStampParams(fileName);
            ApplyStampParams(p);

            AppConfig.LastStampImagePath = path;
            AppConfig.SaveUiConfig();
        }

        /// <summary>容差输入框启用状态：只有勾选"去除白色背景"时才能编辑。</summary>
        private void UpdateToleranceEnabled()
        {
            txtTolerance.IsEnabled = chkRemoveWhite.IsChecked == true;
        }

        /// <summary>把当前界面的印章参数保存到当前印章名下。</summary>
        private void SaveCurrentStampParams()
        {
            if (string.IsNullOrEmpty(_currentStampFileName))
            {
                return;
            }
            try
            {
                AppConfig.StampParams p = new AppConfig.StampParams
                {
                    Size = TryParseInt(txtStampSize.Text, 1, 500, out int s) ? s : 40,
                    Rotation = TryParseInt(txtRotation.Text, -360, 360, out int r) ? r : 0,
                    RotationHandle = comboRotationHandle.SelectedIndex >= 0 ? comboRotationHandle.SelectedIndex : 0,
                    Opacity = TryParseInt(txtOpacity.Text, 0, 100, out int o) ? o : 60,
                    RandomParams = chkRandomParams.IsChecked == true,
                    RandomRange = TryParseInt(txtRandomRange.Text, 0, 90, out int rr) ? rr : 5,
                    RemoveWhite = chkRemoveWhite.IsChecked == true,
                    Tolerance = TryParseInt(txtTolerance.Text, 0, 50, out int t) ? t : 20,
                    MaxSplit = TryParseInt(txtMaxSplit.Text, 1, 10000, out int ms) ? ms : 500
                };
                AppConfig.SaveStampParams(_currentStampFileName, p);
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>把印章参数应用到界面控件。</summary>
        private void ApplyStampParams(AppConfig.StampParams p)
        {
            if (p == null)
            {
                return;
            }
            txtStampSize.Text = p.Size.ToString();
            txtRotation.Text = p.Rotation.ToString();
            comboRotationHandle.SelectedIndex = (p.RotationHandle >= 0 && p.RotationHandle <= 1) ? p.RotationHandle : 0;
            txtOpacity.Text = p.Opacity.ToString();
            chkRandomParams.IsChecked = p.RandomParams;
            txtRandomRange.Text = p.RandomRange.ToString();
            chkRemoveWhite.IsChecked = p.RemoveWhite;
            txtTolerance.Text = p.Tolerance.ToString();
            if (p.MaxSplit > 0) txtMaxSplit.Text = p.MaxSplit.ToString();
            UpdateToleranceEnabled();
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

        private void OnOutputDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0 && Directory.Exists(files[0]))
                {
                    txtOutputDir.Text = files[0];
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
            previewPlaceholderViewbox.Visibility = Visibility.Visible;
            previewImage.Visibility = Visibility.Collapsed;
            overlayCanvas.Visibility = Visibility.Collapsed;
            UpdatePageInfo();
            comboCurrentFile.Items.Clear();
            txtFileTotalPages.Text = "";
            btnUndoAuto.IsEnabled = false;
            logText.Text = InitialHelpText;
            logContainsOnlyHelp = true;
            UpdatePlacementOperationHint();
        }

        private void LoadPdf(string path)
        {
            try
            {
                stampPlacements.Clear();
                autoStampOperations.Clear();
                ClearOverlayImages();
                ReleasePdfResources();
                logText.Text = InitialHelpText;   // 拖入新文件：预览与日志区都恢复初始帮助说明
                logContainsOnlyHelp = true;

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
                previewPlaceholderViewbox.Visibility = Visibility.Collapsed;
                previewImage.Visibility = Visibility.Visible;
                overlayCanvas.Visibility = Visibility.Visible;
                btnUndoAuto.IsEnabled = false;
                zoomPercent = 0;
                zoomInitialized = false;
                UpdatePageInfo();
                RelayoutPreview();
                UpdatePlacementOperationHint();
            }
            catch (Exception ex)
            {
                // 完整异常链（含 InnerException 底层原因）写入诊断日志 + 弹窗，便于定位引擎加载问题
                string chain = PDFQFZ.WPF.Services.PdfiumBootstrap.BuildExceptionChain(ex);
                PDFQFZ.WPF.Services.PdfiumBootstrap.WriteDiag("加载 PDF 失败，完整异常链：" + Environment.NewLine + chain);
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
                        double overlayLeft = placement.CenterRatio
                            ? dispW * placement.X - overlaySize.Width / 2.0
                            : (dispW - overlaySize.Width) * placement.X;
                        double overlayTop = placement.CenterRatio
                            ? dispH * placement.Y - overlaySize.Height / 2.0
                            : (dispH - overlaySize.Height) * placement.Y;
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
            if (pdfRenderer == null || isGenerating) return;
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
            if (pdfRenderer == null || isGenerating) return;
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

        /// <summary>计算含随机旋转的最终角度：勾选盖章随机旋转时，在基础角度上叠加 ±range° 的随机值。</summary>
        private int GetEffectiveRotation(int baseRotation)
        {
            if (chkRandomParams.IsChecked == true)
            {
                int range = 5;
                int.TryParse(txtRandomRange.Text, out range);
                if (range < 0) range = 0;
                if (range > 0)
                {
                    lock (StampRandomGenerator)
                    {
                        return baseRotation + StampRandomGenerator.Next(-range, range + 1);
                    }
                }
            }
            return baseRotation;
        }

        private void AddPreviewStamp(float px, float py, string stampPath, int stampType, int pageNumber = 0)
        {
            int sizeMm = GetSizeValue();
            int currentOpacity = GetOpacityValue();
            int currentRotation = GetRotationValue();
            int whiteTolerance = GetToleranceValue();
            bool useWhiteTransparency = chkRemoveWhite.IsChecked == true;
            bool useOriginalRotationCrop = comboRotationHandle.SelectedIndex == 0;

            if (stampType == SpecifiedPageStampType && specifiedRangeFirstClickPending &&
                specifiedPageRange != null && IsAtSpecifiedEndPage())
            {
                activeSpecifiedBatchId = stampPlacements.CreateBatchId();
                for (int page = specifiedPageRange.StartPage; page <= specifiedPageRange.EndPage; page++)
                {
                    stampPlacements.Add(sourcePath, page, px, py, stampPath, sizeMm,
                        currentOpacity, GetEffectiveRotation(currentRotation), whiteTolerance, useWhiteTransparency,
                        useOriginalRotationCrop, activeSpecifiedBatchId, centerRatio: true);
                }
                specifiedRangeFirstClickPending = false;
                activeSpecifiedBatchId = 0;
                comboPageStamp.SelectedIndex = 1;
                // 原版：完成提示只写右侧操作提示区，不写左侧日志区
                SetOperationHint("指定范围页印章已添加，现已回到手动点击盖章；右键印章可删除。需要再添加一批时，请重新点击“指定范围页盖章”。");
                return;
            }

            int targetPage = pageNumber > 0 ? pageNumber : currentPageIndex + 1;
            stampPlacements.Add(sourcePath, targetPage, px, py, stampPath, sizeMm,
                currentOpacity, GetEffectiveRotation(currentRotation), whiteTolerance, useWhiteTransparency,
                useOriginalRotationCrop, 0, centerRatio: true);
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
                if (pdfRenderer == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
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

                List<PdfTextMatch> allMatches;
                using (PdfTextSearcher searcher = new PdfTextSearcher(sourcePath))
                {
                    if (!searcher.HasAnyText())
                    {
                        MessageBox.Show("图片型 PDF 无法搜索到文字，无法放置印章", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    allMatches = searcher.FindAll(keyword);
                }

                if (allMatches == null || allMatches.Count == 0)
                {
                    MessageBox.Show("没找到您指定的盖章文字", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 解析上下文过滤参数（未勾选则不过滤）
                bool useContextFilter = chkContextFilter.IsChecked == true;
                string[] contextKeywords = useContextFilter ? ParseContextKeywords(txtContextKeywords.Text) : new string[0];
                int contextRange = ParseContextRange(txtContextRange.Text);
                bool requireAll = comboContextMatch.SelectedIndex == 1;

                List<PdfTextMatch> matches;
                if (contextKeywords.Length == 0)
                {
                    // 关键词留空 → 不过滤，全部盖
                    matches = allMatches;
                }
                else
                {
                    // 有关键词 → 二次搜索并过滤
                    using (PdfTextSearcher searcher = new PdfTextSearcher(sourcePath))
                    {
                        matches = searcher.FindAll(keyword, contextKeywords, contextRange, requireAll);
                    }
                    if (matches == null || matches.Count == 0)
                    {
                        string kwDisplay = string.Join("，", contextKeywords);
                        string modeDisplay = requireAll ? "全部关键词（且）" : "任一关键词（或）";
                        MessageBox.Show(
                            string.Format("找到了 {0} 处“{1}”，但附近都没有指定关键词，未盖章。\n\n关键词（共 {2} 个）：{3}\n匹配模式：{4}",
                                allMatches.Count, keyword, contextKeywords.Length, kwDisplay, modeDisplay),
                            "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                // 相同文字已放置过 → 确认后撤销旧批、按最新参数重盖
                int batchId = 0;
                bool appendToExistingBatch = false;
                AutoStampOperation existingOp = autoStampOperations.LastOrDefault(
                    o => string.Equals(o.Keyword, keyword, StringComparison.Ordinal));
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
                        return;
                    }
                    if (confirm == MessageBoxResult.Yes)
                    {
                        stampPlacements.RemoveBatch(sourcePath, existingOp.BatchId);
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

                int sizeMm = GetSizeValue();
                int currentOpacity = GetOpacityValue();
                int currentRotation = GetRotationValue();
                int whiteTolerance = GetToleranceValue();
                bool useWhiteTransparency = chkRemoveWhite.IsChecked == true;
                bool useOriginalRotationCrop = comboRotationHandle.SelectedIndex == 0;

                System.Drawing.Size overlaySize = CalculateCurrentOverlaySize();
                double previewW = Math.Max(1, displayWidth);
                double previewH = Math.Max(1, displayHeight);
                float ratioW = (float)(overlaySize.Width / previewW);
                float ratioH = (float)(overlaySize.Height / previewH);

                int addedCount = 0;
                var addedPages = new List<int>();
                foreach (PdfTextMatch match in matches)
                {
                    AutoStampPositionResult pos = AutoStampPositionCalculator.Calculate(
                        match.CenterX, match.CenterY, match.PageWidth, match.PageHeight, ratioW, ratioH);

                    // 选"否"追加模式：旧批次同页已有坐标接近的印章则跳过，不重复叠加
                    if (appendToExistingBatch)
                    {
                        bool alreadyPlaced = stampPlacements.ForPage(sourcePath, match.PageIndex + 1)
                            .Any(p => p.BatchId == batchId
                                && Math.Abs(p.X - pos.Px) < 0.03
                                && Math.Abs(p.Y - pos.Py) < 0.03);
                        if (alreadyPlaced) continue;
                    }

                    stampPlacements.Add(sourcePath, match.PageIndex + 1, pos.Px, pos.Py, stampPath, sizeMm,
                        currentOpacity, GetEffectiveRotation(currentRotation), whiteTolerance, useWhiteTransparency,
                        useOriginalRotationCrop, batchId);
                    addedCount++;
                    addedPages.Add(match.PageIndex + 1);
                }

                if (appendToExistingBatch && addedCount == 0)
                {
                    AppendLog(string.Format("按文字盖章完成：关键词“{0}”，本次搜索到 {1} 处，位置上次都已盖过，无新增印章。", keyword, matches.Count));
                    SetOperationHint("按文字盖章完成：无新增，详见下方日志");
                    RefreshPreviewOverlays();
                    return;
                }

                if (!appendToExistingBatch)
                {
                    autoStampOperations.Add(new AutoStampOperation { Keyword = keyword, BatchId = batchId });
                }
                btnUndoAuto.IsEnabled = true;

                // 成功找到并完成盖章的文字才记入历史
                AppConfig.RecordAutoStampKeyword(keyword);

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
                        "按文字盖章完成：关键词“{0}”，共找到 {1} 处，其中 {2} 处附近有关键词，已在 {3} 盖章。关键词（共 {4} 个）：{5} | 匹配模式：{6}。右键单个章可删除，或点击“撤销放置”逐步撤销。",
                        keyword, allMatches.Count, matches.Count, pageDisplay, contextKeywords.Length, kwDisplay, modeDisplay));
                    SetOperationHint(string.Format("按文字盖章完成：{0} 处，详见下方日志", matches.Count));
                }
                else
                {
                    AppendLog(string.Format(
                        "按文字盖章完成：关键词“{0}”，共找到 {1} 处，已在 {2} 盖章。右键单个章可删除，或点击“撤销放置”逐步撤销。",
                        keyword, matches.Count, pageDisplay));
                    SetOperationHint(string.Format("按文字盖章完成：{0} 处，详见下方日志", matches.Count));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("放置印章失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // 撤销放置：每次撤销最近一次"按文字放置"
        private void OnUndoAutoClick()
        {
            if (autoStampOperations.Count == 0)
            {
                return;
            }

            AutoStampOperation op = autoStampOperations[autoStampOperations.Count - 1];
            autoStampOperations.RemoveAt(autoStampOperations.Count - 1);

            int removed = stampPlacements.RemoveBatch(sourcePath, op.BatchId);
            btnUndoAuto.IsEnabled = autoStampOperations.Count > 0;

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
            if (pdfRenderer == null || pageCount < 1)
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

            // 盖章时自动收起印章参数和其他设置，给预览区留更多空间
            if (sealParamsContent.Visibility == Visibility.Visible)
                ToggleFold(sealParamsContent, foldSealParamsArrow, foldSealParamsText);
            if (otherContent.Visibility == Visibility.Visible)
                ToggleFold(otherContent, foldOtherArrow, foldOtherText);

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
                        string output = OutputFileNamingPolicy.GetNextOutputPath(outDir, source,
                            AppConfig.FixStr, AppConfig.FixType == 1);
                        AppendLog(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, fileInfo.Name));
                        bool success = StampEngine.PDFWatermark(options, seamImage, xzbl,
                            source, output, source,
                            msg => AppendLog(msg),
                            (d, t) => SetOperationHint(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                                d, t, t > 0 ? (int)Math.Round(100.0 * d / t) : 0, fileInfo.Name)),
                            active => { if (active) StartSavingIndicator(); });
                        if (success && djType == 1)
                        {
                            StampEngine.PDFToiPDF(output, options.QmType, options.Cert);
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
                        string output = OutputFileNamingPolicy.GetNextOutputPath(outDir, file,
                            AppConfig.FixStr, AppConfig.FixType == 1);
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
                                StampEngine.PDFToiPDF(output, options.QmType, options.Cert);
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
            // 布局完成后滚动位置会被重置回顶部；这里再延迟滚动一次到底，保证最终停在最新一行。
            if (logScroll != null)
            {
                logScroll.UpdateLayout();
                logScroll.ScrollToEnd();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (logScroll != null) logScroll.ScrollToEnd();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        // ===== 工具栏响应式布局：组间间距分档压缩 + 按钮文字缩短 =====
        private const double ToolbarGapExpanded = 22;   // 展开档：组间空白充裕
        private const double ToolbarGapCompact = 12;    // 紧凑档：略窄
        private const double ToolbarGapMin = 8;         // 极限档：很窄
        private bool toolbarSpacingBusy;                // 防重入：判定过程中忽略后续 SizeChanged
        private int toolbarTextLevel = 0;               // 文字档：0=完整文字，1=双字，2=单字

        /// <summary>工具栏尺寸变化入口：只响应宽度变化，延迟到布局完成后再判定，防止换行→高度变化→再判定的振荡。</summary>
        private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || toolbarSpacingBusy) return;
            toolbarSpacingBusy = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { UpdateToolbarSpacing(); }
                finally { toolbarSpacingBusy = false; }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>设置组间空白（分隔线两侧到相邻组的距离），组内间距不动。</summary>
        private void SetToolbarGap(double side)
        {
            txtPageTotal.Margin = new Thickness(0, 0, side, 0);
            previewToolbarSep1.Margin = new Thickness(0, 0, side, 0);
            txtPercent.Margin = new Thickness(0, 0, side, 0);
            previewToolbarSep2.Margin = new Thickness(0, 0, side, 0);
        }

        /// <summary>切换按钮文字档位（0=全部完整；1=翻页/缩放缩短、视图完整；2=翻页/缩放缩短、视图双字；3=翻页/缩放缩短、视图单字），窗口恢复时还原。
        /// 缩减优先级：先缩"上一页/下一页/缩小/放大"，最后才缩视图按钮。</summary>
        private void SetToolbarText(int level)
        {
            if (toolbarTextLevel == level) return;
            toolbarTextLevel = level;
            // 翻页/缩放组：level>=1 即缩短为符号
            btnPrev.Content = level >= 1 ? "◀" : "上一页";
            btnNext.Content = level >= 1 ? "▶" : "下一页";
            btnZoomOut.Content = level >= 1 ? "−" : "缩小";
            btnZoomIn.Content = level >= 1 ? "+" : "放大";
            // 视图组三态：level<=1 完整文字；level==2 双字；level==3 单字
            if (level <= 1)
            {
                btnFitPage.Content = "单页视图"; btnFitWidth.Content = "放大视图"; btnFitDouble.Content = "双页视图";
            }
            else if (level == 2)
            {
                btnFitPage.Content = "单页"; btnFitWidth.Content = "放大"; btnFitDouble.Content = "双页";
            }
            else
            {
                btnFitPage.Content = "单"; btnFitWidth.Content = "放"; btnFitDouble.Content = "双";
            }
        }

        /// <summary>按可用宽度自动选档。判定方式：设置档位后强制立即布局（UpdateLayout），按 WrapPanel 实际高度是否仍为单行来判断，
        /// 确保计算与屏幕显示一致。档位顺序（从宽到窄）：全部完整+22 → 全部完整+12 → 全部完整+8 → 翻页/缩放缩短+8
        /// → 视图双字+8 → 视图单字+8 → WrapPanel 换行兜底。缩减优先级：先缩翻页/缩放按钮，最后缩视图按钮。</summary>
        private void UpdateToolbarSpacing()
        {
            if (previewToolbarWrap == null || previewToolbar.ActualWidth <= 0) return;
            double avail = previewToolbar.ActualWidth - 18; // Border Padding 左右各 8 + BorderThickness 左右各 1
            if (avail <= 40) return;
            int[][] steps =
            {
                new[] { 0, 22 },  // 全部完整 + 展开
                new[] { 0, 12 },  // 全部完整 + 紧凑
                new[] { 0, 8 },   // 全部完整 + 极限
                new[] { 1, 8 },   // 翻页/缩放缩短（视图仍完整）
                new[] { 2, 8 },   // 视图双字
                new[] { 3, 8 }    // 视图单字
            };
            foreach (var st in steps)
            {
                SetToolbarGap(st[1]);
                SetToolbarText(st[0]);
                previewToolbarWrap.UpdateLayout();
                if (IsToolbarSingleRow()) return;
            }
            // 全部放不下 → 保持视图单字+8，由 WrapPanel 自动换行兜底
        }

        /// <summary>工具栏是否为单行：WrapPanel 单行高约 26，两行高约 52+，以 40 为界。</summary>
        private bool IsToolbarSingleRow()
        {
            return previewToolbarWrap.ActualHeight > 0 && previewToolbarWrap.ActualHeight <= 40;
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
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "splitter_diag.log"),
                        "[" + DateTime.Now.ToString("HH:mm:ss") + "] 拖动保存: ActualWidth=" + leftPanelColumn.ActualWidth + "\r\n");
                }
                catch
                {
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>启动时恢复上次拖动的左栏宽度（限制在合理范围，避免窗口过窄时挤压预览区）。</summary>
        private void RestoreLeftPanelWidth()
        {
            var diag = new System.Text.StringBuilder();
            try
            {
                if (leftPanelColumn == null || AppConfig.LeftPanelWidth <= 0) return;
                double minLeft = leftPanelColumn.MinWidth > 0 ? leftPanelColumn.MinWidth : 420;
                double maxLeft = Math.Max(minLeft, this.ActualWidth * 0.6);
                double w = Math.Max(minLeft, Math.Min(AppConfig.LeftPanelWidth, maxLeft));
                leftPanelColumn.Width = new GridLength(w);
                diag.Append("[" + DateTime.Now.ToString("HH:mm:ss") + "] 恢复左栏: 配置=" + AppConfig.LeftPanelWidth
                    + ", 窗口宽=" + this.ActualWidth + ", min=" + minLeft + ", max=" + maxLeft
                    + ", 设置=" + w + ", 设置后ActualWidth=" + leftPanelColumn.ActualWidth + "\r\n");
            }
            finally
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "splitter_diag.log"), diag.ToString());
                }
                catch
                {
                }
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

        /// <summary>
        /// 左栏上下两区高度分配（严格顺序）：设置区优先长高，直到内容完全显示（无滚动条）；
        /// 内容显示全之后，窗口继续升高的空间才全部给提示区。窗口高度不足时，设置区压缩为滚动区，
        /// 提示区保持 190px 保证初始 7 行说明可读。
        /// </summary>
        private void UpdateSettingsHeight()
        {
            if (settingsRow == null || logRow == null || settingsContent == null || leftGrid == null) return;
            double padV = settingsScroll.Padding.Top + settingsScroll.Padding.Bottom; // ScrollViewer 上下内边距
            double contentH = settingsContent.ActualHeight + padV; // 内容真实高度 + 内边距，行高达到该值即无滚动条
            if (contentH <= 1) return;
            const double fixedRows = 50 + 1; // 按钮行(Auto) + 分隔线(1)
            const double minLog = 190;       // 提示区最小高度（容纳初始 7 行说明）
            // 关键：必须用窗口客户区高度减去左栏 Border 上下边距（MainWindow.xaml: Margin="0,14,0,14"）作为可用空间。
            // 不能用 leftGrid.ActualHeight——它等于内部行高的总和（Border 按内容排列 Grid），窗口缩小后行高不变它就
            // 不变，用它计算会把"放大后的行高"永远当成可用空间，导致缩小后无法恢复（死锁）。
            double space = Math.Max(0, this.ActualHeight - 28 - fixedRows);
            // 设置区优先：先满足内容全显示；空间不足时至少留出提示区最小高度
            double targetSettings = Math.Min(contentH, Math.Max(0, space - minLog));
            double targetLog = space - targetSettings;
            if (targetLog < minLog) { targetLog = minLog; targetSettings = space - minLog; }
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
        }

        /// <summary>印章下拉项数据对象（用普通数据类而非 ComboBoxItem，ItemTemplate 才能生效显示删除叉）。</summary>
        private sealed class StampItem
        {
            public string Content { get; set; }
            public string Tag { get; set; }
            // IsEditable=True 的 ComboBox 选中框用 ToString() 显示，重写后显示文件名而非类型名
            public override string ToString() => Content ?? "";
        }
    }
}
