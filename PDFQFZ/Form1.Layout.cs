using PDFQFZ.Library;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PDFQFZ
{
    public partial class Form1
    {
        private readonly Dictionary<RadioButton, int> fileModeRadios = new Dictionary<RadioButton, int>();
        private readonly Dictionary<RadioButton, int> seamModeRadios = new Dictionary<RadioButton, int>();
        private readonly Dictionary<RadioButton, int> pageStampRadios = new Dictionary<RadioButton, int>();
        private readonly Dictionary<RadioButton, int> outputModeRadios = new Dictionary<RadioButton, int>();
        private SplitContainer mainSplit;
        private Panel previewStage;
        private TableLayoutPanel previewViewport;
        private VScrollBar previewPageScrollBar;
        private TextBox currentPageInput;
        private Label totalPageLabel;
        private Button zoomOutButton;
        private Button zoomInButton;
        private TextBox zoomInput;
        private Panel currentPageInputHost;
        private Panel zoomInputHost;
        private Label zoomPercentLabel;
        private Button singlePageButton;
        private Button scrollViewButton;
        private TableLayoutPanel previewToolbar;
        private Label operationHint;
        private Label previewPlaceholder;
        private Button specifiedPageButton;
        private TableLayoutPanel leftLayout;
        private bool synchronizingModeControls;
        private bool synchronizingPageScrollBar;
        private int previewWheelDeltaRemainder;
        private PreviewViewMode previewViewMode = PreviewViewMode.SinglePage;
        private int previewZoomPercent = PreviewZoomPolicy.MinimumPercent;
        private bool previewPointerPressed;
        private bool previewPanActive;
        private Point previewPointerStartPoint;
        private Point previewPanStartOffset;
        private Control previewPointerCaptureOwner;
        private bool logContainsOnlyHelp = true;

        private sealed class PreviewStagePanel : Panel
        {
            public event MouseEventHandler PreviewMouseWheel;

            public PreviewStagePanel()
            {
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                Focus();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                PreviewMouseWheel?.Invoke(this, e);
                if ((ModifierKeys & Keys.Control) != Keys.Control)
                {
                    base.OnMouseWheel(e);
                }
            }
        }

        private const string InitialHelpText =
            "提示：\r\n" +
            "1、建议使用472像素以上且背景透明的印章图片。\r\n" +
            "2、使用合并模式会导致文字不可编辑，并且原数字签名丢失。\r\n" +
            "3、随意骑缝章与页面印章共用右侧预览定位，建议分开操作。\r\n" +
            "4、手动点击盖章：单击页面可添加印章，多次单击可添加多个印章；双击已有印章可删除。\r\n" +
            "5、指定范围页盖章：每次可在指定范围内各页相同位置添加1个印章；需要多个印章时请重复操作；双击已有印章可删除。\r\n" +
            "6、预览页面：Ctrl+鼠标滚轮可放大或缩小；按住鼠标左键并拖动可移动预览页面。\r\n" +
            "7、全部位置确认后，点击“盖章”生成输出文件。";

        private void InitializeAdaptiveLayout()
        {
            SuspendLayout();

            Text = "PDF盖页面章与骑缝章工具 （V1.3.0  GG优化版）";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1500, 930);
            MinimumSize = SizeFromClientSize(new Size(1500, 930));
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);

            Controls.Clear();
            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = false,
                SplitterWidth = 8,
                Padding = new Padding(10),
                BackColor = SystemColors.Control
            };
            Controls.Add(mainSplit);

            BuildLeftLayout(mainSplit.Panel1);
            BuildRightLayout(mainSplit.Panel2);
            ConfigureModeSynchronization();
            ResetPreviewSessionState();

            log.Text = InitialHelpText;
            logContainsOnlyHelp = true;
            cbxTransColor.Text = "去除白色背景";
            cbxTransColor.CheckedChanged += WhiteBackgroundOption_CheckedChanged;
            UpdateWhiteBackgroundOptionState();
            UpdatePlacementOperationHint();

            Resize += Form1_AdaptiveResize;
            Shown += Form1_InitialLayout;
            KeyDown += PreviewNavigation_KeyDown;
            ResumeLayout(true);
        }

        private void BuildLeftLayout(Control host)
        {
            Panel border = CreateBorderPanel();
            host.Controls.Add(border);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12, 9, 12, 10),
                ColumnCount = 1,
                RowCount = 5
            };
            leftLayout = layout;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Keep the settings sections compact when the window grows. The help
            // panel is the flexible region so controls do not drift apart.
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            border.Controls.Add(layout);

            layout.Controls.Add(BuildFileSection(), 0, 0);
            layout.Controls.Add(BuildStampModeSection(), 0, 1);
            layout.Controls.Add(BuildSettingsSection(), 0, 2);

            Panel actionPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
            bt_gz.Size = new Size(96, 36);
            bt_gz.Anchor = AnchorStyles.None;
            actionPanel.Controls.Add(bt_gz);
            actionPanel.Resize += (sender, args) =>
            {
                bt_gz.Location = new Point((actionPanel.ClientSize.Width - bt_gz.Width) / 2, 4);
            };
            layout.Controls.Add(actionPanel, 0, 3);

            log.Dock = DockStyle.Fill;
            log.Margin = new Padding(0, 0, 12, 0);
            log.ScrollBars = ScrollBars.Vertical;
            log.WordWrap = true;
            log.MinimumSize = new Size(0, 220);
            layout.Controls.Add(log, 0, 4);
        }

        private Control BuildFileSection()
        {
            TableLayoutPanel section = CreateSectionTable();
            section.Padding = new Padding(0, 8, 0, 9);

            Label heading = CreateSectionHeading("文件");
            section.Controls.Add(heading, 0, 0);
            section.SetRowSpan(heading, 4);

            FlowLayoutPanel modeRow = CreateFlowRow();
            modeRow.Controls.Add(CreateModeRadio("文件模式", 1, fileModeRadios));
            modeRow.Controls.Add(CreateModeRadio("目录模式", 0, fileModeRadios));
            section.Controls.Add(modeRow, 1, 0);

            section.Controls.Add(BuildPathField(label1, SelectPath, pathText), 1, 1);
            section.Controls.Add(BuildPathField(label2, OutPath, textBCpath), 1, 2);
            section.Controls.Add(BuildStampFileField(), 1, 3);
            return section;
        }

        private Control BuildPathField(Label label, Button button, TextBox textBox)
        {
            TableLayoutPanel field = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 2, 22, 2)
            };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            label.Dock = DockStyle.Fill;
            label.AutoSize = true;
            label.Margin = new Padding(0);
            field.Controls.Add(label, 0, 0);
            field.SetColumnSpan(label, 2);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 2, 7, 1);
            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 0, 1);
            field.Controls.Add(button, 0, 1);
            field.Controls.Add(textBox, 1, 1);
            return field;
        }

        private Control BuildStampFileField()
        {
            TableLayoutPanel field = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 2, 22, 2)
            };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            label3.Dock = DockStyle.Fill;
            label3.AutoSize = true;
            label3.Margin = new Padding(0);
            field.Controls.Add(label3, 0, 0);
            field.SetColumnSpan(label3, 2);
            comboBoxYz.Dock = DockStyle.Fill;
            comboBoxYz.Margin = new Padding(0, 2, 7, 1);
            GzPath.Dock = DockStyle.Fill;
            GzPath.Margin = new Padding(0, 2, 0, 1);
            field.Controls.Add(comboBoxYz, 0, 1);
            field.Controls.Add(GzPath, 1, 1);
            return field;
        }

        private Control BuildStampModeSection()
        {
            TableLayoutPanel section = CreateSectionTable();
            section.Padding = new Padding(0, 10, 0, 10);
            section.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;

            Label heading = CreateSectionHeading("盖章");
            section.Controls.Add(heading, 0, 0);

            TableLayoutPanel groups = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));

            groups.Controls.Add(BuildRadioColumn("骑缝章", new[]
            {
                CreateModeRadio("不加骑缝章", 1, seamModeRadios),
                CreateModeRadio("加盖骑缝章", 0, seamModeRadios),
                CreateModeRadio("单页骑缝章", 2, seamModeRadios),
                CreateModeRadio("双页骑缝章", 3, seamModeRadios),
                CreateModeRadio("随意骑缝章", 4, seamModeRadios)
            }), 0, 0);

            specifiedPageButton = new Button
            {
                AutoSize = true,
                Text = "指定范围页盖章",
                Margin = new Padding(0, 2, 0, 0)
            };
            specifiedPageButton.Click += SpecifiedPageButton_Click;
            groups.Controls.Add(BuildRadioColumn("页面盖章", new Control[]
            {
                CreateModeRadio("不盖页面章", 0, pageStampRadios),
                CreateModeRadio("手动点击盖章", CustomPlacementStampType, pageStampRadios),
                specifiedPageButton
            }), 1, 0);

            groups.Controls.Add(BuildRadioColumn("盖章模式", new[]
            {
                CreateModeRadio("合并（盖章不可编辑）", 1, outputModeRadios),
                CreateModeRadio("叠加（盖章浮于页面）", 0, outputModeRadios)
            }), 2, 0);
            section.Controls.Add(groups, 1, 0);

            // 按文字盖章：输入文字 → 自动定位并把印章中心对准文字放置到预览。
            // 第一行：标签 + 输入框 + 清除按钮（输入框占满剩余空间）；第二行：两个操作按钮。
            section.RowCount = 4;
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel autoInputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 14, 0, 0),   // 与上方保持距离，突出独立功能块
                Padding = new Padding(0)
            };
            autoInputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            autoInputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            Label autoLabel = new Label
            {
                Text = "按文字盖章",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),   // 与"骑缝章""页面盖章"等标签样式一致
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 3, 6, 0)
            };

            autoStampInput = new HistoryInputControl
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 14, 0)   // 右侧留白，与其他输入框右边界对齐
            };
            autoStampInput.HistoryProvider = LoadAutoStampHistory;

            autoInputRow.Controls.Add(autoLabel, 0, 0);
            autoInputRow.Controls.Add(autoStampInput, 1, 0);
            section.Controls.Add(autoInputRow, 1, 2);

            FlowLayoutPanel autoButtonRow = CreateFlowRow();
            autoButtonRow.Margin = new Padding(0, 6, 0, 0);

            autoStampButton = new Button
            {
                Text = "按文字放置印章",
                AutoSize = true,
                Margin = new Padding(0, 2, 6, 0)
            };
            autoStampButton.Click += AutoStampButton_Click;

            undoAutoStampButton = new Button
            {
                Text = "撤销放置",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0),
                Enabled = false
            };
            undoAutoStampButton.Click += UndoAutoStampButton_Click;

            autoButtonRow.Controls.Add(autoStampButton);
            autoButtonRow.Controls.Add(undoAutoStampButton);
            section.Controls.Add(autoButtonRow, 1, 3);
            return section;
        }

        private Control BuildSettingsSection()
        {
            TableLayoutPanel stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddSettingsSection(stack, 0, "数字签名", BuildSignatureSettings());
            AddSettingsSection(stack, 1, "印章设置", BuildStampSettings());
            AddSettingsSection(stack, 2, "骑缝章设置", BuildSeamSettings());
            AddSettingsSection(stack, 3, "其他", BuildOtherSettings());
            return stack;
        }

        private Control BuildSignatureSettings()
        {
            FlowLayoutPanel row = CreateSettingsFlowRow();
            comboQmtype.Width = 185;
            comboQmtype.Margin = new Padding(0, 1, 14, 0);
            row.Controls.Add(comboQmtype);
            row.Controls.Add(CreateInlineField(labelname, textname, 120));
            row.Controls.Add(CreateInlineField(labelpass, textpass, 120));
            return row;
        }

        private Control BuildStampSettings()
        {
            TableLayoutPanel rows = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            FlowLayoutPanel first = CreateSettingsFlowRow();
            first.Controls.Add(CreateInlineField(label13, textCC, 54, "mm"));
            first.Controls.Add(CreateInlineField(label10, textRotation, 54, "°"));
            first.Controls.Add(CreateInlineField(new Label { Text = "旋转处理", AutoSize = true }, comboBoxQB, 115));

            FlowLayoutPanel second = CreateSettingsFlowRow();
            second.Controls.Add(CreateInlineField(new Label { Text = "不透明度", AutoSize = true }, textOpacity, 54, "%"));
            second.Controls.Add(checkRandom);
            second.Controls.Add(cbxTransColor);
            second.Controls.Add(CreateInlineField(new Label { Text = "容差", AutoSize = true }, txtAllow, 54));
            rows.Controls.Add(first, 0, 0);
            rows.Controls.Add(second, 0, 1);
            return rows;
        }

        private Control BuildSeamSettings()
        {
            FlowLayoutPanel row = CreateSettingsFlowRow();
            row.Controls.Add(CreateInlineField(new Label { Text = "骑缝章位置", AutoSize = true }, comboBoxWZ, 80));
            row.Controls.Add(CreateInlineField(new Label { Text = "位置", AutoSize = true }, textWzbl, 54, "%"));
            row.Controls.Add(CreateInlineField(label14, textMaxFgs, 62));
            return row;
        }

        private Control BuildOtherSettings()
        {
            FlowLayoutPanel row = CreateSettingsFlowRow();
            row.Controls.Add(CreateInlineField(label15, textpdfpass, 260));
            return row;
        }

        private void BuildRightLayout(Control host)
        {
            RecreatePreviewToolbarControls();
            Panel border = CreateBorderPanel();
            host.Controls.Add(border);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            border.Controls.Add(layout);

            comboPDFlist.Dock = DockStyle.Fill;
            comboPDFlist.Visible = true;
            comboPDFlist.Margin = new Padding(0);
            layout.Controls.Add(comboPDFlist, 0, 0);

            operationHint = new Label
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(245, 250, 255),
                ForeColor = Color.FromArgb(34, 62, 91),
                Padding = new Padding(12, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 5, 0, 5),
                AutoEllipsis = false,   // 允许换行，长提示完整显示两行
                AutoSize = false
            };
            layout.Controls.Add(operationHint, 0, 1);

            previewToolbar = CreatePreviewToolbar();
            layout.Controls.Add(previewToolbar, 0, 2);

            previewViewport = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            previewViewport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            previewViewport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18F));

            PreviewStagePanel stage = new PreviewStagePanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(218, 220, 222),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0),
                TabStop = true,
                AutoScroll = false
            };
            stage.PreviewMouseWheel += PreviewStage_MouseWheel;
            previewStage = stage;
            previewStage.Controls.Add(pictureBox1);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.MouseEnter += PreviewPage_MouseEnter;
            pictureBox1.MouseDown += PreviewPage_MouseDown;
            pictureBox1.MouseMove += PreviewPage_MouseMove;
            pictureBox1.MouseUp += PreviewPage_MouseUp;
            pictureBox1.MouseCaptureChanged += PreviewPage_MouseCaptureChanged;
            AttachPreviewGestureHandlers(pictureBox2);
            previewPlaceholder = new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(64, 64, 64),
                Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Regular, GraphicsUnit.Point, 134),
                Text = "请上传 PDF 文件",
                TextAlign = ContentAlignment.MiddleCenter
            };
            pictureBox1.Controls.Add(previewPlaceholder);
            previewPlaceholder.BringToFront();
            previewStage.Resize += PreviewStage_Resize;
            previewViewport.Controls.Add(previewStage, 0, 0);

            previewPageScrollBar = new VScrollBar
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 1,
                LargeChange = 1,
                SmallChange = 1,
                Value = 1,
                TabStop = true,
                Enabled = false,
                Margin = new Padding(2, 0, 0, 0)
            };
            previewPageScrollBar.ValueChanged += PreviewPageScrollBar_ValueChanged;
            previewViewport.Controls.Add(previewPageScrollBar, 1, 0);
            layout.Controls.Add(previewViewport, 0, 3);
        }

        private void RecreatePreviewToolbarControls()
        {
            // These controls were originally created in the fixed Designer layout.
            // Recreate them for the adaptive toolbar so their native text rendering
            // and parent ownership are consistent with the new layout.
            comboPDFlist = new ComboBox
            {
                Name = "comboPDFlist",
                DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134)
            };
            comboPDFlist.SelectedIndexChanged += comboPDFlist_SelectedIndexChanged;

            buttonUp = CreatePreviewToolbarButton("上一页", null, buttonUp_Click);
            buttonUp.Name = "buttonUp";
            buttonNext = CreatePreviewToolbarButton("下一页", null, buttonNext_Click);
            buttonNext.Name = "buttonNext";

            currentPageInput = new TextBox { TextAlign = HorizontalAlignment.Center, Font = new Font("Microsoft YaHei UI", 9.5F), MaxLength = 6 };
            currentPageInput.KeyDown += CurrentPageInput_KeyDown;
            currentPageInputHost = CreateToolbarTextInputHost(currentPageInput, 58);
            totalPageLabel = new Label { TextAlign = ContentAlignment.MiddleLeft, Text = "/ 0 页", AutoSize = true, Font = new Font("Microsoft YaHei UI", 9.5F) };
            zoomOutButton = CreatePreviewToolbarButton("缩小", -1, ZoomButton_Click);
            zoomInButton = CreatePreviewToolbarButton("放大", 1, ZoomButton_Click);
            zoomInput = new TextBox { TextAlign = HorizontalAlignment.Center, Font = new Font("Microsoft YaHei UI", 9.5F), MaxLength = 3, Text = PreviewZoomPolicy.MinimumPercent.ToString() };
            zoomInput.KeyPress += ZoomInput_KeyPress;
            zoomInput.TextChanged += ZoomInput_TextChanged;
            zoomInput.KeyDown += ZoomInput_KeyDown;
            zoomInput.Leave += ZoomInput_Leave;
            zoomInputHost = CreateToolbarTextInputHost(zoomInput, 48);
            zoomPercentLabel = new Label { Text = "%", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9.5F) };
            singlePageButton = CreatePreviewToolbarButton("单页视图", PreviewViewMode.SinglePage, PreviewViewModeButton_Click);
            scrollViewButton = CreatePreviewToolbarButton("放大视图", PreviewViewMode.Scroll, PreviewViewModeButton_Click);
            SetToolbarButtonSelected(singlePageButton, true);
        }

        private TableLayoutPanel CreatePreviewToolbar()
        {
            TableLayoutPanel toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 13,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 21F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 21F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            AddToolbarControl(toolbar, buttonUp, 0, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, buttonNext, 1, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, currentPageInputHost, 2, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, totalPageLabel, 3, new Padding(0));
            AddToolbarControl(toolbar, CreateToolbarSeparator(), 4, new Padding(10, 0, 10, 0));
            AddToolbarControl(toolbar, zoomOutButton, 5, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, zoomInButton, 6, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, zoomInputHost, 7, new Padding(0, 0, 4, 0));
            AddToolbarControl(toolbar, zoomPercentLabel, 8, new Padding(0));
            AddToolbarControl(toolbar, CreateToolbarSeparator(), 9, new Padding(10, 0, 10, 0));
            AddToolbarControl(toolbar, singlePageButton, 10, new Padding(0, 0, 6, 0));
            AddToolbarControl(toolbar, scrollViewButton, 11, new Padding(0));
            return toolbar;
        }

        private static void AddToolbarControl(TableLayoutPanel toolbar, Control control, int column, Padding margin)
        {
            control.Anchor = AnchorStyles.Left;
            control.Margin = margin;
            toolbar.Controls.Add(control, column, 0);
        }

        private static Panel CreateToolbarTextInputHost(TextBox textBox, int width)
        {
            Panel host = new Panel
            {
                Width = width,
                Height = 30,
                BackColor = Color.White,
                Padding = new Padding(0),
                TabStop = false
            };
            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = Color.White;
            textBox.AutoSize = true;
            host.Controls.Add(textBox);

            Action layoutInput = () =>
            {
                textBox.Width = Math.Max(1, host.ClientSize.Width - 8);
                textBox.Location = new Point(4, Math.Max(1, (host.ClientSize.Height - textBox.PreferredHeight) / 2));
            };
            host.Resize += (sender, args) => layoutInput();
            host.Click += (sender, args) => textBox.Focus();
            textBox.Enter += (sender, args) => host.Invalidate();
            textBox.Leave += (sender, args) => host.Invalidate();
            host.Paint += (sender, args) =>
            {
                Color borderColor = textBox.Focused
                    ? Color.FromArgb(55, 125, 220)
                    : Color.FromArgb(150, 150, 150);
                using (Pen pen = new Pen(borderColor, 1F))
                {
                    Rectangle border = new Rectangle(0, 0, host.ClientSize.Width - 1, host.ClientSize.Height - 1);
                    args.Graphics.DrawRectangle(pen, border);
                }
            };
            layoutInput();
            return host;
        }

        private static Panel CreateToolbarSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 20,
                BackColor = Color.FromArgb(185, 185, 185),
                TabStop = false
            };
        }

        private Button CreatePreviewToolbarButton(string text, object tag, EventHandler handler)
        {
            Font buttonFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            int regularWidth = TextRenderer.MeasureText(text, buttonFont).Width;
            int boldWidth;
            using (Font boldFont = new Font(buttonFont, FontStyle.Bold))
            {
                boldWidth = TextRenderer.MeasureText(text, boldFont).Width;
            }
            Button button = new Button
            {
                Text = text,
                Tag = tag,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderColor = Color.FromArgb(160, 160, 160), BorderSize = 1 },
                BackColor = SystemColors.Control,
                UseVisualStyleBackColor = false,
                Font = buttonFont,
                Height = 30,
                Width = Math.Max(text == "上一页" || text == "下一页" ? 68 : 56, Math.Max(regularWidth, boldWidth) + 24),
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = true
            };
            button.Click += handler;
            return button;
        }

        private static void SetToolbarButtonSelected(Button button, bool selected)
        {
            if (button == null) return;
            button.FlatAppearance.BorderColor = selected
                ? Color.FromArgb(0, 120, 215)
                : Color.FromArgb(160, 160, 160);
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
            button.BackColor = selected
                ? Color.FromArgb(225, 239, 255)
                : SystemColors.Control;
            button.ForeColor = selected
                ? Color.FromArgb(0, 72, 140)
                : SystemColors.ControlText;
            button.FlatAppearance.MouseOverBackColor = selected
                ? Color.FromArgb(214, 233, 255)
                : Color.FromArgb(235, 242, 250);
            button.FlatAppearance.MouseDownBackColor = selected
                ? Color.FromArgb(198, 224, 252)
                : Color.FromArgb(218, 232, 247);

            FontStyle targetStyle = selected ? FontStyle.Bold : FontStyle.Regular;
            if (button.Font.Style != targetStyle)
            {
                Font oldFont = button.Font;
                button.Font = new Font(oldFont, targetStyle);
                oldFont.Dispose();
            }
        }

        private void ConfigureModeSynchronization()
        {
            comboType.Visible = false;
            comboQfz.Visible = false;
            comboYz.Visible = false;
            comboDJ.Visible = false;
            comboBoxPages.Visible = false;
            checkMultiple.Visible = false;
            checkMultiple.Checked = true;
            labelPage.Visible = false;
            textPx.Visible = false;
            textPy.Visible = false;
            label6.Visible = false;
            label7.Visible = false;
            label8.Visible = false;
            label9.Visible = false;
            label12.Visible = false;

            comboType.SelectedIndexChanged += HiddenModeCombo_SelectedIndexChanged;
            comboQfz.SelectedIndexChanged += HiddenModeCombo_SelectedIndexChanged;
            comboYz.SelectedIndexChanged += HiddenModeCombo_SelectedIndexChanged;
            comboDJ.SelectedIndexChanged += HiddenModeCombo_SelectedIndexChanged;
        }

        private RadioButton CreateModeRadio(string text, int value, IDictionary<RadioButton, int> map)
        {
            RadioButton radio = new RadioButton
            {
                AutoSize = true,
                Text = text,
                Margin = new Padding(0, 1, 8, 1),
                Tag = value
            };
            map[radio] = value;
            radio.CheckedChanged += VisibleModeRadio_CheckedChanged;
            return radio;
        }

        private Control BuildRadioColumn(string title, IEnumerable<Control> controls)
        {
            FlowLayoutPanel column = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 8, 0)
            };
            column.Controls.Add(new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = title, Margin = new Padding(0, 0, 0, 3) });
            foreach (Control control in controls)
            {
                column.Controls.Add(control);
            }
            return column;
        }

        private TableLayoutPanel CreateSectionTable(bool separateRows = false)
        {
            TableLayoutPanel section = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = SystemColors.Control
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.Paint += (sender, args) => DrawSectionDividers(section, args, separateRows);
            return section;
        }

        private static void DrawSectionDividers(TableLayoutPanel section, PaintEventArgs args, bool separateRows)
        {
            using (Pen pen = new Pen(Color.FromArgb(205, 208, 212)))
            {
                if (separateRows)
                {
                    int y = 0;
                    foreach (int rowHeight in section.GetRowHeights())
                    {
                        y += rowHeight;
                        args.Graphics.DrawLine(pen, 0, Math.Max(0, y - 1), section.ClientSize.Width, Math.Max(0, y - 1));
                    }
                }
                else
                {
                    int y = Math.Max(0, section.ClientSize.Height - 1);
                    args.Graphics.DrawLine(pen, 0, y, section.ClientSize.Width, y);
                }
            }
        }

        private Label CreateSectionHeading(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0)
            };
        }

        private FlowLayoutPanel CreateFlowRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private FlowLayoutPanel CreateSettingsFlowRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private Control CreateInlineField(Label label, Control field, int width, string unit = null)
        {
            FlowLayoutPanel group = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 14, 3)
            };
            label.AutoSize = true;
            label.Margin = new Padding(0, 5, 5, 0);
            field.Width = width;
            field.Margin = new Padding(0, 1, 0, 0);
            group.Controls.Add(label);
            group.Controls.Add(field);
            if (!string.IsNullOrEmpty(unit))
            {
                group.Controls.Add(new Label { AutoSize = true, Text = unit, Margin = new Padding(4, 5, 0, 0) });
            }
            return group;
        }

        private void AddSettingsRow(TableLayoutPanel section, int row, string heading, Control content)
        {
            Label label = CreateSectionHeading(heading);
            label.Padding = new Padding(0, 5, 0, 5);
            section.Controls.Add(label, 0, row);
            section.Controls.Add(CenterVertically(content), 1, row);
        }

        private void AddSettingsSection(TableLayoutPanel stack, int row, string heading, Control content)
        {
            TableLayoutPanel section = CreateSectionTable();
            section.Dock = DockStyle.Top;
            section.AutoSize = true;
            section.Margin = new Padding(0);
            section.Padding = new Padding(0, 10, 0, 10);

            section.Controls.Add(CreateSectionHeading(heading), 0, 0);
            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.Margin = new Padding(0);
            section.Controls.Add(content, 1, 0);
            stack.Controls.Add(section, 0, row);
        }

        private Control CreateSettingsContainer(string heading, Control content, bool drawDivider)
        {
            TableLayoutPanel container = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0, 10, 0, 10),
                BackColor = SystemColors.Control
            };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114F));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label label = CreateSectionHeading(heading);
            label.Padding = new Padding(0);
            label.Dock = DockStyle.Fill;
            container.Controls.Add(label, 0, 0);

            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.Margin = new Padding(0);
            container.Controls.Add(content, 1, 0);

            if (drawDivider)
            {
                container.Paint += (sender, args) =>
                {
                    using (Pen pen = new Pen(Color.FromArgb(205, 208, 212)))
                    {
                        int y = container.ClientSize.Height - 1;
                        args.Graphics.DrawLine(pen, 0, y, container.ClientSize.Width, y);
                    }
                };
            }
            return container;
        }

        private static Control CenterVertically(Control content)
        {
            TableLayoutPanel wrapper = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 4)
            };
            wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Dock = DockStyle.Top;
            content.AutoSize = true;
            content.Margin = new Padding(0);
            wrapper.Controls.Add(content, 0, 0);
            return wrapper;
        }

        private Panel CreateBorderPanel()
        {
            Panel panel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Control,
                Margin = new Padding(0)
            };
            return panel;
        }

        private void VisibleModeRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (synchronizingModeControls || !(sender is RadioButton radio) || !radio.Checked)
            {
                return;
            }

            if (fileModeRadios.TryGetValue(radio, out int fileMode))
            {
                comboType.SelectedIndex = fileMode;
                comboType_SelectionChangeCommitted(comboType, EventArgs.Empty);
            }
            else if (seamModeRadios.TryGetValue(radio, out int seamMode))
            {
                comboQfz.SelectedIndex = seamMode;
                comboQfz_SelectionChangeCommitted(comboQfz, EventArgs.Empty);
            }
            else if (pageStampRadios.TryGetValue(radio, out int pageMode))
            {
                comboYz.SelectedIndex = pageMode;
                yzType = pageMode;
                lastCommittedYzType = pageMode;
                UpdatePlacementOperationHint();
            }
            else if (outputModeRadios.TryGetValue(radio, out int outputMode))
            {
                comboDJ.SelectedIndex = outputMode;
            }
        }

        private void HiddenModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            SynchronizeVisibleModeControls();
        }

        private void SynchronizeVisibleModeControls()
        {
            if (synchronizingModeControls)
            {
                return;
            }

            synchronizingModeControls = true;
            SetChecked(fileModeRadios, comboType.SelectedIndex);
            SetChecked(seamModeRadios, comboQfz.SelectedIndex);
            SetChecked(pageStampRadios, comboYz.SelectedIndex == SpecifiedPageStampType ? CustomPlacementStampType : comboYz.SelectedIndex);
            SetChecked(outputModeRadios, comboDJ.SelectedIndex);
            synchronizingModeControls = false;
        }

        private static void SetChecked(IDictionary<RadioButton, int> radios, int value)
        {
            foreach (KeyValuePair<RadioButton, int> item in radios)
            {
                item.Key.Checked = item.Value == value;
            }
        }

        private void SpecifiedPageButton_Click(object sender, EventArgs e)
        {
            BeginSpecifiedPageStampMode();
        }

        private void WhiteBackgroundOption_CheckedChanged(object sender, EventArgs e)
        {
            UpdateWhiteBackgroundOptionState();
            ClearTransparentStampCache();
            RefreshPreviewOverlay();
        }

        private void UpdateWhiteBackgroundOptionState()
        {
            txtAllow.Enabled = WhiteBackgroundOptionPolicy.IsToleranceEnabled(cbxTransColor.Checked);
            tip.SetToolTip(cbxTransColor, "将印章图片中的白色和接近白色像素变为透明。");
            tip.SetToolTip(txtAllow, "容差范围0-50；数值越大，去除的近白色范围越宽，过大可能影响浅色细节。");
        }

        private async void CurrentPageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            if (!PageNavigationPolicy.TryParse(currentPageInput.Text, imgPageCount, out int page, out string error))
            {
                SetOperationHint(error, true);
                currentPageInput.SelectAll();
                return;
            }

            imgStartPage = page;
            await viewPDFPage();
            SetOperationHint("已跳转到第 " + page + " 页。", false);
        }

        private void PreviewViewModeButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null || button.Tag == null) return;
            previewViewMode = (PreviewViewMode)button.Tag;
            if (previewViewMode == PreviewViewMode.SinglePage)
            {
                previewZoomPercent = PreviewZoomPolicy.MinimumPercent;
                if (zoomInput != null) zoomInput.Text = previewZoomPercent.ToString();
                if (zoomOutButton != null) zoomOutButton.Enabled = false;
                if (zoomInButton != null) zoomInButton.Enabled = true;
            }
            SetToolbarButtonSelected(singlePageButton, previewViewMode == PreviewViewMode.SinglePage);
            SetToolbarButtonSelected(scrollViewButton, previewViewMode == PreviewViewMode.Scroll);
            UpdatePreviewViewportColumns();
            LayoutPreviewPage();
            UpdatePlacementOperationHint();
        }

        private void ZoomButton_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            int direction = button == null || button.Tag == null ? 0 : Convert.ToInt32(button.Tag);
            ApplyPreviewZoom(PreviewZoomPolicy.Step(previewZoomPercent, direction));
        }

        private void ZoomInput_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back && e.KeyChar != (char)Keys.Delete)
            {
                e.Handled = true;
            }
        }

        private void ZoomInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyZoomInput();
            }
        }

        private void ZoomInput_TextChanged(object sender, EventArgs e)
        {
            if (zoomInput == null) return;
            string filtered = PreviewValueSanitizer.KeepDigitsOnly(zoomInput.Text);
            if (filtered == zoomInput.Text) return;
            int selectionStart = zoomInput.SelectionStart;
            zoomInput.Text = filtered;
            zoomInput.SelectionStart = Math.Min(selectionStart, zoomInput.Text.Length);
        }

        private void ZoomInput_Leave(object sender, EventArgs e)
        {
            ApplyZoomInput();
        }

        private void ApplyZoomInput()
        {
            if (!PreviewZoomPolicy.TryParse(zoomInput == null ? string.Empty : zoomInput.Text, out int percent, out string error))
            {
                SetOperationHint(error, true);
                if (zoomInput != null) zoomInput.Text = previewZoomPercent.ToString();
                return;
            }
            ApplyPreviewZoom(percent);
        }

        private void ApplyPreviewZoom(int percent)
        {
            previewZoomPercent = PreviewZoomPolicy.Clamp(percent);
            if (zoomInput != null) zoomInput.Text = previewZoomPercent.ToString();
            if (zoomOutButton != null) zoomOutButton.Enabled = previewZoomPercent > PreviewZoomPolicy.MinimumPercent;
            if (zoomInButton != null) zoomInButton.Enabled = previewZoomPercent < PreviewZoomPolicy.MaximumPercent;
            if (previewZoomPercent > PreviewZoomPolicy.MinimumPercent && previewViewMode == PreviewViewMode.SinglePage)
            {
                previewViewMode = PreviewViewMode.Scroll;
                SetToolbarButtonSelected(singlePageButton, false);
                SetToolbarButtonSelected(scrollViewButton, true);
                UpdatePreviewViewportColumns();
            }
            LayoutPreviewPage();
            UpdatePlacementOperationHint();
        }

        private void ResetPreviewSessionState()
        {
            previewZoomPercent = PreviewZoomPolicy.MinimumPercent;
            previewViewMode = PreviewViewMode.SinglePage;
            if (zoomInput != null) zoomInput.Text = previewZoomPercent.ToString();
            SetToolbarButtonSelected(singlePageButton, true);
            SetToolbarButtonSelected(scrollViewButton, false);
            if (zoomOutButton != null) zoomOutButton.Enabled = false;
            if (zoomInButton != null) zoomInButton.Enabled = true;
            if (previewStage != null) previewStage.Cursor = Cursors.Default;
            UpdatePreviewViewportColumns();
        }

        private void UpdatePreviewViewportColumns()
        {
            if (previewViewport == null || previewViewport.ColumnStyles.Count < 2) return;
            previewViewport.ColumnStyles[1].Width = previewViewMode == PreviewViewMode.SinglePage ? 18F : 0F;
        }

        private void PreviewPage_MouseEnter(object sender, EventArgs e)
        {
            previewStage?.Focus();
        }

        private void PreviewPage_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || previewStage == null)
            {
                return;
            }

            previewPointerPressed = true;
            previewPanActive = false;
            previewPointerStartPoint = Control.MousePosition;
            previewPanStartOffset = new Point(-previewStage.AutoScrollPosition.X, -previewStage.AutoScrollPosition.Y);
            previewPointerCaptureOwner = sender as Control;
            if (previewPointerCaptureOwner != null)
            {
                previewPointerCaptureOwner.Capture = true;
            }
        }

        private void PreviewPage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!previewPointerPressed || previewStage == null) return;
            Point current = Control.MousePosition;
            if (!previewPanActive && PreviewGesturePolicy.IsDrag(previewPointerStartPoint, current, SystemInformation.DragSize))
            {
                previewPanActive = true;
                previewStage.Cursor = Cursors.SizeAll;
            }
            if (!previewPanActive) return;

            int nextX = Math.Max(0, previewPanStartOffset.X - (current.X - previewPointerStartPoint.X));
            int nextY = Math.Max(0, previewPanStartOffset.Y - (current.Y - previewPointerStartPoint.Y));
            previewStage.AutoScrollPosition = new Point(nextX, nextY);
        }

        private void PreviewPage_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            bool wasPressed = previewPointerPressed;
            bool wasPan = previewPanActive;
            Control source = sender as Control;
            previewPointerPressed = false;
            previewPanActive = false;
            Control captureOwner = previewPointerCaptureOwner;
            previewPointerCaptureOwner = null;
            if (captureOwner != null)
            {
                captureOwner.Capture = false;
            }
            if (previewStage != null) previewStage.Cursor = Cursors.Default;

            if (wasPressed && !wasPan && source == pictureBox1)
            {
                AddPreviewStampAtPoint(pictureBox1.PointToClient(Control.MousePosition));
            }
        }

        private void PreviewPage_MouseCaptureChanged(object sender, EventArgs e)
        {
            Control source = sender as Control;
            if (source != null && source.Capture) return;
            if (!previewPointerPressed || source != previewPointerCaptureOwner) return;
            previewPointerPressed = false;
            previewPanActive = false;
            previewPointerCaptureOwner = null;
            if (previewStage != null) previewStage.Cursor = Cursors.Default;
        }

        private void AttachPreviewGestureHandlers(Control control)
        {
            if (control == null || control == pictureBox1) return;
            control.MouseEnter += PreviewPage_MouseEnter;
            control.MouseDown += PreviewPage_MouseDown;
            control.MouseMove += PreviewPage_MouseMove;
            control.MouseUp += PreviewPage_MouseUp;
            control.MouseCaptureChanged += PreviewPage_MouseCaptureChanged;
        }

        private void PreviewStage_MouseWheel(object sender, MouseEventArgs e)
        {
            if (previewStage == null ||
                !previewStage.RectangleToScreen(previewStage.ClientRectangle).Contains(Control.MousePosition) ||
                viewPdfimgs == null ||
                imgPageCount < 1 ||
                e.Delta == 0)
            {
                return;
            }

            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                ApplyPreviewZoom(PreviewZoomPolicy.StepByWheel(previewZoomPercent, e.Delta > 0 ? 1 : -1));
                return;
            }

            if (previewViewMode == PreviewViewMode.Scroll)
            {
                return;
            }

            previewWheelDeltaRemainder += e.Delta;
            int threshold = SystemInformation.MouseWheelScrollDelta;
            if (Math.Abs(previewWheelDeltaRemainder) < threshold)
            {
                return;
            }

            int wheelStep = Math.Sign(previewWheelDeltaRemainder) * threshold;
            previewWheelDeltaRemainder -= wheelStep;
            int targetPage = PageNavigationPolicy.MoveByWheel(imgStartPage, imgPageCount, wheelStep);
            if (targetPage != imgStartPage)
            {
                NavigateToPreviewPage(targetPage);
            }
        }

        private void PreviewNavigation_KeyDown(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox || ActiveControl is ComboBox) return;
            if (viewPdfimgs == null || imgPageCount < 1) return;
            if (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.Up || e.KeyCode == Keys.Left)
            {
                NavigateToPreviewPage(Math.Max(1, imgStartPage - 1));
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Down || e.KeyCode == Keys.Right)
            {
                NavigateToPreviewPage(Math.Min(imgPageCount, imgStartPage + 1));
                e.SuppressKeyPress = true;
            }
        }

        private void PreviewPageScrollBar_ValueChanged(object sender, EventArgs e)
        {
            if (synchronizingPageScrollBar || viewPdfimgs == null || imgPageCount < 1)
            {
                return;
            }

            int targetPage = PageNavigationPolicy.NormalizeScrollValue(previewPageScrollBar.Value, imgPageCount);
            if (targetPage != imgStartPage)
            {
                NavigateToPreviewPage(targetPage);
            }
        }

        private void NavigateToPreviewPage(int page)
        {
            if (!PageNavigationPolicy.TryParse(page.ToString(), imgPageCount, out int targetPage, out _))
            {
                return;
            }

            imgStartPage = targetPage;
            _ = viewPDFPage();
        }

        private void SetOperationHint(string text, bool isError = false)
        {
            if (operationHint == null)
            {
                return;
            }

            if (operationHint.InvokeRequired)
            {
                operationHint.BeginInvoke(new Action(() => SetOperationHint(text, isError)));
                return;
            }

            operationHint.Text = text;
            operationHint.ForeColor = isError ? Color.Firebrick : Color.FromArgb(34, 62, 91);
            operationHint.BackColor = isError ? Color.FromArgb(255, 244, 244) : Color.FromArgb(245, 250, 255);
        }

        private void UpdatePlacementOperationHint()
        {
            bool hasPreview = !string.IsNullOrWhiteSpace(previewPath) && viewPdfimgs != null;
            bool directoryMode = comboType.SelectedIndex == 0;
            bool directorySelected = directoryMode && Directory.Exists(pathText.Text);
            bool specifiedPending = specifiedRangeFirstClickPending && specifiedPageRange != null;
            bool pageStampEnabled = comboYz.SelectedIndex == CustomPlacementStampType;
            SetOperationHint(PreviewOperationHintPolicy.Build(
                hasPreview,
                directoryMode,
                directorySelected,
                specifiedPending,
                pageStampEnabled,
                previewViewMode));
        }

        private void Form1_InitialLayout(object sender, EventArgs e)
        {
            ApplyMainSplitLayout();
            if (string.IsNullOrWhiteSpace(previewPath) || viewPdfimgs == null)
            {
                SetPreviewPlaceholderText(GetIdlePreviewPlaceholderText());
                SetPreviewPlaceholderVisible(true);
            }
            LayoutPreviewPage();
        }

        private void SetPreviewPlaceholderVisible(bool visible)
        {
            if (previewPlaceholder == null)
            {
                return;
            }

            previewPlaceholder.Visible = visible;
            if (visible)
            {
                previewPlaceholder.BringToFront();
            }
        }

        private void SetPreviewPlaceholderText(string text)
        {
            if (previewPlaceholder != null)
            {
                previewPlaceholder.Text = text;
            }
        }

        private void Form1_AdaptiveResize(object sender, EventArgs e)
        {
            ApplyMainSplitLayout();
            if (leftLayout != null)
            {
                leftLayout.PerformLayout();
                leftLayout.Invalidate();
            }
            LayoutPreviewPage();
        }

        private void ApplyMainSplitLayout()
        {
            if (mainSplit == null || mainSplit.ClientSize.Width <= 0)
            {
                return;
            }

            const int leftMinimum = 500;
            const int rightMinimum = 560;
            int availableWidth = mainSplit.ClientSize.Width - mainSplit.SplitterWidth;
            if (availableWidth < leftMinimum + rightMinimum)
            {
                return;
            }

            int preferred = Convert.ToInt32(mainSplit.ClientSize.Width * 0.45);
            int maximumLeftWidth = availableWidth - rightMinimum;
            int splitterDistance = Math.Max(leftMinimum, Math.Min(preferred, maximumLeftWidth));

            // WinForms validates the current splitter position while each minimum is assigned.
            // Establish a valid position first, then apply both panel constraints.
            mainSplit.Panel1MinSize = 0;
            mainSplit.Panel2MinSize = 0;
            mainSplit.SplitterDistance = splitterDistance;
            mainSplit.Panel1MinSize = leftMinimum;
            mainSplit.Panel2MinSize = rightMinimum;
        }

        private void PreviewStage_Resize(object sender, EventArgs e)
        {
            LayoutPreviewPage();
        }

        private void LayoutPreviewPage()
        {
            if (previewStage == null || pictureBox1 == null || pictureBox1.Image == null)
            {
                return;
            }

            Size fitSize = PreviewViewportLayout.Calculate(
                previewStage.ClientSize.Width,
                previewStage.ClientSize.Height,
                pictureBox1.Image.Width,
                pictureBox1.Image.Height,
                18);
            if (fitSize.Width <= 0 || fitSize.Height <= 0)
            {
                return;
            }

            Point oldCenter = Point.Empty;
            Size oldPageSize = pictureBox1.Size;
            bool preserveCenter = previewViewMode == PreviewViewMode.Scroll && pictureBox1.Width > 0 && pictureBox1.Height > 0;
            if (preserveCenter)
            {
                oldCenter = new Point(
                    -previewStage.AutoScrollPosition.X + previewStage.ClientSize.Width / 2,
                    -previewStage.AutoScrollPosition.Y + previewStage.ClientSize.Height / 2);
            }

            Size size = previewViewMode == PreviewViewMode.Scroll
                ? new Size(Math.Max(1, fitSize.Width * previewZoomPercent / 100), Math.Max(1, fitSize.Height * previewZoomPercent / 100))
                : fitSize;

            previewStage.AutoScroll = previewViewMode == PreviewViewMode.Scroll;
            previewStage.AutoScrollMinSize = previewViewMode == PreviewViewMode.Scroll ? size : Size.Empty;
            pictureBox1.Size = size;
            pictureBox1.Location = new Point(
                Math.Max(0, (previewStage.ClientSize.Width - size.Width) / 2),
                Math.Max(0, (previewStage.ClientSize.Height - size.Height) / 2));

            if (preserveCenter && oldCenter != Point.Empty)
            {
                double ratioX = oldCenter.X / (double)Math.Max(1, oldPageSize.Width);
                double ratioY = oldCenter.Y / (double)Math.Max(1, oldPageSize.Height);
                int targetX = Math.Max(0, (int)(ratioX * pictureBox1.Width - previewStage.ClientSize.Width / 2));
                int targetY = Math.Max(0, (int)(ratioY * pictureBox1.Height - previewStage.ClientSize.Height / 2));
                previewStage.AutoScrollPosition = new Point(targetX, targetY);
            }
            RefreshPreviewOverlays();
            UpdatePreviewPageScrollBar();
        }

        private void UpdatePreviewPageScrollBar()
        {
            if (previewPageScrollBar == null)
            {
                return;
            }

            synchronizingPageScrollBar = true;
            try
            {
                int pageCount = Math.Max(1, imgPageCount);
                previewPageScrollBar.Minimum = 1;
                previewPageScrollBar.Maximum = pageCount;
                previewPageScrollBar.LargeChange = 1;
                previewPageScrollBar.SmallChange = 1;
                previewPageScrollBar.Visible = previewViewMode == PreviewViewMode.SinglePage;
                previewPageScrollBar.Enabled = previewViewMode == PreviewViewMode.SinglePage && imgPageCount > 1;
                previewPageScrollBar.Value = PageNavigationPolicy.NormalizeScrollValue(imgStartPage, pageCount);
            }
            finally
            {
                synchronizingPageScrollBar = false;
            }
        }
    }
}
