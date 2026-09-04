using iTextSharp.text;
using iTextSharp.text.exceptions;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.security;
using PDFQFZ.Library;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace PDFQFZ
{
    public partial class Form1 : Form
    {
        int fw;  //Form Width
        int fh;  //Form Height
        int collapsedClientWidth;
        int expandedClientWidth;
        int imgStartPage = 0;
        int imgPageCount=0;
        const int CustomPlacementStampType = 1;
        const int SpecifiedPageStampType = 2;

        ToolTip tip = new ToolTip();  //容差的ToolTip显示

        string certDefaultPath = $@"{Application.StartupPath}\pdfqfz.pfx";//证书默认存放地址

        string yzLog = $@"{Application.StartupPath}\yz.log";//获取印章记录

        //@loquat 20250920 增加全局参数
        int fixType;
        string fixStr = "已盖章";
        string fixStr2 = "加密";
        string signBuiltInPath;
        string signBuiltInPass;
        string signCustomPath;
        string signCustomPass;
        string section = "config";

        DataTable dt = new DataTable();       //PDF列表
        DataTable dtPages = new DataTable();  //PDF文件页列表
        readonly StampPlacementCollection stampPlacements = new StampPlacementCollection();
        readonly Dictionary<int, PictureBox> previewStampOverlays = new Dictionary<int, PictureBox>();
        DataTable dtYz = new DataTable();     //印章图片列表
        List<string> commandLinePdfFiles = new List<string>();
        string sourcePath = "";
        string outputPath = "";
        string imgPath = "";
        string previewPath = "";
        string signText = "";
        string password = "";
        string pdfpassword = "";
        //这里设置的默认值是不是没有意义
        int wjType = StartupModeDefaults.FileMode;          //首次启动：文件模式
        int qfzType = StartupModeDefaults.NoSeamStamp;      //首次启动：不加骑缝章
        int yzType = StartupModeDefaults.NoPageStamp;       //首次启动：不盖页面章
        int djType = StartupModeDefaults.OverlayOutput;     //首次启动：合并
        int qmType = 0;     //签名类型
        int wzType = 3;     //骑缝章位置类型
        int yzIndex = -1;   //选择的印章索引
        int qbflag = 0;     //是否切边标记
        int size = 40;      //尺寸，单位mm，loquat改的自定义签章部分用作宽度，高度维持原图比例
        int rotation = 0;   //旋转角
        int opacity = 60;   //透明度预设
        int wz = 50;        //骑缝章位置
        int yzr = 36;
        int maxfgs = 20;   //骑缝章最大分割数
        Bitmap imgYz = null;   //签章图片对象
        Bitmap[] viewPdfimgs = null;         //预览的pdf列表， viewPdfimgs[imgStartPage-1]表示当前在预览的图片
        PageCache<Bitmap> previewPageCache = null;
        IPdfDocumentRenderer previewPdfRenderer = null;
        // 预览渲染分辨率（DPI）：72 太低导致预览文字模糊；随系统屏幕 DPI 渲染更清晰，并设上限避免大文件内存过高
        readonly int previewRenderDpi = Math.Max(96, Math.Min(144, (int)Math.Round(Graphics.FromHwnd(IntPtr.Zero).DpiX)));
        string previewRenderPath = "";
        Bitmap cachedTransparentStamp = null;
        string cachedTransparentStampKey = "";
        readonly object transparentStampCacheSync = new object();
        readonly PreviewOverlayRequestGate previewOverlayRequestGate = new PreviewOverlayRequestGate();
        X509Certificate2 cert = null;        //证书
        float xzbl = 1f;                     //旋转图片导致长宽变化的比例
        //后台盖章时使用的 UI 值快照（避免后台线程跨线程访问控件）
        float uiTextPxValue = 50f;
        float uiTextPyValue = 50f;
        bool uiCheckRandom = false;

        //按文字盖章相关
        HistoryInputControl autoStampInput = null;
        System.Windows.Forms.Button autoStampButton = null;
        System.Windows.Forms.Button undoAutoStampButton = null;
        // 按文字放置操作栈（用于"撤销放置"逐步撤销）；每个操作记录关键词与批次
        private readonly List<AutoStampOperation> autoStampOperations = new List<AutoStampOperation>();

        //按文字盖章历史记忆相关
        const string AutoStampHistoryIniKey = "autoStampHistory";
        const int MaxAutoStampHistory = 10;
        const string AutoStampHistorySeparator = "||";
        private static readonly Random randomGenerator = new Random();
        private string strIniFilePath = $@"{Application.StartupPath}\config.ini";//获取INI文件路径
        //private bool isSelectionCommitted = false; // 文档预览下拉列表框事件标记位
        private CancellationTokenSource cancellationTokenSource;//处理文件进度取消标记
        private CancellationTokenSource cts;//PDF文件异步处理取消标记
        private CancellationTokenSource previewOverlayCts;//印章预览异步取消标记
        private PageRange specifiedPageRange;
        private bool specifiedRangeFirstClickPending;
        private int activeSpecifiedBatchId;
        private int lastCommittedYzType;
        private bool suppressYzSelectionChange;
        // 保存/生成阶段的不确定进度提示：操作提示区两行文字动画（省略号增减）+ 实时计时
        private DateTime savingStartTime;
        private volatile bool savingIndicatorActive;
        private int savingDotPhase;
        private DateTime lastSavingTick;

        public Form1(string[] args)
        {
            InitializeComponent();
            InitializeAdaptiveLayout();
            this.KeyDown += Esc_Key_Down;//接受键盘ESC响应
            this.FormClosing += Form1_FormClosing;//记住窗口大小位置
            // 在这里处理命令行参数
            commandLinePdfFiles = CommandLineFileLoader.CollectExistingPdfFiles(args);
            if (commandLinePdfFiles.Count > 0)
            {
                sourcePath = string.Join(",", commandLinePdfFiles);
                outputPath = System.IO.Path.GetDirectoryName(commandLinePdfFiles[0]);
            }
        }


        //关闭时记住窗口大小与位置
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
                int saveW, saveH, saveX, saveY;
                if (this.WindowState == FormWindowState.Maximized)
                {
                    saveW = this.RestoreBounds.Width;
                    saveH = this.RestoreBounds.Height;
                    saveX = this.RestoreBounds.Left;
                    saveY = this.RestoreBounds.Top;
                }
                else if (this.WindowState == FormWindowState.Normal)
                {
                    saveW = this.Width;
                    saveH = this.Height;
                    saveX = this.Left;
                    saveY = this.Top;
                }
                else
                {
                    return;
                }
                iniFileHelper.WriteIniInt(section, "windowWidth", saveW);
                iniFileHelper.WriteIniInt(section, "windowHeight", saveH);
                iniFileHelper.WriteIniInt(section, "windowLeft", saveX);
                iniFileHelper.WriteIniInt(section, "windowTop", saveY);
            }
            catch
            {
                //保存窗口状态失败不影响退出
            }
        }

        //程序加载
        private void Form1_Load(object sender, EventArgs e)
        {
            if (File.Exists(strIniFilePath))//读取时先要判读INI文件是否存在
            {
                IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
                string section = "config";
                string WjType = iniFileHelper.ContentValue(section, "wjType");//文件类型
                string QfzType = iniFileHelper.ContentValue(section, "qfzType");//骑缝章类型
                string YzType = iniFileHelper.ContentValue(section, "yzType");//印章类型
                string YzTypeVersion = iniFileHelper.ContentValue(section, "yzTypeVersion");//印章类型索引版本
                string QmType = iniFileHelper.ContentValue(section, "qmType");//签名类型 0-不签名 1-内置签名 2-自定义签名
                string WzType = iniFileHelper.ContentValue(section, "wzType");//骑缝章位置类型
                string Qbflag = iniFileHelper.ContentValue(section, "qbflag");//是否切边标记
                string Size = iniFileHelper.ContentValue(section, "size");//印章尺寸
                string Rotation = iniFileHelper.ContentValue(section, "rotation");//旋转角度
                string Opacity = iniFileHelper.ContentValue(section, "opacity");//不透明度
                string Wz = iniFileHelper.ContentValue(section, "wz");//骑缝章位置
                string Maxfgs = iniFileHelper.ContentValue(section, "maxfgs");//骑缝章最大分割数
                string YzIndex = iniFileHelper.ContentValue(section, "yzIndex");//选择的印章索引
                signText = iniFileHelper.ContentValue(section, "signText");//签名

                //@loquat 20250920
                string FixType = iniFileHelper.ContentValue(section, "fixType");//输出结果前后缀类型
                string configuredFixStr = iniFileHelper.ContentValue(section, "fixStr"); //输出结果前后缀文本
                if (!string.IsNullOrWhiteSpace(configuredFixStr)) { fixStr = configuredFixStr.Trim(); }
                string configuredFixStr2 = iniFileHelper.ContentValue(section, "fixStr2"); //加密后缀
                if (!string.IsNullOrWhiteSpace(configuredFixStr2)) { fixStr2 = configuredFixStr2.Trim(); }
                fixType = ToIntOrDefault(FixType,0);  //默认值0，不改变原有逻辑
                signBuiltInPath = iniFileHelper.ContentValue(section, "signBuiltInPath");    //内置证书路径
                signBuiltInPass = iniFileHelper.ContentValue(section, "signBuiltInPass");    //内置证书密码
                signCustomPath = iniFileHelper.ContentValue(section, "signCustomPath");      //自定义证书路径
                signCustomPass = iniFileHelper.ContentValue(section, "signCustomPass");      //自定义证书密码

                wjType = ToIntOrDefault(WjType,1);
                qfzType = ToIntOrDefault(QfzType, 0);
                yzType = NormalizeStampTypeFromConfig(YzType, YzTypeVersion);
                djType = StartupModeDefaults.OverlayOutput;
                qmType = ToIntOrDefault(QmType, 0);  //默认值还是0
                wzType = ToIntOrDefault(WzType, 3);
                qbflag = ToIntOrDefault(Qbflag, 0);
                size = ToIntOrDefault(Size, 40);
                rotation = ToIntOrDefault(Rotation, 0);
                opacity = ToIntOrDefault(Opacity, 60);
                wz = ToIntOrDefault(Wz, 50);
                maxfgs = ToIntOrDefault(Maxfgs, 20);
                yzIndex = ToIntOrDefault(YzIndex, -1);
            }
            //恢复上次保存的窗口大小与位置（存在且尺寸合理时才应用）
            if (File.Exists(strIniFilePath))
            {
                IniFileHelper winIni = new IniFileHelper(strIniFilePath);
                int winW = ToIntOrDefault(winIni.ContentValue(section, "windowWidth"), 0);
                int winH = ToIntOrDefault(winIni.ContentValue(section, "windowHeight"), 0);
                int winX = ToIntOrDefault(winIni.ContentValue(section, "windowLeft"), int.MinValue);
                int winY = ToIntOrDefault(winIni.ContentValue(section, "windowTop"), int.MinValue);
                System.Drawing.Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                if (winW >= 900 && winH >= 600 && winW <= workArea.Width && winH <= workArea.Height)
                {
                    this.Width = winW;
                    this.Height = winH;
                }
                if (winX != int.MinValue && winY != int.MinValue && winX < workArea.Right && winY < workArea.Bottom)
                {
                    this.StartPosition = FormStartPosition.Manual;
                    this.Left = winX;
                    this.Top = winY;
                }
            }
            fw = this.Width;
            fh = this.Height;
            collapsedClientWidth = PreviewPanelLayout.GetCollapsedClientWidth(comboPDFlist.Left, 8);
            expandedClientWidth = this.ClientSize.Width + 517;
            ApplyPreviewPanelLayout(yzType, qfzType);
            comboType.SelectedIndex = wjType;
            comboQfz.SelectedIndex = qfzType;
            comboYz.SelectedIndex = yzType;
            lastCommittedYzType = comboYz.SelectedIndex;
            comboDJ.SelectedIndex = djType;
            comboQmtype.SelectedIndex = qmType;
            comboBoxWZ.SelectedIndex = wzType;
            comboBoxQB.SelectedIndex = qbflag;
            textCC.Text = size.ToString();
            textRotation.Text = rotation.ToString();
            textOpacity.Text = opacity.ToString();
            textWzbl.Text = wz.ToString();
            textMaxFgs.Text = maxfgs.ToString();
            SynchronizeVisibleModeControls();
            UpdateWhiteBackgroundOptionState();
            UpdatePlacementOperationHint();

            //@loquat 20250922
            //容差加上ToolTip
            tip.AutoPopDelay = 5000;     // 提示显示时长（毫秒）
            tip.InitialDelay = 100;      // 鼠标进入后出现提示的延迟
            tip.ReshowDelay = 100;       // 提示再次出现的延迟
            tip.SetToolTip(this.txtAllow, "可设置透明色容差，默认20，最高50");

            pictureBox2.Parent = this.pictureBox1;//设置盖章预览图片的父控件为盖章预览框
            pictureBox2.Location = new Point(220, 380);//盖章预览图片位置
            pictureBox2.Tag = 0;
            previewStampOverlays[0] = pictureBox2;
            ApplyIdleStampOverlaySize();
            if (qmType == 0)
            {
                labelname.Text = "签名";
                textname.Text = "";
                textpass.Text = "";
                textname.ReadOnly = true;
                textpass.ReadOnly = true;
            }
            else if (qmType == 1)
            {
                textpass.Text = "";
                if (!File.Exists(certDefaultPath))  //内置证书未生成
                {
                    labelname.Text = "签名";
                    textname.Text = "";
                    textname.ReadOnly = false;     //可以输入签名文本和密码
                }
                else
                {
                    labelname.Text = "证书";
                    textname.Text = certDefaultPath;
                    textname.ReadOnly = true;
                    textpass.Text = signBuiltInPass;  //已经有了，就自动带入上次的密码
                }
                textpass.ReadOnly = false;
            }
            else if (qmType == 2)  //缩小范围
            {
                labelname.Text = "证书";
                if (signCustomPath.Length == 0)  //之前未使用过自定义证书
                {
                    OpenFileDialog file = new OpenFileDialog();
                    file.Filter = "证书文件|*.pfx";
                    if (file.ShowDialog() == DialogResult.OK)  //用户选择了pfx文件
                    {
                        textname.Text = file.FileName;
                    }
                    else                                       //用户为选择pfx文件
                    {
                        comboQmtype.SelectedIndex = 0;         //没选就跳到无数字证书
                        labelname.Text = "签名";
                        textname.ReadOnly = true;
                        textpass.ReadOnly = true;
                    }
                }
                else  //之前使用过自定义证书，那就加载自定义证书和密码
                {
                    textname.Text = signCustomPath;
                    textpass.Text = signCustomPass;
                    textname.ReadOnly = true;
                    textpass.ReadOnly = false;  //用户还是可以手动太密码
                }

            }
            else
            {
                //啥也不干
            }
            dtYz.Columns.Add("Name", typeof(string));
            dtYz.Columns.Add("Value", typeof(string));
            comboBoxYz.DisplayMember = "Name";
            comboBoxYz.ValueMember = "Value";
            comboBoxYz.DataSource = dtYz;

            if (!File.Exists(yzLog))  //不存在yz.log文件
            {
                yzIndex = -1;
                if (File.Exists(strIniFilePath))
                {
                    //为了避免误删印章记录,然后下次打开又不盖章,再打开可能的报错,这里先重置下印章索引
                    IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
                    iniFileHelper.WriteIniString(section, "yzIndex", yzIndex.ToString());
                }
            }
            else
            {
                string line;
                string filename;
                StreamReader sr = new StreamReader(yzLog, false);
                while ((line = sr.ReadLine()) != null)
                {
                    filename = System.IO.Path.GetFileName(line);//文件名
                    dtYz.Rows.Add(new object[] { filename, line });
                }
                sr.Close();
                sr.Dispose();
            }
            comboBoxYz.SelectedIndex = yzIndex;
            dt.Columns.Add("Name", typeof(string));
            dt.Columns.Add("Value", typeof(string));
            comboPDFlist.DisplayMember = "Name";
            comboPDFlist.ValueMember = "Value";
            comboPDFlist.DataSource = dt;
            dtPages.Columns.Add("Name", typeof(string));
            dtPages.Columns.Add("Value", typeof(string));
            comboBoxPages.DisplayMember = "Name";
            comboBoxPages.ValueMember = "Value";
            comboBoxPages.DataSource = dtPages;
            isSaveSources.Enabled = false;
            if (sourcePath != "")
            {
                wjType = 1;
                comboType.SelectedIndex = wjType;
                pathText.Text = sourcePath;
                textBCpath.Text = outputPath;
                dt.Rows.Clear();
                dt.Rows.Add(new object[] { "", "" });
                foreach (string filePath in commandLinePdfFiles)
                {
                    string filename = System.IO.Path.GetFileName(filePath);
                    dt.Rows.Add(new object[] { filename, filePath });
                }

                if (comboPDFlist.Items.Count > 1)
                {
                    comboPDFlist.SelectedIndex = comboPDFlist.Items.Count - 1;
                }
            }

            //预填上一次输入过的盖章文字
            InitAutoStampHistoryOnLoad();
        }

        public static int ToIntOrDefault(string str, int defaultValue = 0)
        {
            return int.TryParse(str, out int result) ? result : defaultValue;
        }

        private static int NormalizeStampTypeFromConfig(string value, string version)
        {
            int configuredType = ToIntOrDefault(value, 0);
            if (version == "3")
            {
                return configuredType >= 0 && configuredType <= SpecifiedPageStampType
                    ? configuredType
                    : 0;
            }

            if (version == "2")
            {
                // Previous build: 0=none, 1=all pages, 2=custom, 3=specified.
                if (configuredType == 3)
                {
                    return SpecifiedPageStampType;
                }

                return configuredType == 2 ? CustomPlacementStampType : 0;
            }

            // v1.33 had separate first-page and last-page options at indexes 1 and 2.
            // Those options no longer exist; map legacy visible-stamp settings to
            // custom placement rather than silently selecting a different mode.
            if (configuredType == 4)
            {
                return CustomPlacementStampType;
            }

            if (configuredType >= 5)
            {
                return SpecifiedPageStampType;
            }

            if (configuredType == 3)
            {
                return CustomPlacementStampType;
            }

            return configuredType == 0 ? 0 : CustomPlacementStampType;
        }

        /// <summary>
        /// 盖章按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void button1_Click(object sender, EventArgs e)
        {
            wjType = comboType.SelectedIndex;   //文件类型
            qfzType = comboQfz.SelectedIndex;   //骑缝章类型
            yzType = comboYz.SelectedIndex;     //印章类型
            djType = comboDJ.SelectedIndex;     //叠加类型
            qmType = comboQmtype.SelectedIndex; //签名类型
            wzType = comboBoxWZ.SelectedIndex;  //骑缝章位置类型
            qbflag = comboBoxQB.SelectedIndex;  //是否切边标记
            yzIndex = comboBoxYz.SelectedIndex; //选择的印章索引

            // 汇总本次是否实际会产生盖章效果（骑缝章 / 数字签名 / 预览中已放置的章）。
            // 注意：页面盖章的"手动点击/指定范围"只是操作模式，是否真的盖章要看预览里是否已有章。
            bool wantsSeam = qfzType != 1;
            bool wantsSignature = qmType != 0;
            bool hasPreviewStamps = stampPlacements.Count > 0;

            if (!wantsSeam && !wantsSignature && !hasPreviewStamps)
            {
                // 没有任何盖章操作：确认后仍生成（用户可能只想要一个不盖章的合并/输出文件）
                DialogResult choice = MessageBox.Show(
                    "您没有进行任何盖章操作，确定要生成文件吗？",
                    "确认生成",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (choice != DialogResult.Yes)
                {
                    return;
                }
            }
            else if (yzIndex == -1)
            {
                MessageBox.Show("请先选择印章!");
                return;
            }

            sourcePath = pathText.Text;
                outputPath = textBCpath.Text;
                imgPath = comboBoxYz.SelectedValue != null ? comboBoxYz.SelectedValue.ToString() : "";
                signText = textname.Text;
                password = textpass.Text;
                pdfpassword = textpdfpass.Text;

                if (sourcePath != "" && outputPath != "" && (imgPath != "" || qfzType == 1 && yzType == 0))
                {
                    // 是否需要印章图片/印章参数：没有任何实际盖章操作时不需要，直接输出文件
                    bool needStampImage = wantsSeam || wantsSignature || hasPreviewStamps;
                    if (needStampImage && !File.Exists(imgPath))
                    {
                        MessageBox.Show("印章文件读取失败,请重新导入印章。");
                    }
                    else if (needStampImage && (!int.TryParse(textCC.Text, out size) || size > 100))
                    {
                        MessageBox.Show("印章尺寸设置错误,请输入正确的尺寸。");
                    }
                    else if (needStampImage && (!int.TryParse(textRotation.Text, out rotation)))
                    {
                        MessageBox.Show("印章角度设置错误,请输入正确的整数。");
                    }
                    else if (needStampImage && (!int.TryParse(textOpacity.Text, out opacity) || opacity > 100))
                    {
                        MessageBox.Show("不透明度设置错误,请输入100以内的整数。");
                    }
                    else if (needStampImage && (!int.TryParse(textWzbl.Text, out wz) || wz > 100))
                    {
                        MessageBox.Show("骑缝章位置设置错误,请输入100以内的整数。");
                    }
                    else if (needStampImage && (!int.TryParse(textMaxFgs.Text, out maxfgs)))
                    {
                        MessageBox.Show("最大分割数设置错误,请输入正确的整数。");
                    }
                    else
                    {
                        await RunStampBatchAsync();

                        // 仅在本次有实际盖章操作时才保存印章配置，避免"仅输出文件"把印章参数覆盖为空值
                        if (needStampImage)
                        {
                            //自动保持最后一次盖章的配置信息到配置文件
                            IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);

                            iniFileHelper.WriteIniString(section, "wjType", wjType.ToString());
                            iniFileHelper.WriteIniString(section, "qfzType", qfzType.ToString());
                            iniFileHelper.WriteIniString(section, "yzType", yzType.ToString());
                            iniFileHelper.WriteIniString(section, "yzTypeVersion", "3");
                            iniFileHelper.WriteIniString(section, "djType", djType.ToString());
                            iniFileHelper.WriteIniString(section, "qmType", qmType.ToString());
                            iniFileHelper.WriteIniString(section, "wzType", wzType.ToString());
                            iniFileHelper.WriteIniString(section, "qbflag", qbflag.ToString());
                            iniFileHelper.WriteIniString(section, "size", size.ToString());
                            iniFileHelper.WriteIniString(section, "rotation", rotation.ToString());
                            iniFileHelper.WriteIniString(section, "opacity", opacity.ToString());
                            iniFileHelper.WriteIniString(section, "wz", wz.ToString());
                            iniFileHelper.WriteIniString(section, "maxfgs", maxfgs.ToString());
                            iniFileHelper.WriteIniString(section, "yzIndex", yzIndex.ToString());

                            //@loquat
                            //iniFileHelper.WriteIniString(section, "signText", signText);
                            //去pdfGz里，成功才保存签名的3个参数
                        }
                    }
                }
                else
                {
                    MessageBox.Show("文件路径不能为空，请先选择路径。");
                }
            
        }

        /// <summary>
        /// 盖章处理（后台调度）：先在 UI 线程准备印章与证书，再在后台批量盖章并显示进度
        /// </summary>
        private async Task RunStampBatchAsync()
        {
            bt_gz.Enabled = false;
            try
            {
                if (!PrepareStampResources())
                {
                    SetOperationHint("准备失败，未开始盖章，请检查上面的提示。", true);
                    return;
                }

                // 缓存 UI 值供后台线程读取，避免跨线程访问控件
                uiTextPxValue = ParseFloatOrDefault(textPx.Text, 50f);
                uiTextPyValue = ParseFloatOrDefault(textPy.Text, 50f);
                uiCheckRandom = checkRandom.Checked;
                bool saveSources = isSaveSources.Checked;

                bool hasFailures = await Task.Run(() => pdfGzCore(saveSources));
                SetOperationHint(hasFailures
                    ? "盖章处理完成，但有文件失败，请在下方日志查看失败原因。"
                    : "盖章处理完成，请在左下角查看各文件的输出结果。");
            }
            catch (Exception ex)
            {
                SetOperationHint("盖章过程中发生错误，请在下方日志查看。", true);
                AppendLog("错误：" + ex.Message + "\r\n");
            }
            finally
            {
                bt_gz.Enabled = true;
            }
        }

        /// <summary>
        /// 盖章前的准备：创建输出目录、加载证书、处理印章图片（仅 UI 线程调用）
        /// </summary>
        private bool PrepareStampResources()
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            if (logContainsOnlyHelp)
            {
                log.Text = "";
                logContainsOnlyHelp = false;
            }
            log.ForeColor = Color.Black;

            try
            {
                // 没有任何实际盖章操作且未选择印章时：用占位图片走"仅输出文件"流程（不会实际盖章）
                if (qfzType == 1 && qmType == 0 && stampPlacements.Count == 0
                    && (string.IsNullOrEmpty(imgPath) || !File.Exists(imgPath)))
                {
                    imgYz = new Bitmap(1, 1);
                    return true;
                }

                //如果要数字签名,先判断证书能否正常加载
                if (qmType != 0)
                {
                    string certPath = null;//证书路径
                    if (qmType == 1)
                    {
                        //如果证书不存在就先生成证书
                        if (!File.Exists(certDefaultPath))
                        {
                            var rSA = RSA.Create(4096); // 生成非对称密钥对
                            var req = new CertificateRequest("CN=" + signText, rSA, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                            X509Certificate2 newCert = req.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(5));

                            // Create PFX (PKCS #12) with private key
                            File.WriteAllBytes(certDefaultPath, newCert.Export(X509ContentType.Pkcs12, password));
                        }
                        certPath = certDefaultPath;
                    }
                    else
                    {
                        certPath = signText;//自定义证书路径
                    }

                    try
                    {
                        cert = new X509Certificate2(certPath, password, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);//注意设置第三个参数,不然有权限问题
                    }
                    catch
                    {
                        MessageBox.Show("证书加载失败，请检查证书路径和密码。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }

                imgYz = new Bitmap(imgPath);
                if (cbxTransColor.Checked)
                {
                    imgYz = SetWhiteToTransparent(imgYz);
                }
                if (opacity < 100)
                {
                    imgYz = SetImageOpacity(imgYz, opacity);
                }
                if (rotation != 0)
                {
                    bool qb = qbflag == 0 ? true : false;
                    int iw = imgYz.Width;
                    imgYz = RotateImg(imgYz, rotation, qb);
                    xzbl = 1f * imgYz.Width / iw;
                }
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("印章准备失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// 批量盖章核心（后台线程执行）
        /// </summary>
        private bool pdfGzCore(bool saveSources)
        {
            bool hasFailures = false;
            try
            {
                //目录模式还是文件模式
                if (wjType == 0)  //目录模式
                {
                    DirectoryInfo dir = new DirectoryInfo(sourcePath);
                    var fileInfos = dir.GetFiles("*.pdf", SearchOption.AllDirectories);
                    List<FileInfo> targets = new List<FileInfo>();
                    foreach (var fileInfo in fileInfos)
                    {
                        if (fileInfo.DirectoryName == outputPath)
                        {
                            //如果源文件目录跟输出目录一样,则不处理
                            continue;
                        }
                        if (fileInfo.Extension == ".pdf")
                        {
                            targets.Add(fileInfo);
                        }
                    }
                    int total = targets.Count;
                    int done = 0;
                    foreach (var fileInfo in targets)
                    {
                        done++;
                        string source = fileInfo.DirectoryName + "\\" + fileInfo.Name;
                        string destinationDirectory = saveSources
                            ? fileInfo.DirectoryName
                            : outputPath;
                        string output = OutputFileNamingPolicy.GetNextOutputPath(
                            destinationDirectory,
                            source,
                            fixStr,
                            fixType == 1);
                        UpdateStampProgress(done, total, fileInfo.Name);
                        bool isSurrcess = PDFWatermark(source, output, source,
                            (d, t) => UpdateStampPageProgress(d, t, fileInfo.Name),
                            saving => ShowSavingIndicator(saving));
                        if (isSurrcess && djType == 1)
                        {
                            PDFToiPDF(output);
                        }
                        if (isSurrcess)
                        {
                            StopSavingIndicator();
                            AppendLog(OutputFileNamingPolicy.BuildSuccessMessage(fileInfo.Name, output) + "\r\n");
                            SaveSuccessfulStampConfig();
                        }
                        else
                        {
                            StopSavingIndicator();
                            hasFailures = true;
                            AppendLog("失败！“" + fileInfo.Name + "”盖章失败！\r\n");
                        }
                    }
                }
                else  //文件模式
                {
                    string[] fileArray = sourcePath.Split(',');//字符串转数组
                    int total = fileArray.Length;
                    int done = 0;
                    foreach (string file in fileArray)
                    {
                        done++;
                        string filename = Path.GetFileName(file);//文件名
                        string output = OutputFileNamingPolicy.GetNextOutputPath(
                            outputPath,
                            file,
                            fixStr,
                            fixType == 1);
                        string actualOutput = output;
                        UpdateStampProgress(done, total, filename);
                        bool isSurrcess = PDFWatermark(file, output, file,
                            (d, t) => UpdateStampPageProgress(d, t, filename),
                            saving => ShowSavingIndicator(saving));
                        if (isSurrcess)
                        {
                            if (djType == 1)
                            {
                                PDFToiPDF(output);
                            }
                            if (pdfpassword != "")
                            {
                                string jmoutput = OutputFileNamingPolicy.AddSuffix(output, fixStr2);
                                EncryptPDF(output, jmoutput, pdfpassword);
                                if (File.Exists(jmoutput))
                                {
                                    actualOutput = jmoutput;
                                }
                            }
                            StopSavingIndicator();
                            AppendLog(OutputFileNamingPolicy.BuildSuccessMessage(filename, actualOutput) + "\r\n");
                            SaveSuccessfulStampConfig();
                        }
                        else
                        {
                            StopSavingIndicator();
                            hasFailures = true;
                            AppendLog("失败！“" + filename + "”盖章失败！\r\n");
                        }
                    }
                }
                return hasFailures;
            }
            catch (Exception ex)
            {
                AppendLog("处理过程中发生错误：" + ex.Message + "\r\n");
                return true;
            }
        }

        /// <summary>
        /// 后台线程安全地向日志区追加一行
        /// </summary>
        private void AppendLog(string line)
        {
            if (log.InvokeRequired)
            {
                log.BeginInvoke(new Action<string>(AppendLog), line);
                return;
            }
            log.AppendText(line);
        }

        /// <summary>
        /// 更新盖章进度（进度条 + 操作提示），线程安全
        /// </summary>
        private void UpdateStampProgress(int done, int total, string fileName)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.BeginInvoke(new Action(() => UpdateStampProgress(done, total, fileName)));
                return;
            }
            if (total > 0)
            {
                progressBar1.Visible = true;
                progressBar1.Maximum = total;
                progressBar1.Value = Math.Min(done, total);
            }
            SetOperationHint(string.Format("正在处理第 {0}/{1} 个文件：{2}", done, total, fileName));
            if (done >= total)
            {
                progressBar1.Visible = false;
            }
        }

        /// <summary>
        /// 页级盖章进度（后台线程调用）：提示区实时显示“正在盖章中：已完成 X/Y 页（Z%）”，
        /// 让页数多的文件也能看到动态推进，避免误以为卡死。
        /// </summary>
        private void UpdateStampPageProgress(int done, int total, string fileName)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.BeginInvoke(new Action(() => UpdateStampPageProgress(done, total, fileName)));
                return;
            }
            int percent = total > 0 ? (int)Math.Round(100.0 * done / total) : 0;
            SetOperationHint(string.Format("正在盖章中：已完成 {0}/{1} 页（{2}%），文件：{3}",
                done, total, percent, fileName));
        }

        /// <summary>
        /// 保存阶段指示器（后台线程调用）：不改变提示区框的位置，只替换框内文字内容——
        /// “正在保存中.....”省略号循环增减动画 + 第二行已用时计时，让用户明确知道程序仍在运行。
        /// </summary>
        private void ShowSavingIndicator(bool active)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<bool>(ShowSavingIndicator), active);
                return;
            }

            if (active)
            {
                savingIndicatorActive = true;
                savingStartTime = DateTime.Now;
                savingDotPhase = 0;
                lastSavingTick = DateTime.MinValue;
                // 界面空闲事件驱动操作提示区的动态文字刷新（纯 UI 线程，无需后台任务）
                Application.Idle += OnSavingIdle;
                UpdateSavingIndicatorTick();
            }
            else
            {
                // 保存（写回文件）阶段结束，但后续还有合并转图/加密等处理，
                // 动画继续，避免提前显示“完成”
                UpdateSavingIndicatorTick();
            }
        }

        /// <summary>
        /// 所有处理全部完成后停止动画并恢复操作提示区默认说明。
        /// 完成与否以左侧提示区显示的“盖章成功”日志为准。
        /// </summary>
        private void StopSavingIndicator()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(StopSavingIndicator));
                return;
            }

            savingIndicatorActive = false;
            Application.Idle -= OnSavingIdle;
            UpdatePlacementOperationHint();
        }

        /// <summary>
        /// 界面空闲时刷新操作提示区保存文字（UI 线程）：每 300ms 更新一次，省略号增减 + 计时递增
        /// </summary>
        private void OnSavingIdle(object sender, EventArgs e)
        {
            if (!savingIndicatorActive)
            {
                Application.Idle -= OnSavingIdle;
                return;
            }

            if ((DateTime.Now - lastSavingTick).TotalMilliseconds < 300)
            {
                return;
            }

            lastSavingTick = DateTime.Now;
            UpdateSavingIndicatorTick();
        }

        /// <summary>
        /// 保存文字动画刷新（UI 线程）：省略号数量先增后减循环，第二行显示已用时
        /// </summary>
        private void UpdateSavingIndicatorTick()
        {
            if (!savingIndicatorActive)
            {
                return;
            }

            savingDotPhase = (savingDotPhase + 1) % 8;
            int dotCount = 3 + (savingDotPhase < 4 ? savingDotPhase : 7 - savingDotPhase);
            TimeSpan elapsed = DateTime.Now - savingStartTime;
            SetOperationHint(string.Format("本文件较大，请耐心等待，正在生成盖章文件中{0}\r\n已用时 {1:mm\\:ss}",
                new string('.', dotCount), elapsed));
        }

        /// <summary>
        /// 盖章成功后保存一次签章配置（从后台线程调用，只读取已缓存的字段）
        /// </summary>
        private void SaveSuccessfulStampConfig()
        {
            IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
            if (qmType == 1)
            {
                signBuiltInPath = signText;
                signBuiltInPass = password;
                iniFileHelper.WriteIniInt(section, "fixType", fixType);
                iniFileHelper.WriteIniString(section, "signBuiltInPath", signBuiltInPath);
                iniFileHelper.WriteIniString(section, "signBuiltInPass", signBuiltInPass);
            }
            else if (qmType == 2)
            {
                signCustomPath = signText;
                signCustomPass = password;
                iniFileHelper.WriteIniInt(section, "fixType", fixType);
                iniFileHelper.WriteIniString(section, "signCustomPath", signCustomPath);
                iniFileHelper.WriteIniString(section, "signCustomPass", signCustomPass);
            }
        }

        private static float ParseFloatOrDefault(string text, float defaultValue)
        {
            return float.TryParse(text, out float value) ? value : defaultValue;
        }
        //设置图片白色为透明
        private Bitmap SetWhiteToTransparent(Bitmap src)
        {
            return WhiteTransparencyHelper.Apply(src, GetWhiteTransparencyTolerance());
        }

        /// <summary>
        /// 解决任意骑缝章时没有选定的页面的问题
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboQfz_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboQfz.SelectedIndex != -1 && comboQfz.SelectedIndex == 4)
            {
                comboPDFlist.SelectedIndex = comboPDFlist.Items.Count - 1;
                if (IsPlacementStampType(comboYz.SelectedIndex) && comboPDFlist.SelectedIndex != -1)
                {
                    MessageBox.Show("提示:随意骑缝章和手动点击盖章共用右边的预览定位,所以同时使用的时候会冲突,建议分开盖章");
                }
            }
        }

        /// <summary>
        /// 加盖印章
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void comboYz_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressYzSelectionChange)
            {
                return;
            }

            if (comboYz.SelectedIndex != -1 && comboYz.SelectedIndex != 0)
            {
                if (comboPDFlist.SelectedIndex == -1 && comboPDFlist.Items.Count > 1)
                {
                    comboPDFlist.SelectedIndex = comboPDFlist.Items.Count - 1;
                }
                if (IsPlacementStampType(comboYz.SelectedIndex) && comboQfz.SelectedIndex == 4)
                {
                    MessageBox.Show("提示:随意骑缝章和手动点击盖章共用右边的预览定位,所以同时使用的时候会冲突,建议分开盖章");
                }
            }
        }

        /// <summary>
        /// pdf文件预览
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void comboPDFlist_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboPDFlist.SelectedIndex != -1 && comboPDFlist.Items.Count > 0)   //手动选择pdf文件
            {
                await LoadSelectedPreviewAsync();
            }
        }
        //设置图片透明度(1-100)
        //设置条件编译
    #if UNSAFE_MODE
        private Bitmap SetImageOpacity(Bitmap srcImage, int opacity)
        {
            // 参数校验
            if (opacity < 0 || opacity > 100)
                throw new ArgumentOutOfRangeException("opacity", "必须介于0到100之间");
            // 计算实际Alpha值 (0-255)
            byte alpha = (byte)(opacity * 255 / 100);
            // 创建目标位图（确保32bppARGB格式）
            Bitmap dstImage = new Bitmap(srcImage.Width, srcImage.Height, PixelFormat.Format32bppArgb);
            // 将原始图像绘制到目标位图（保留透明度信息）
            using (Graphics g = Graphics.FromImage(dstImage))
            {
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(srcImage, new System.Drawing.Rectangle(0, 0, dstImage.Width, dstImage.Height));
            }
            // 锁定位图进行快速内存操作
            BitmapData data = dstImage.LockBits(
                new System.Drawing.Rectangle(0, 0, dstImage.Width, dstImage.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb
            );
            unsafe // 需要启用unsafe编译选项
            {
                byte* ptr = (byte*)data.Scan0;
                // 遍历所有像素
                for (int y = 0; y < data.Height; y++)
                {
                    for (int x = 0; x < data.Width; x++)
                    {
                        // 每个像素的四个通道（BGRA格式，索引0-3对应Blue, Green, Red, Alpha）
                        int index = y * data.Stride + x * 4;
                        // 仅修改非完全透明像素（alpha值不为0）
                        if (ptr[index + 3] != 0) // 检查Alpha通道
                        {
                            ptr[index + 3] = alpha; // 设置新的Alpha值
                        }
                    }
                }
            }
            // 解锁并返回处理后的图像
            dstImage.UnlockBits(data);
            return dstImage;
        }
#else   //SetImageOpacity的安全方法，比原来的效率也要高很多倍
        private Bitmap SetImageOpacity(Bitmap src, int opacity)
        {
            byte alpha = (byte)(opacity * 255 / 100);
            BitmapData data = src.LockBits(new System.Drawing.Rectangle(0, 0, src.Width, src.Height),
                                         ImageLockMode.ReadWrite,
                                         PixelFormat.Format32bppArgb);
            try
            {
                byte[] buffer = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                for (int i = 3; i < buffer.Length; i += 4) // 遍历每个Alpha通道
                {
                    if (buffer[i] != 0) buffer[i] = alpha;
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
            }
            finally
            {
                src.UnlockBits(data);
            }
            return src;
        }
#endif 
        //旋转图片
        public Bitmap RotateImg(System.Drawing.Bitmap bitmap, int angle,bool original = true)
        {
            angle = angle % 360;
            //弧度转换 
            double radian = angle * Math.PI / 180.0;
            double cos = Math.Cos(radian);
            double sin = Math.Sin(radian);
            //原图的宽和高 
            int w = bitmap.Width;
            int h = bitmap.Height;
            int W = (int)(Math.Max(Math.Abs(w * cos - h * sin), Math.Abs(w * cos + h * sin)));
            int H = (int)(Math.Max(Math.Abs(w * sin - h * cos), Math.Abs(w * sin + h * cos)));

            //为了尽可能的去除白边,减小印章旋转后尺寸的误差,这里保持原印章宽度,切掉部分角
            if (original)
            {
                H = H * w / W;
                W = w;
            }

            //目标位图 
            Bitmap dsImage = new Bitmap(W, H);
            System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(dsImage);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            //计算偏移量 
            Point Offset = new Point((W - w) / 2, (H - h) / 2);
            //构造图像显示区域:让图像的中心与窗口的中心点一致 
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(Offset.X, Offset.Y, w, h);
            Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.TranslateTransform(center.X, center.Y);
            g.RotateTransform(angle);
            //恢复图像在水平和垂直方向的平移 
            g.TranslateTransform(-center.X, -center.Y);
            g.DrawImage(bitmap, rect);
            //重至绘图的所有变换 
            g.ResetTransform();
            g.Dispose();

            return dsImage;
        }

        //分割图片
        private static Bitmap[] subImages(Bitmap img, int n)//图片分割
        {
            Bitmap[] nImage = new Bitmap[n];
            int H = img.Height;
            int W = img.Width;
            int w1 = W / 3;//首页骑缝章默认占1/3宽度
            int w = (W- w1) / n;
            n = n - 1;
            int tmpw = W;
            for (int i = 0; i <= n; i++)
            {
                int sw;
                if(i == n)
                {
                    sw = tmpw;
                }else if(i == 0)
                {
                    sw = w1;
                }
                else
                {
                    sw = w;
                }
                Bitmap newbitmap = new Bitmap(sw, H);
                Graphics g = Graphics.FromImage(newbitmap);
                g.DrawImage(img, new System.Drawing.Rectangle(0, 0, sw, H), new System.Drawing.Rectangle(W-tmpw, 0, sw, H), GraphicsUnit.Pixel);
                g.Dispose();
                nImage[i] = newbitmap;
                tmpw = tmpw - sw;
            }
            return nImage;
        }

        //PDF盖章(贴图)
        private bool PDFWatermark(string inputfilepath, string outputfilepath, string sourcepath, Action<int, int> pageProgress = null, Action<bool> savingIndicator = null)
        {
            float sfbl = (100f * size * xzbl * 72) / (25.4f * imgYz.Width);

            PdfReader pdfReader = null;
            PdfStamper pdfStamper = null;
            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(outputfilepath, FileMode.Create);
                pdfReader = new PdfReader(inputfilepath, new System.Text.UTF8Encoding().GetBytes(pdfpassword));//选择需要印章的pdf
                if (qmType != 0)
                {
                    //最后的true表示保留原签名
                    pdfStamper = PdfStamper.CreateSignature(pdfReader, fileStream, '\0', null, true);//加完印章后的pdf
                }
                else
                {
                    pdfStamper = new PdfStamper(pdfReader, fileStream);
                }

                int numberOfPages = pdfReader.NumberOfPages;//pdf页数
                int qfzPages = 0;
                List<int> qfzList = new List<int>();

                bool skipSeamStamp = SeamStampPolicy.ShouldSkipForSinglePage(numberOfPages, qfzType);
                if (!skipSeamStamp && qfzType == 0)
                {
                    for (int i = 1; i <= numberOfPages; i ++)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if(!skipSeamStamp && qfzType == 2)
                {
                    for (int i = 1; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if(!skipSeamStamp && qfzType == 3)
                {
                    for (int i = 2; i <= numberOfPages; i += 2)
                    {
                        qfzList.Add(i);
                        qfzPages++;
                    }
                }
                else if(!skipSeamStamp && qfzType == 4)
                {
                    foreach (int page in stampPlacements.DistinctPages(sourcepath))
                    {
                        qfzList.Add(page);
                        qfzPages++;
                    }
                }
                
                PdfContentByte waterMarkContent;

                if(qfzType != 1&& qfzPages > 1)
                {
                    int max = maxfgs;//骑缝章最大分割数
                    int ss = qfzPages / max + 1;
                    int sy = qfzPages - ss * max / 2;
                    int sys = sy / ss;
                    int syy = sy % ss;
                    int pp = max / 2 + sys;
                    Bitmap[] nImage;
                    int startIndex = 0;
                    for (int i = 0; i < ss; i++)
                    {
                        int tmp = pp;
                        if (i < syy)
                        {
                            tmp++;
                        }
                        nImage = subImages(imgYz, tmp);
                        for (int y = 0; y < tmp; y++)
                        {
                            int page = qfzList[startIndex + y];
                            waterMarkContent = pdfStamper.GetOverContent(page);//获取当前页内容
                            int rotation = pdfReader.GetPageRotation(page);//获取当前页的旋转度
                            iTextSharp.text.Rectangle psize = pdfReader.GetPageSize(page);//获取当前页尺寸
                            float pWidth, pHeight;
                            if (rotation == 90 || rotation == 270)
                            {
                                pWidth = psize.Height;
                                pHeight = psize.Width;
                            }
                            else
                            {
                                pWidth = psize.Width;
                                pHeight = psize.Height;
                            }
                            Bitmap qfzImage = null;
                            if(wzType == 3|| wzType == 2)
                            {
                                qfzImage = nImage[y];
                            }
                            else if (wzType == 1|| wzType == 0)
                            {
                                qfzImage = RotateImg(nImage[y], 90, false);
                            }
                            iTextSharp.text.Image image = iTextSharp.text.Image.GetInstance(qfzImage, System.Drawing.Imaging.ImageFormat.Png);//获取骑缝章对应页的部分
                            float imageW, imageH;
                            image.ScalePercent(sfbl);//设置图片比例
                            imageW = image.Width * sfbl / 100f;
                            imageH = image.Height * sfbl / 100f;

                            //水印的位置
                            float xPos=0, yPos=0;
                            if (wzType == 3)
                            {
                                xPos = pWidth - imageW;
                                yPos = (pHeight - imageH) * (100 - wz) / 100;
                            }
                            else if (wzType == 2)
                            {
                                xPos = 0;
                                yPos = (pHeight - imageH) * (100 - wz) / 100;
                            }
                            else if (wzType == 1)
                            {
                                xPos = (pWidth - imageW) * wz / 100;
                                yPos = 0;
                            }
                            else if (wzType == 0)
                            {
                                xPos = (pWidth - imageW) * wz / 100;
                                yPos = pHeight - imageH;
                            }
                            image.SetAbsolutePosition(xPos, yPos);
                            waterMarkContent.AddImage(image);
                            //waterMarkContent.RestoreState();
                        }
                        startIndex += tmp;
                    }
                }

                iTextSharp.text.Image img = null;
                float imgW = 0, imgH = 0;
                float stampXPos = 0, stampYPos = 0;
                int signpage = numberOfPages;

                if (StampRenderPolicy.ShouldRenderVisibleStamp(yzType))
                {
                    if (IsPlacementStampType(yzType))
                    {
                        List<int> placementPages = stampPlacements.DistinctPages(sourcepath).ToList();
                        signpage = placementPages.Count == 0 ? 1 : placementPages[placementPages.Count - 1];
                        StampPlacement signaturePlacement = qmType == 0
                            ? null
                            : stampPlacements.ForPage(sourcepath, signpage).LastOrDefault();

                        int placementDone = 0;
                        int totalProcessPages = numberOfPages;
                        for (int page = 1; page <= numberOfPages; page++)
                        {
                            // 每遍历一页都推进进度（含无章页），让进度反映整个文件的真实处理进程
                            if (pageProgress != null)
                            {
                                pageProgress(Math.Min(page, totalProcessPages), totalProcessPages);
                            }

                            List<StampPlacement> pagePlacements = stampPlacements.ForPage(sourcepath, page).ToList();
                            if (pagePlacements.Count == 0)
                            {
                                continue;
                            }
                            placementDone++;

                            waterMarkContent = pdfStamper.GetOverContent(page);
                            int pageRotation = pdfReader.GetPageRotation(page);
                            iTextSharp.text.Rectangle pageSize = pdfReader.GetPageSize(page);

                            foreach (StampPlacement placement in pagePlacements)
                            {
                                using (Bitmap placementBitmap = CreatePlacementBitmap(placement))
                                {
                                    iTextSharp.text.Image placementImage = iTextSharp.text.Image.GetInstance(
                                        placementBitmap,
                                        System.Drawing.Imaging.ImageFormat.Png);
                                    float placementScale = 100f * placement.SizeMm * 72f /
                                        (25.4f * placementBitmap.Width);
                                    placementImage.ScalePercent(placementScale);
                                    placementImage.RotationDegrees = GetRandomStampRotation(page, signpage);

                                    float placementWidth = placementImage.Width * placementScale / 100f;
                                    float placementHeight = placementImage.Height * placementScale / 100f;
                                    float placementX;
                                    float placementY;
                                    CalculateStampPosition(
                                        pageRotation,
                                        pageSize,
                                        placementWidth,
                                        placementHeight,
                                        placement.X,
                                        1f - placement.Y,
                                        out placementX,
                                        out placementY);
                                    placementImage.SetAbsolutePosition(placementX, placementY);

                                    bool useForDigitalSignature = signaturePlacement != null &&
                                        placement.Id == signaturePlacement.Id;
                                    if (useForDigitalSignature)
                                    {
                                        img = placementImage;
                                        imgW = placementWidth;
                                        imgH = placementHeight;
                                        stampXPos = placementX;
                                        stampYPos = placementY;
                                    }
                                    else
                                    {
                                        waterMarkContent.AddImage(placementImage);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        int no_page = 0;//不需要盖印章的页,0表示所有页都需要盖印章
                        signpage = 0;

                        for (int i = 1; i <= numberOfPages; i++)
                        {
                            if (!StampPlacementPolicy.ShouldRenderVisibleStampOnPage(i, no_page, signpage, qmType != 0))
                            {
                                continue;
                            }

                            waterMarkContent = pdfStamper.GetOverContent(i);//获取当前页内容
                            int pageRotation = pdfReader.GetPageRotation(i);//获取指定页面的旋转度
                            iTextSharp.text.Rectangle pageSize = pdfReader.GetPageSize(i);//获取当前页尺寸
                            float wbl = uiTextPxValue;
                            float hbl = 1 - uiTextPyValue;
                            StampPlacement pagePlacement = stampPlacements.ForPage(sourcepath, i).FirstOrDefault();
                            if (pagePlacement != null)
                            {
                                wbl = pagePlacement.X;
                                hbl = 1f - pagePlacement.Y;
                            }
                            else if (uiCheckRandom)
                            {
                                Random random = new Random();
                                int random_w = random.Next(-2, 3);
                                int random_h = random.Next(-2, 3);
                                if ((wbl + 0.01f * random_w) > 0f && (wbl + 0.01f * random_w) < 1f)
                                {
                                    wbl += 0.01f * random_w;
                                }
                                if ((hbl - 0.01f * random_h) > 0f && (hbl - 0.01f * random_h) < 1f)
                                {
                                    hbl -= 0.01f * random_h;
                                }
                            }

                            img = iTextSharp.text.Image.GetInstance(imgYz, System.Drawing.Imaging.ImageFormat.Png);//创建一个图片对象
                            img.RotationDegrees = GetRandomStampRotation(i, signpage);
                            img.ScalePercent(sfbl);//设置图片比例
                            imgW = img.Width * sfbl / 100f;
                            imgH = img.Height * sfbl / 100f;
                            CalculateStampPosition(
                                pageRotation,
                                pageSize,
                                imgW,
                                imgH,
                                wbl,
                                hbl,
                                out stampXPos,
                                out stampYPos);
                            img.SetAbsolutePosition(stampXPos, stampYPos);
                            waterMarkContent.AddImage(img);
                        }
                    }
                }

                if (qmType != 0)
                {

                    Org.BouncyCastle.X509.X509CertificateParser cp = new Org.BouncyCastle.X509.X509CertificateParser();
                    Org.BouncyCastle.X509.X509Certificate[] chain = new Org.BouncyCastle.X509.X509Certificate[] {cp.ReadCertificate(cert.RawData)};

                    Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair pk = Org.BouncyCastle.Security.DotNetUtilities.GetKeyPair(cert.GetRSAPrivateKey());
                    IExternalSignature externalSignature = new PrivateKeySignature(pk.Private, DigestAlgorithms.SHA256);

                    PdfSignatureAppearance signatureAppearance = pdfStamper.SignatureAppearance;
                    signatureAppearance.SignDate = DateTime.Now;
                    if (!StampRenderPolicy.ShouldRenderVisibleStamp(yzType))
                    {
                        signatureAppearance.SetVisibleSignature(new iTextSharp.text.Rectangle(0, 0, 0, 0), numberOfPages, null);
                    }
                    else
                    {
                        signatureAppearance.SignatureRenderingMode = PdfSignatureAppearance.RenderingMode.GRAPHIC;//仅体现图片
                        signatureAppearance.SignatureGraphic = img;

                        float bk = 2;//数字签名的图片要加上边框才能跟普通印章的位置完全一致
                        signatureAppearance.SetVisibleSignature(new iTextSharp.text.Rectangle(stampXPos - bk, stampYPos - bk, stampXPos + imgW + bk, stampYPos + imgH + bk), signpage, null);
                    }

                    MakeSignature.SignDetached(signatureAppearance, externalSignature, chain, null, null, null, 0, CryptoStandard.CMS);
                }
                return true;
            }
            catch (BadPasswordException)
            {
                AppendLog("文件“" + Path.GetFileName(inputfilepath) + "”打不开：PDF 密码错误或文件已加密。\r\n");
                return false;
            }
            catch (Exception ex)
            {
                AppendLog("文件“" + Path.GetFileName(inputfilepath) + "”盖章失败：" + ex.Message + "\r\n");
                return false;
            }
            finally
            {
                // 保存阶段：iTextSharp 在 Close 时才把改动写回整个文件，大文件较耗时。
                // 开启不确定进度动画 + 计时提示（提示文字由 ShowSavingIndicator 统一设置），
                // 确保 Close 期间界面仍有反馈（Close 整体写入，无法拿到真实百分比）
                if (savingIndicator != null)
                {
                    savingIndicator(true);
                }

                try
                {
                    if (pdfStamper != null)
                        pdfStamper.Close();

                    if (pdfReader != null)
                        pdfReader.Close();

                    if (fileStream != null)
                        fileStream.Close();
                }
                finally
                {
                    if (savingIndicator != null)
                    {
                        savingIndicator(false);
                    }
                }

                //盖章失败时清理可能残留的 0 字节空文件
                if (File.Exists(outputfilepath))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(outputfilepath);
                        if (fi.Length == 0)
                        {
                            File.Delete(outputfilepath);
                        }
                    }
                    catch
                    {
                        //删除失败不影响主流程
                    }
                }
            }
        }

        private Bitmap CreatePlacementBitmap(StampPlacement placement)
        {
            Bitmap processed = new Bitmap(placement.StampPath);
            if (placement.UseWhiteTransparency)
            {
                Bitmap transparent = WhiteTransparencyHelper.Apply(processed, placement.WhiteTransparencyTolerance);
                processed.Dispose();
                processed = transparent;
            }

            if (placement.Opacity < 100)
            {
                processed = SetImageOpacity(processed, placement.Opacity);
            }

            if (placement.Rotation != 0)
            {
                Bitmap rotated = RotateImg(processed, placement.Rotation, placement.UseOriginalRotationCrop);
                if (!ReferenceEquals(rotated, processed))
                {
                    processed.Dispose();
                }
                processed = rotated;
            }

            return processed;
        }

        private int GetRandomStampRotation(int page, int signPage)
        {
            if (page != signPage && uiCheckRandom)
            {
                lock (randomGenerator)
                {
                    return randomGenerator.Next(-2, 3);
                }
            }

            return 0;
        }

        private static void CalculateStampPosition(
            int pageRotation,
            iTextSharp.text.Rectangle pageSize,
            float imageWidth,
            float imageHeight,
            float widthRatio,
            float heightRatio,
            out float x,
            out float y)
        {
            if (pageRotation == 90 || pageRotation == 270)
            {
                x = (pageSize.Height - imageWidth) * widthRatio;
                y = (pageSize.Width - imageHeight) * heightRatio;
            }
            else
            {
                x = (pageSize.Width - imageWidth) * widthRatio;
                y = (pageSize.Height - imageHeight) * heightRatio;
            }
        }

        public static void ImageToPDF(Bitmap[] bitmaps,float bl, string trageFullName)
        {
            using (iTextSharp.text.Document document = new iTextSharp.text.Document(new iTextSharp.text.Rectangle(0, 0), 0, 0, 0, 0))
            {
                iTextSharp.text.pdf.PdfWriter.GetInstance(document, new FileStream(trageFullName, FileMode.Create, FileAccess.ReadWrite));
                document.Open();
                iTextSharp.text.Image image;
                for (int i = 0; i < bitmaps.Length; i++)
                {
                    image = iTextSharp.text.Image.GetInstance(bitmaps[i], System.Drawing.Imaging.ImageFormat.Bmp);
                    float Width = image.Width* bl, Height = image.Height* bl;
                    image.ScaleToFit(Width, Height);
                    document.SetPageSize(new iTextSharp.text.Rectangle(0, 0, Width, Height));
                    document.NewPage();
                    document.Add(image);
                }
            }
        }


        public static void SignaturePDF(string inputPath,string outPath, X509Certificate2 cert)
        {
            PdfReader pdfReader = null;
            PdfStamper pdfStamper = null;
            try
            {
                pdfReader = new PdfReader(inputPath);//选择需要印章的pdf
                pdfStamper = PdfStamper.CreateSignature(pdfReader, new FileStream(outPath, FileMode.Create), '\0', null, true);
                Org.BouncyCastle.X509.X509CertificateParser cp = new Org.BouncyCastle.X509.X509CertificateParser();
                Org.BouncyCastle.X509.X509Certificate[] chain = new Org.BouncyCastle.X509.X509Certificate[] { cp.ReadCertificate(cert.RawData) };

                Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair pk = Org.BouncyCastle.Security.DotNetUtilities.GetKeyPair(cert.GetRSAPrivateKey());
                IExternalSignature externalSignature = new PrivateKeySignature(pk.Private, DigestAlgorithms.SHA256);

                PdfSignatureAppearance signatureAppearance = pdfStamper.SignatureAppearance;
                signatureAppearance.SignDate = DateTime.Now;

                MakeSignature.SignDetached(signatureAppearance, externalSignature, chain, null, null, null, 0, CryptoStandard.CMS);
            }
            catch (Exception ex)
            {
                MessageBox.Show("数字签名失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {

                if (pdfStamper != null)
                    pdfStamper.Close();

                if (pdfReader != null)
                    pdfReader.Close();
            }

        }
        //加密PDF文件
        public static void EncryptPDF(string inputFilePath, string outputFilePath, string password)
        {
            try
            {
                using (FileStream fs = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    using (PdfReader reader = new PdfReader(inputFilePath))
                    {
                        using (Document document = new Document())
                        {
                            PdfEncryptor.Encrypt(reader, fs, true, password, password, PdfWriter.ALLOW_PRINTING);
                        }
                    }
                }
                File.Delete(inputFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("添加密码保护时发生错误：" + ex.Message);
            }
        }
        //@loquat 20250920 增加一个双击可重新选择证书
        private void textname_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Filter = "证书文件|*.pfx";
            if (file.ShowDialog() == DialogResult.OK)  //用户选择了pfx文件
            {
                textname.Text = file.FileName;
            }
            else                                       //用户为选择pfx文件
            {
                comboQmtype.SelectedIndex = 0;         //没选就跳到无数字证书
                labelname.Text = "签名";
                textname.ReadOnly = true;
                textpass.ReadOnly = true;
            }
        }

        private void SaveSources(object sender, EventArgs e)
        {
            if (isSaveSources.Checked == true)
            {
                textBCpath.Enabled = false;
                OutPath.Enabled = false;
                if (comboType.SelectedIndex == 0 && pathText.Text != "")
                {
                    textBCpath.Text = pathText.Text;
                }
                else if (comboType.SelectedIndex == 1 && pathText.Text != "")
                {
                    textBCpath.Text = System.IO.Path.GetDirectoryName(pathText.Text);
                }
            }
            else
            {
                textBCpath.Enabled = true;
                OutPath.Enabled = true;
                if (comboType.SelectedIndex == 0 && pathText.Text != "")
                {
                    textBCpath.Text = pathText.Text +"\\已盖章"; ;
                }
                else if (comboType.SelectedIndex == 1 && pathText.Text != "")
                {
                    textBCpath.Text = System.IO.Path.GetDirectoryName(pathText.Text) + "\\已盖章";
                }
            }

        }


        /// <summary>
        /// pdf文件转换为图片，这个里面的很多设置后期可以考虑放出来，比如分辨率倍数等
        /// </summary>
        /// <param name="pdfPath"></param>
        public void PDFToiPDF(string pdfPath)
        {
            int dpi = 300; //原版方法最好默认300
            float bl = 72f / dpi; // 为了尽量保证转换的清晰度，这里需要把电脑的DPI缩放到PDF的DPI,在计算bl时，使用了固定值72，PDF中，1英寸等于72个点（point）
            Bitmap[] bitmaps = null;
            string renderPath = pdfPath;
            try
            {
                // PDFium does not paint some Acrobat /Stamp annotations. Flatten them before
                // merge-mode rasterization so existing stamps become part of the output bitmap.
                renderPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(pdfPath);
                using (IPdfDocumentRenderer pdfRenderer = PdfiumDocumentRenderer.Open(renderPath))
                {
                    bitmaps = new Bitmap[pdfRenderer.PageCount];
                    for (int i = 0; i < pdfRenderer.PageCount; i++)
                    {
                        bitmaps[i] = pdfRenderer.RenderPage(i, dpi);
                    }
                }

                string tmpPdf = pdfPath;
                if (qmType != 0)        //0 不使用数字签名，这表示使用数字签名时
                {
                    tmpPdf = System.IO.Path.GetTempPath() + "PDFQFZ_tmp.pdf";
                }

                ImageToPDF(bitmaps, bl, tmpPdf);      //转换位图到图片

                if (qmType != 0)
                {
                    SignaturePDF(tmpPdf, pdfPath, cert);
                }
            }
            finally
            {
                if (bitmaps != null)
                {
                    foreach (Bitmap bitmap in bitmaps)
                    {
                        bitmap?.Dispose();
                    }
                }

                if (!string.Equals(renderPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                {
                    PreviewPdfPreparation.TryDelete(renderPath);
                }
            }
        }
        //建一个全局的目录变量
        string pathDir = "";
        //选择源文件
        private async void SelectPath_Click(object sender, EventArgs e)
        {
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token; // 获取取消标记

            if (comboType.SelectedIndex == 0)   //目录模式
            {
                if (Environment.OSVersion.Version.Major >= 6)                                       //操作系统win7及以上才能使用此效果
                {
                    FolderSelectDialog fsd = new FolderSelectDialog();
                    fsd.Title = "请选择pdf所在的文件夹";
                    fsd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (fsd.ShowDialog(this.Handle))
                    {
                        pathText.Text = fsd.FileName;
                        pathDir = fsd.FileName;
                        if (textBCpath.Text == "")
                        {
                            textBCpath.Text = fsd.FileName + "\\已盖章";
                        }
                        dt.Rows.Clear();
                        dt.Rows.Add(new object[] { "", "" });
                        DirectoryInfo dir = new DirectoryInfo(pathText.Text);
                        var fileInfos = dir.GetFiles("*.pdf", SearchOption.AllDirectories);

                        // 初始化状态栏和进度条
                        log.Text = "准备批量处理...\r\n";
                        progressBar1.Visible = true;
                        bt_gz.Enabled = false;//批量加载时防止用户点击按钮
                        
                        //开始异步加载文件
                        try
                        {
                            await Task.Run(() => Load_All_PdfFiles_Async(fileInfos, token), token);
                        }
                        catch (OperationCanceledException)
                        {
                            log.Text = "操作已取消！";
                        }
                        finally
                        {
                            // 取消后的清理工作
                            cancellationTokenSource.Dispose();
                        }
                        bt_gz.Enabled = true;//恢复盖章按钮
                        pathText.Text = fsd.FileName;
                        ResetPreviewDisplay();
                        //foreach (var fileInfo in fileInfos)
                        //{
                        //    if (fileInfo.Extension == ".pdf")
                        //    {
                        //        dt.Rows.Add(new object[] { fileInfo.Name, fileInfo.FullName });
                        //    }
                        //}
                    }
                }
                else
                {
                    FolderBrowserDialog fbd = new FolderBrowserDialog();
                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        pathText.Text = fbd.SelectedPath;
                        pathDir = fbd.SelectedPath;
                        if (textBCpath.Text == "")
                        {
                            textBCpath.Text = fbd.SelectedPath + "\\已盖章";
                        }
                        dt.Rows.Clear();
                        dt.Rows.Add(new object[] { "", "" });
                        DirectoryInfo dir = new DirectoryInfo(pathText.Text);
                        var fileInfos = dir.GetFiles("*.pdf", SearchOption.AllDirectories);
                        foreach (var fileInfo in fileInfos)
                        {
                            if (fileInfo.Extension == ".pdf")
                            {
                                dt.Rows.Add(new object[] { fileInfo.Name, fileInfo.FullName });
                            }
                        }
                        ResetPreviewDisplay();
                    }
                }
            }
            else
            {           //文件模式
                OpenFileDialog file = new OpenFileDialog();
                file.Multiselect = true;
                file.Filter = "PDF文件|*.pdf";
                file.Title = "请选择要盖章的pdf文件";    //不设置默认左上角显示打开
                if (file.ShowDialog() == DialogResult.OK)
                {
                    pathText.Text = string.Join(",", file.FileNames);
                    if (textBCpath.Text == "")
                    {
                        textBCpath.Text = System.IO.Path.GetDirectoryName(file.FileName);
                    }
                    pathDir = textBCpath.Text;
                    dt.Rows.Clear();
                    dt.Rows.Add(new object[] { "", "" });
                    foreach (string filePath in file.FileNames)
                    {
                        string filename = System.IO.Path.GetFileName(filePath);//文件名
                        dt.Rows.Add(new object[] { filename, filePath });
                    }

                    //更新文件后，重新更新pdf预览框
                    if (comboPDFlist.SelectedIndex != -1 && comboPDFlist.Items.Count > 0)//0是空白行,1才是选择的文件行
                    {
                        comboPDFlist.SelectedIndex = comboPDFlist.Items.Count - 1;
                        //// 手动创建一个 EventArgs 对象
                        //EventArgs eventArgs = new EventArgs();
                        //// 调用 comboPDFlist_SelectionChangeCommitted 事件处理程序
                        //comboPDFlist_SelectionChangeCommitted(comboPDFlist, eventArgs);
                    }
                }
            }
        }
        /// <summary>
        /// 加载文件夹文件使用异步方式并辅以进度条显示状态，并可以随时按ESC取消
        /// </summary>
        private async Task Load_All_PdfFiles_Async(FileInfo[] fileInfos, CancellationToken token)
        {
            int totalFiles = fileInfos.Length;

            // 在 UI 线程上更新进度条的最大值和初始值
            this.Invoke((Action)(() =>
            {
                progressBar1.Maximum = totalFiles;
                progressBar1.Value = 0;
                log.Text += "开始批量处理...\r\n";
            }));


            // 遍历文件
            await Task.Run(() =>
            {
                for (int i = 0; i < totalFiles; i++)
                {
                    token.ThrowIfCancellationRequested(); // 检查是否有取消请求,如果有结束遍历动作

                    var fileInfo = fileInfos[i];

                    // 更新UI（此处要使用 Invoke，因为跨线程更新UI）
                    Invoke(new Action(() =>
                    {
                        dt.Rows.Add(new object[] { fileInfo.Name, fileInfo.FullName });

                        // 更新进度条和状态栏
                        pathText.Text = $"正在加载文件 {i + 1}/{totalFiles}\r\n";
                        progressBar1.Value = i + 1;
                    }));
                }
            });

            // 所有文件加载完成后更新状态
            Invoke(new Action(() =>
            {
                log.Text = "加载完成!\r\n";
                progressBar1.Value = totalFiles;
                progressBar1.Visible = false;
            }));
        }
        //按下ESC事件
        private void Esc_Key_Down(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // 检查是否有一个操作在进行中
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel(); // 请求取消当前操作
                    MessageBox.Show("操作已停止");
                    progressBar1.Visible = false;
                    bt_gz.Enabled = true;
                    pathText.Text = "";
                }
            }
        }

        //选择保存目录
        private void OutPath_Click(object sender, EventArgs e)
        {
            if (Environment.OSVersion.Version.Major >= 6)                                       //操作系统win7及以上才能使用此效果
            {
                FolderSelectDialog fsd = new FolderSelectDialog();
                fsd.Title = "请选择保存的文件夹";
                if (string.IsNullOrWhiteSpace(pathText.Text.Trim()))
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    fsd.InitialDirectory = desktopPath;
                }
                else
                {
                    fsd.InitialDirectory = pathText.Text;
                }
                if (fsd.ShowDialog(this.Handle))
                {
                    textBCpath.Text = fsd.FileName;
                }
            }
            else
            {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    textBCpath.Text = fbd.SelectedPath;
                }
            }
        }
        //选择印章图片
        private void GzPath_Click(object sender, EventArgs e)
        {
            OpenFileDialog file = new OpenFileDialog();
            file.Filter = "图片文件|*.jpg;*.png";
            file.Multiselect = true;
            if (file.ShowDialog() == DialogResult.OK)
            {
                string filename;
                StreamWriter sw = File.AppendText(yzLog);
                foreach (string filePath in file.FileNames)
                {
                    filename = System.IO.Path.GetFileName(filePath);//文件名
                    dtYz.Rows.Add(new object[] { filename, filePath });
                    sw.WriteLine(filePath);
                }
                sw.Close();
                sw.Dispose();

                //// 保存当前选中的项
                //object selectedItem = comboBoxYz.SelectedItem;

                //// 更新ComboBox的数据源
                //List<string> items = new List<string>();
                //foreach (string item in comboBoxYz.Items)
                //{
                //    items.Add(item);
                //}
                //items.Reverse();
                //comboBoxYz.DataSource = items;

                // 将之前保存的选中项重新设置为ComboBox的选中项
                //comboBoxYz.SelectedItem = selectedItem;

                //// 将ComboBox的SelectedIndex设置为0
                comboBoxYz.SelectedIndex = comboBoxYz.Items.Count - 1; //每次导入章图片后，并选定到最后一张，也就是最新导入的图片
            }
        }
        //预览图定位，根据页面中的单击位置添加印章。
        private void AddPreviewStampAtPoint(Point point)
        {
            try
            {
                Point pt = point;
                Size overlaySize = GetCurrentPreviewOverlaySize();
                pt.X = pt.X - overlaySize.Width / 2;
                pt.Y = pt.Y - overlaySize.Height / 2;
                int picw = Math.Max(0, pictureBox1.Width - overlaySize.Width);
                int pich = Math.Max(0, pictureBox1.Height - overlaySize.Height);

                if (pt.X < 0) pt.X = 0;
                if (pt.Y < 0) pt.Y = 0;
                if (pt.X > picw) pt.X = picw;
                if (pt.Y > pich) pt.Y = pich;
                float px = picw == 0 ? 0f : 1f * pt.X / picw;
                float py = pich == 0 ? 0f : 1f * pt.Y / pich;

                textPx.Text = px.ToString("#0.0000");
                textPy.Text = py.ToString("#0.0000");
                if (string.IsNullOrWhiteSpace(previewPath))
                {
                    MessageBox.Show("请先加载 PDF，再进行手动盖章。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (comboBoxYz.SelectedValue == null)
                {
                    // 仅手动点击 / 指定范围等放置类模式提示；其他模式保持原静默行为
                    if (IsPlacementStampType(yzType))
                    {
                        MessageBox.Show("请先选择印章！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                string selectedStampPath = comboBoxYz.SelectedValue.ToString();
                if (!File.Exists(selectedStampPath))
                {
                    return;
                }

                AddPreviewStamp(px, py, selectedStampPath);
                RefreshPreviewOverlays();
            }
            catch (Exception)
            {
                MessageBox.Show("请先加载pdf再预览，偷个懒不再提供无pdf预览功能");
            }
        }

        private static bool IsPlacementStampType(int stampType)
        {
            return stampType == CustomPlacementStampType || stampType == SpecifiedPageStampType;
        }

        private bool IsCurrentSpecifiedRangePage()
        {
            return specifiedPageRange != null && specifiedPageRange.Contains(imgStartPage);
        }

        private void AddPreviewStamp(float px, float py, string selectedStampPath)
        {
            int sizeMm = GetPreviewSizeValue();
            int currentOpacity = GetPreviewOpacityValue();
            int currentRotation = GetPreviewRotationValue();
            int whiteTolerance = GetWhiteTransparencyTolerance();
            bool useWhiteTransparency = cbxTransColor.Checked;
            bool useOriginalRotationCrop = qbflag == 0;

            if (yzType == SpecifiedPageStampType && specifiedRangeFirstClickPending &&
                specifiedPageRange != null && imgStartPage == specifiedPageRange.EndPage)
            {
                activeSpecifiedBatchId = stampPlacements.CreateBatchId();
                for (int page = specifiedPageRange.StartPage; page <= specifiedPageRange.EndPage; page++)
                {
                    stampPlacements.Add(previewPath, page, px, py, selectedStampPath, sizeMm,
                        currentOpacity, currentRotation, whiteTolerance, useWhiteTransparency,
                        useOriginalRotationCrop, activeSpecifiedBatchId);
                }
                specifiedRangeFirstClickPending = false;
                activeSpecifiedBatchId = 0;
                yzType = CustomPlacementStampType;
                comboYz.SelectedIndex = CustomPlacementStampType;
                lastCommittedYzType = CustomPlacementStampType;
                SynchronizeVisibleModeControls();
                SetOperationHint("指定范围页印章已添加，现已回到手动点击盖章；双击印章可删除。需要再添加一批时，请重新点击“指定范围页盖章”。");
                return;
            }

            stampPlacements.Add(previewPath, imgStartPage, px, py, selectedStampPath, sizeMm,
                currentOpacity, currentRotation, whiteTolerance, useWhiteTransparency,
                useOriginalRotationCrop);
        }

        //文字预盖章：搜索指定文字，把所有匹配位置的印章中心对准文字中心并放置到预览。
        private void AutoStampButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
                {
                    MessageBox.Show("请先加载 PDF 文件再预盖章。");
                    return;
                }

                if (comboBoxYz.SelectedValue == null)
                {
                    MessageBox.Show("请先选择印章图片。");
                    return;
                }

                string stampPath = comboBoxYz.SelectedValue.ToString();
                if (!File.Exists(stampPath))
                {
                    MessageBox.Show("印章图片不存在，请重新选择。");
                    return;
                }

                string keyword = autoStampInput != null ? autoStampInput.Text.Trim() : string.Empty;
                if (keyword.Length == 0)
                {
                    MessageBox.Show("请输入要识别的盖章文字。");
                    return;
                }

                List<PdfTextMatch> matches;
                using (PdfTextSearcher searcher = new PdfTextSearcher(previewPath))
                {
                    // 整个文档没有文字层（扫描件/纯图片）→ 图片型 PDF，无法搜索文字
                    if (!searcher.HasAnyText())
                    {
                        MessageBox.Show("图片型 PDF 无法搜索到文字，无法放置印章");
                        return;
                    }

                    matches = searcher.FindAll(keyword);
                }

                if (matches == null || matches.Count == 0)
                {
                    MessageBox.Show("没找到您指定的盖章文字");
                    return;
                }

                // 相同文字已放置过 → 弹窗确认是否重新放置（确认后撤销旧批、按最新参数重盖；不同文字则各自保留）
                AutoStampOperation existingOp = autoStampOperations.LastOrDefault(
                    o => string.Equals(o.Keyword, keyword, StringComparison.Ordinal));
                if (existingOp != null)
                {
                    DialogResult confirm = MessageBox.Show(
                        string.Format("已用“{0}”放置过印章，是否重新放置？", keyword),
                        "重新放置",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes)
                    {
                        SetOperationHint("已取消重新放置，保留原有印章。");
                        return;
                    }

                    stampPlacements.RemoveBatch(previewPath, existingOp.BatchId);
                    autoStampOperations.Remove(existingOp);
                }

                int batchId = stampPlacements.CreateBatchId();
                int sizeMm = GetPreviewSizeValue();
                int currentOpacity = GetPreviewOpacityValue();
                int currentRotation = GetPreviewRotationValue();
                int whiteTolerance = GetWhiteTransparencyTolerance();
                bool useWhiteTransparency = cbxTransColor.Checked;
                bool useOriginalRotationCrop = qbflag == 0;

                Size overlaySize = GetCurrentPreviewOverlaySize();
                int previewW = Math.Max(1, pictureBox1.Width);
                int previewH = Math.Max(1, pictureBox1.Height);
                float ratioW = 1f * overlaySize.Width / previewW;
                float ratioH = 1f * overlaySize.Height / previewH;

                foreach (PdfTextMatch match in matches)
                {
                    AutoStampPositionResult pos = AutoStampPositionCalculator.Calculate(
                        match.CenterX, match.CenterY, match.PageWidth, match.PageHeight, ratioW, ratioH);
                    stampPlacements.Add(previewPath, match.PageIndex + 1, pos.Px, pos.Py, stampPath, sizeMm,
                        currentOpacity, currentRotation, whiteTolerance, useWhiteTransparency,
                        useOriginalRotationCrop, batchId);
                }

                autoStampOperations.Add(new AutoStampOperation { Keyword = keyword, BatchId = batchId });
                if (undoAutoStampButton != null)
                {
                    undoAutoStampButton.Enabled = true;
                }

                // 成功找到并完成盖章的文字才记入历史（搜索不到则不记；本次失败不影响之前同样文字的历史）
                RecordAutoStampKeyword(keyword);

                RefreshPreviewOverlays();
                SetOperationHint(string.Format(
                    "按文字盖章完成：共找到 {0} 处“{1}”，已全部盖上。双击单个章可删除，或点击“撤销放置”逐步撤销。",
                    matches.Count, keyword));
            }
            catch (Exception ex)
            {
                MessageBox.Show("预盖章失败：" + ex.Message);
            }
        }

        // 撤销放置：像 Word 撤销一样，点击一次撤销最近一次"按文字放置"，再点撤销更早一次，直到全部撤销完。
        private void UndoAutoStampButton_Click(object sender, EventArgs e)
        {
            if (autoStampOperations.Count == 0)
            {
                return;
            }

            AutoStampOperation op = autoStampOperations[autoStampOperations.Count - 1];
            autoStampOperations.RemoveAt(autoStampOperations.Count - 1);

            int removed = stampPlacements.RemoveBatch(previewPath, op.BatchId);
            if (undoAutoStampButton != null)
            {
                undoAutoStampButton.Enabled = autoStampOperations.Count > 0;
            }

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

        // 按文字放置的一次操作记录（用于逐步撤销）
        private sealed class AutoStampOperation
        {
            public string Keyword;
            public int BatchId;
        }

        // ---------- 按文字盖章：历史记忆与下拉选择 ----------

        /// <summary>读取已保存的盖章文字历史（最新在前）。</summary>
        private List<string> LoadAutoStampHistory()
        {
            List<string> result = new List<string>();
            try
            {
                IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
                string raw = iniFileHelper.ContentValue(section, AutoStampHistoryIniKey);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return result;
                }

                foreach (string item in raw.Split(new[] { AutoStampHistorySeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string text = item.Trim();
                    if (text.Length > 0)
                    {
                        result.Add(text);
                    }
                }
            }
            catch
            {
                // 读取失败时返回空列表，不影响使用
            }

            return result;
        }

        /// <summary>保存盖章文字历史到配置文件。</summary>
        private void SaveAutoStampHistory(List<string> history)
        {
            try
            {
                IniFileHelper iniFileHelper = new IniFileHelper(strIniFilePath);
                iniFileHelper.WriteIniString(section, AutoStampHistoryIniKey, string.Join(AutoStampHistorySeparator, history));
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>记录一条盖章文字（最新在前、去重、最多保留 10 条）。</summary>
        private void RecordAutoStampKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            List<string> history = LoadAutoStampHistory();
            history.RemoveAll(x => string.Equals(x, keyword, StringComparison.Ordinal));
            history.Insert(0, keyword.Trim());
            if (history.Count > MaxAutoStampHistory)
            {
                history.RemoveRange(MaxAutoStampHistory, history.Count - MaxAutoStampHistory);
            }
            SaveAutoStampHistory(history);
        }

        /// <summary>程序启动时预填上一次输入过的盖章文字。</summary>
        private void InitAutoStampHistoryOnLoad()
        {
            if (autoStampInput == null)
            {
                return;
            }

            List<string> history = LoadAutoStampHistory();
            if (history.Count > 0)
            {
                autoStampInput.SetText(history[0]);
            }
        }

        //文件/目录模式切换
        private void comboType_SelectionChangeCommitted(object sender, EventArgs e)
        {
            pathText.Text = "";
            textBCpath.Text = "";
            dt.Rows.Clear();
            ResetPreviewForInputModeChange();
            if (comboType.SelectedIndex == 0)
            {
                label1.Text = "请选择或拖入需要盖章的PDF文件所在目录";
                label2.Text = "请选择PDF盖章后所保存的目录";
                isSaveSources.Enabled = true;
            }
            else
            {
                label1.Text = "请选择或拖入需要盖章的PDF文件（支持多选）";
                label2.Text = "请选择PDF盖章后所保存的目录";
                isSaveSources.Enabled = false;

            }
        }

        //显示指定PDF页
        public async Task viewPDFPage()
        {
            if (viewPdfimgs == null || previewPageCache == null)
            {
                return;
            }

            Bitmap pageImage;
            try
            {
                pageImage = await GetOrLoadPreviewPageAsync(imgStartPage - 1);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            pictureBox1.Image = pageImage;
            SetPreviewPlaceholderVisible(false);
            UpdatePreviewPageScrollBar();
            labelPage.Text = imgStartPage + "/" + imgPageCount;
            if (currentPageInput != null)
            {
                currentPageInput.Text = imgStartPage.ToString();
            }
            if (totalPageLabel != null)
            {
                totalPageLabel.Text = "/ " + imgPageCount + " 页";
            }
            LayoutPreviewPage();

            if (imgStartPage == 1)
            {
                buttonUp.Enabled = false;
            }
            else
            {
                buttonUp.Enabled = true;
            }
            if (imgStartPage == imgPageCount)
            {
                buttonNext.Enabled = false;
            }
            else
            {
                buttonNext.Enabled = true;
            }
            comboBoxPages.Text = "" + imgStartPage;
        }

        //跳转到指定页
        private void comboBoxPages_SelectionChangeCommitted(object sender, EventArgs e)
        {
            
            string pageIndex = comboBoxPages.SelectedValue.ToString();
            imgStartPage = int.Parse(pageIndex);
            _ = viewPDFPage();
        }

        //上一页
        private void buttonUp_Click(object sender, EventArgs e)
        {
            if (viewPdfimgs != null&&imgStartPage > 1)
            {
                imgStartPage--;
                _ = viewPDFPage();
            }
        }
        //下一页
        private void buttonNext_Click(object sender, EventArgs e)
        {
            if (viewPdfimgs != null&&imgStartPage < imgPageCount)
            {
                imgStartPage++;
                _ = viewPDFPage();
            }
        }
        //双击删除当前预览中的指定印章
        private void pictureBox2_DoubleClick(object sender, EventArgs e)
        {
            PictureBox overlay = sender as PictureBox;
            if (overlay == null || overlay.Tag == null)
            {
                return;
            }

            int placementId = Convert.ToInt32(overlay.Tag);
            StampPlacement placement = stampPlacements.Find(placementId);
            if (placement == null)
            {
                return;
            }

            bool removed;
            if (placement.BatchId > 0)
            {
                DialogResult choice = MessageBox.Show(
                    "这是由“指定范围页盖章”添加的批量印章。点击“是”删除整个批次，点击“否”仅删除当前页，点击“取消”保留。",
                    "删除印章",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (choice == DialogResult.Yes)
                {
                    removed = stampPlacements.RemoveBatch(previewPath, placement.BatchId) > 0;
                }
                else if (choice == DialogResult.No)
                {
                    removed = stampPlacements.RemoveBatchOnPage(previewPath, imgStartPage, placement.BatchId) > 0;
                }
                else
                {
                    removed = false;
                }
            }
            else
            {
                removed = stampPlacements.Remove(placementId);
            }

            if (removed)
            {
                // 若删除后该"按文字"批次已无残留，从撤销栈移除对应操作，避免"撤销放置"撤销到不存在的批次
                if (placement.BatchId > 0 && !stampPlacements.HasBatch(previewPath, placement.BatchId))
                {
                    autoStampOperations.RemoveAll(o => o.BatchId == placement.BatchId);
                    if (undoAutoStampButton != null)
                    {
                        undoAutoStampButton.Enabled = autoStampOperations.Count > 0;
                    }
                }

                RefreshPreviewOverlays();
            }
        }

        private void pathText_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.All;//调用DragDrop事件
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void pathText_DragDrop(object sender, DragEventArgs e)
        {
            string[] filePaths = (string[])e.Data.GetData(DataFormats.FileDrop);//拖放的多个文件的路径列表

            if (Directory.Exists(filePaths[0])&&comboType.SelectedIndex==0)
            {
                this.pathText.Text = filePaths[0];
                this.textBCpath.Text = filePaths[0] + "\\已盖章";
                dt.Rows.Clear();
                dt.Rows.Add(new object[] { "", "" });
                DirectoryInfo dir = new DirectoryInfo(pathText.Text);
                var fileInfos = dir.GetFiles("*.pdf",SearchOption.AllDirectories);
                foreach (var fileInfo in fileInfos)
                {
                    if (fileInfo.Extension == ".pdf")
                    {
                        dt.Rows.Add(new object[] { fileInfo.Name, fileInfo.FullName });
                    }
                }
                ResetPreviewDisplay();
            }
            else
            {
                string pdfPaths = "";

                foreach (string filePath in filePaths)
                {
                    string extension = System.IO.Path.GetExtension(filePath);//文件后缀名
                    if (extension == ".pdf")
                    {
                        pdfPaths += filePath + ",";
                    }
                }
                if(pdfPaths.Length > 0)
                {
                    pdfPaths = pdfPaths.Substring(0, pdfPaths.Length - 1);
                    this.pathText.Text = pdfPaths;
                    this.textBCpath.Text = System.IO.Path.GetDirectoryName(filePaths[0]);

                    string[] files = pdfPaths.Split(',');
                    dt.Rows.Clear();
                    dt.Rows.Add(new object[] { "", "" });
                    foreach (string file in files)
                    {
                        string filename = System.IO.Path.GetFileName(file.ToString());//文件名
                        dt.Rows.Add(new object[] { filename, file });
                    }

                    //解决拖拽完成后，pdf文件列表空白不显示，预览pdf文件名的问题
                    if (comboPDFlist.SelectedIndex != -1 && comboPDFlist.Items.Count > 0)//0是空白行
                    {
                        comboPDFlist.SelectedIndex = comboPDFlist.Items.Count - 1;

                        //// 手动创建一个 EventArgs 对象
                        //EventArgs eventArgs = new EventArgs();
                        //// 调用 comboPDFlist_SelectionChangeCommitted 事件处理程序
                        //comboPDFlist_SelectionChangeCommitted(comboPDFlist, eventArgs);
                    }

                }
            }
            
        }

        //数字签名类型
        private void comboQmtype_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (comboQmtype.SelectedIndex == 0)
            {
                labelname.Text = "签名";
                textname.Text = "";
                textpass.Text = "";
                textname.ReadOnly = true;
                textpass.ReadOnly = true;
            }
            else if (comboQmtype.SelectedIndex == 1)
            {
                
                textpass.Text = "";
                if (!File.Exists(certDefaultPath))
                {
                    labelname.Text = "签名";
                    textname.Text = "";
                    textname.ReadOnly = false;
                }
                else
                {
                    labelname.Text = "证书";
                    textname.Text = certDefaultPath;
                    textname.ReadOnly = true;
                }
                    
                textpass.ReadOnly = false;
            }
            else
            {
                labelname.Text = "证书";
                textname.Text = "";
                textpass.Text = "";
                textname.ReadOnly = true;
                textpass.ReadOnly = false;

                OpenFileDialog file = new OpenFileDialog();
                file.Filter = "证书文件|*.pfx";
                if (file.ShowDialog() == DialogResult.OK)
                {
                    textname.Text = file.FileName;
                }
                else
                {
                    comboQmtype.SelectedIndex = 0;
                    labelname.Text = "签名";
                    textname.ReadOnly = true;
                    textpass.ReadOnly = true;
                }
            }
            
        }
        private async Task LoadSelectedPreviewAsync()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            cts = new CancellationTokenSource();
            ResetPreviewSessionState();
            dtPages.Rows.Clear();
            // 切换到新的源文件：清空上一份文档放置的章与撤销栈，预览从空白开始
            stampPlacements.Clear();
            autoStampOperations.Clear();
            if (undoAutoStampButton != null)
            {
                undoAutoStampButton.Enabled = false;
            }
            specifiedPageRange = null;
            specifiedRangeFirstClickPending = false;
            activeSpecifiedBatchId = 0;
            // 切换到新的源文件：提示区恢复初始说明，不再显示上一份文件的输出记录
            log.Text = InitialHelpText;
            logContainsOnlyHelp = true;
            previewPath = comboPDFlist.SelectedValue == null ? "" : comboPDFlist.SelectedValue.ToString();
            ClearPreviewResources();

            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                previewRenderPath = PreviewPdfPreparation.CreateAnnotationFlattenedCopy(previewPath);
                previewPdfRenderer = PdfiumDocumentRenderer.Open(previewRenderPath);
                imgStartPage = 1;
                imgPageCount = previewPdfRenderer.PageCount;
                viewPdfimgs = new Bitmap[imgPageCount];
                previewPageCache = new PageCache<Bitmap>(pageIndex => previewPdfRenderer.RenderPage(pageIndex, previewRenderDpi));

                for (int i = 1; i <= imgPageCount; i++)
                {
                    dtPages.Rows.Add(new object[] { i, i });
                }

                viewPdfimgs[0] = await GetOrLoadPreviewPageAsync(0);
                await viewPDFPage();
                UpdatePlacementOperationHint();
                return;
            }

            ResetPreviewDisplay();
        }

        private async Task<Bitmap> GetOrLoadPreviewPageAsync(int pageIndex)
        {
            if (viewPdfimgs[pageIndex] != null)
            {
                return viewPdfimgs[pageIndex];
            }

            CancellationToken token = cts != null ? cts.Token : CancellationToken.None;
            Bitmap pageImage = await Task.Run(() => previewPageCache.GetPage(pageIndex), token);
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            viewPdfimgs[pageIndex] = pageImage;
            return pageImage;
        }

        private Bitmap GetOrLoadPreviewPage(int pageIndex)
        {
            if (viewPdfimgs[pageIndex] != null)
            {
                return viewPdfimgs[pageIndex];
            }

            Bitmap pageImage = previewPageCache.GetPage(pageIndex);
            viewPdfimgs[pageIndex] = pageImage;
            return pageImage;
        }

        private void ClearPreviewResources()
        {
            CancelPreviewOverlayRefresh();
            ClearDynamicPreviewOverlays();
            ReplacePreviewOverlayImage(null);
            pictureBox1.Image = null;

            if (previewPageCache != null)
            {
                previewPageCache.Dispose();
                previewPageCache = null;
            }

            if (previewPdfRenderer != null)
            {
                previewPdfRenderer.Dispose();
                previewPdfRenderer = null;
            }

            if (!string.Equals(previewRenderPath, previewPath, StringComparison.OrdinalIgnoreCase))
            {
                PreviewPdfPreparation.TryDelete(previewRenderPath);
            }
            previewRenderPath = "";

            ClearTransparentStampCache();
        }

        private void ResetPreviewDisplay()
        {
            viewPdfimgs = null;
            imgStartPage = 1;
            imgPageCount = 0;
            labelPage.Text = "0/0";
            if (currentPageInput != null)
            {
                currentPageInput.Text = "";
            }
            if (totalPageLabel != null)
            {
                totalPageLabel.Text = "/ 0 页";
            }
            Bitmap bmp = new Bitmap(358, 500);
            Graphics g = Graphics.FromImage(bmp);
            g.FillRectangle(Brushes.White, new System.Drawing.Rectangle(0, 0, 358, 500));
            g.Dispose();
            pictureBox1.Image = bmp;
            SetPreviewPlaceholderText(GetIdlePreviewPlaceholderText());
            SetPreviewPlaceholderVisible(true);
            UpdatePlacementOperationHint();
            ApplyIdleStampOverlaySize();
            buttonUp.Enabled = false;
            buttonNext.Enabled = false;
            UpdatePreviewPageScrollBar();
        }

        private string GetIdlePreviewPlaceholderText()
        {
            return IdlePreviewPolicy.GetPlaceholderText(
                comboType.SelectedIndex == 0,
                Directory.Exists(pathText.Text));
        }

        private void ResetPreviewForInputModeChange()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }

            previewPath = "";
            dtPages.Rows.Clear();
            stampPlacements.Clear();
            autoStampOperations.Clear();
            if (undoAutoStampButton != null)
            {
                undoAutoStampButton.Enabled = false;
            }

            specifiedPageRange = null;
            specifiedRangeFirstClickPending = false;
            activeSpecifiedBatchId = 0;
            ClearPreviewResources();
            ResetPreviewDisplay();
        }

        private void ApplyIdleStampOverlaySize()
        {
            pictureBox2.Size = new Size(yzr * 2, yzr * 2);

            if (comboBoxYz.SelectedValue == null)
            {
                return;
            }

            string selectedStampPath = comboBoxYz.SelectedValue.ToString();
            if (string.IsNullOrWhiteSpace(selectedStampPath) || !File.Exists(selectedStampPath))
            {
                return;
            }

            using (Bitmap stampImage = new Bitmap(selectedStampPath))
            {
                Size overlaySize = PreviewStampLayout.CalculateOverlaySize(
                    configuredWidthMm: GetPreviewSizeValue(),
                    imageDpi: stampImage.HorizontalResolution,
                    imagePixelWidth: stampImage.Width,
                    imagePixelHeight: stampImage.Height,
                    previewWidth: pictureBox1.Width,
                    pdfWidth: pictureBox1.Image == null
                        ? pictureBox1.Width
                        : pictureBox1.Image.Width * 72f / previewRenderDpi,
                    fallbackSquareSize: yzr * 2);
                pictureBox2.Size = overlaySize;
            }
        }

        private void previewOverlayStyle_Changed(object sender, EventArgs e)
        {
            if (ReferenceEquals(sender, textCC))
            {
                NormalizeUnsignedPreviewTextBox(textCC, false);
            }
            else if (ReferenceEquals(sender, textOpacity))
            {
                NormalizeUnsignedPreviewTextBox(textOpacity, false);
            }
            else if (ReferenceEquals(sender, textRotation))
            {
                NormalizeSignedPreviewTextBox(textRotation, false);
            }

            RefreshPreviewOverlay();
        }

        private void RefreshPreviewOverlay()
        {
            RefreshPreviewOverlays();
        }

        private void RefreshPreviewOverlay(float px, float py, bool visible)
        {
            RefreshPreviewOverlays();
        }

        private void RefreshPreviewOverlays()
        {
            ClearDynamicPreviewOverlays();
            pictureBox2.Visible = false;

            if (string.IsNullOrWhiteSpace(previewPath) || pictureBox1.Image == null)
            {
                return;
            }

            // 页面物理宽度（pt）→ 显示换算基准：预览图片像素 ÷ 渲染缩放
            float pageWidthPoints = pictureBox1.Image.Width * 72f / previewRenderDpi;

            foreach (StampPlacement placement in stampPlacements.ForPage(previewPath, imgStartPage))
            {
                try
                {
                    Bitmap stampBitmap = CreatePlacementBitmap(placement);
                    Size overlaySize = PreviewStampLayout.CalculateOverlaySize(
                        placement.SizeMm,
                        stampBitmap.HorizontalResolution,
                        stampBitmap.Width,
                        stampBitmap.Height,
                        pictureBox1.Width,
                        pageWidthPoints,
                        yzr * 2);
                    PictureBox overlay = new PictureBox
                    {
                        Parent = pictureBox1,
                        Tag = placement.Id,
                        Size = overlaySize,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Transparent,
                        Image = stampBitmap,
                        Cursor = Cursors.Hand
                    };
                    PositionPreviewOverlay(overlay, placement.X, placement.Y);
                    AttachPreviewGestureHandlers(overlay);
                    overlay.DoubleClick += pictureBox2_DoubleClick;
                    overlay.BringToFront();
                    previewStampOverlays[placement.Id] = overlay;
                }
                catch (Exception)
                {
                    // Ignore an invalid individual stamp so other placements remain visible.
                }
            }
        }

        private void ClearDynamicPreviewOverlays()
        {
            foreach (PictureBox overlay in previewStampOverlays.Values.ToList())
            {
                if (overlay == pictureBox2)
                {
                    continue;
                }

                overlay.DoubleClick -= pictureBox2_DoubleClick;
                System.Drawing.Image image = overlay.Image;
                overlay.Image = null;
                image?.Dispose();
                overlay.Dispose();
                previewStampOverlays.Remove(Convert.ToInt32(overlay.Tag));
            }
        }

        private void PositionPreviewOverlay(PictureBox overlay, float px, float py)
        {
            int picw = Math.Max(0, pictureBox1.Width - overlay.Width);
            int pich = Math.Max(0, pictureBox1.Height - overlay.Height);
            int x = picw == 0 ? 0 : Convert.ToInt32(picw * px);
            int y = pich == 0 ? 0 : Convert.ToInt32(pich * py);
            overlay.Location = new Point(x, y);
        }

        private PreviewOverlayRequest CreatePreviewOverlayRequest(float px, float py, bool visible)
        {
            return new PreviewOverlayRequest
            {
                Px = px,
                Py = py,
                Visible = visible,
                StampPath = comboBoxYz.SelectedValue == null ? "" : comboBoxYz.SelectedValue.ToString(),
                SizeMm = GetPreviewSizeValue(),
                Opacity = GetPreviewOpacityValue(),
                Rotation = GetPreviewRotationValue(),
                WhiteTransparencyTolerance = GetWhiteTransparencyTolerance(),
                UseWhiteTransparency = cbxTransColor.Checked,
                UseOriginalRotationCrop = qbflag == 0,
                PreviewWidth = pictureBox1.Width,
                PdfWidth = pictureBox1.Image == null
                    ? pictureBox1.Width
                    : (int)Math.Round(pictureBox1.Image.Width * 72f / previewRenderDpi),
                FallbackSquareSize = yzr * 2
            };
        }

        private void QueuePreviewOverlayRefresh(PreviewOverlayRequest request)
        {
            int requestId = previewOverlayRequestGate.BeginNext();
            CancellationTokenSource nextPreviewOverlayCts = new CancellationTokenSource();
            CancellationTokenSource previousPreviewOverlayCts = Interlocked.Exchange(ref previewOverlayCts, nextPreviewOverlayCts);
            if (previousPreviewOverlayCts != null)
            {
                previousPreviewOverlayCts.Cancel();
                previousPreviewOverlayCts.Dispose();
            }

            _ = RenderPreviewOverlayAsync(request, requestId, nextPreviewOverlayCts.Token);
        }

        private async Task RenderPreviewOverlayAsync(PreviewOverlayRequest request, int requestId, CancellationToken token)
        {
            PreviewOverlayRenderResult renderResult = null;
            try
            {
                renderResult = await Task.Run(() => CreatePreviewOverlayRenderResult(request, token), token);
                token.ThrowIfCancellationRequested();

                if (!previewOverlayRequestGate.IsCurrent(requestId) || IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                PreviewOverlayRenderResult completedRenderResult = renderResult;
                BeginInvoke(new Action(() => ApplyPreviewOverlayRenderResult(request, requestId, completedRenderResult)));
                renderResult = null;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                if (renderResult != null)
                {
                    renderResult.Dispose();
                }
            }
        }

        private PreviewOverlayRenderResult CreatePreviewOverlayRenderResult(PreviewOverlayRequest request, CancellationToken token)
        {
            Size overlaySize = new Size(request.FallbackSquareSize, request.FallbackSquareSize);
            if (string.IsNullOrWhiteSpace(request.StampPath) || !File.Exists(request.StampPath))
            {
                return PreviewOverlayRenderResult.Empty(overlaySize);
            }

            using (Bitmap sourceStamp = new Bitmap(request.StampPath))
            {
                overlaySize = PreviewStampLayout.CalculateOverlaySize(
                    configuredWidthMm: request.SizeMm,
                    imageDpi: sourceStamp.HorizontalResolution,
                    imagePixelWidth: sourceStamp.Width,
                    imagePixelHeight: sourceStamp.Height,
                    previewWidth: request.PreviewWidth,
                    pdfWidth: request.PdfWidth,
                    fallbackSquareSize: request.FallbackSquareSize);

                Bitmap processedStamp = new Bitmap(sourceStamp);
                if (request.UseWhiteTransparency)
                {
                    processedStamp.Dispose();
                    processedStamp = GetTransparentPreviewStamp(request.StampPath, request.WhiteTransparencyTolerance);
                }

                token.ThrowIfCancellationRequested();

                if (request.Opacity < 100)
                {
                    processedStamp = SetImageOpacity(processedStamp, request.Opacity);
                }

                if (request.Rotation != 0)
                {
                    processedStamp = RotateImg(processedStamp, request.Rotation, request.UseOriginalRotationCrop);
                }

                token.ThrowIfCancellationRequested();
                return PreviewOverlayRenderResult.Success(processedStamp, overlaySize);
            }
        }

        private void ApplyPreviewOverlayRenderResult(PreviewOverlayRequest request, int requestId, PreviewOverlayRenderResult renderResult)
        {
            using (renderResult)
            {
                if (renderResult == null || !previewOverlayRequestGate.IsCurrent(requestId) || IsDisposed)
                {
                    return;
                }

                if (!renderResult.HasPreview)
                {
                    ReplacePreviewOverlayImage(null);
                    ApplyIdleStampOverlaySize();
                    pictureBox2.Visible = false;
                    return;
                }

                ReplacePreviewOverlayImage(renderResult.DetachBitmap());
                pictureBox2.Size = renderResult.OverlaySize;
                pictureBox2.Visible = request.Visible;

                if (request.Visible)
                {
                    PositionPreviewOverlay(request.Px, request.Py);
                }
            }
        }

        private void ReplacePreviewOverlayImage(Bitmap newImage)
        {
            if (ReferenceEquals(pictureBox2.Image, newImage))
            {
                return;
            }

            System.Drawing.Image oldImage = pictureBox2.Image;
            pictureBox2.Image = newImage;
            if (oldImage != null)
            {
                oldImage.Dispose();
            }
        }

        private void PositionPreviewOverlay(float px, float py)
        {
            Size overlaySize = GetCurrentPreviewOverlaySize();
            int picw = Math.Max(0, pictureBox1.Width - overlaySize.Width);
            int pich = Math.Max(0, pictureBox1.Height - overlaySize.Height);
            int x = picw == 0 ? 0 : Convert.ToInt32(picw * px);
            int y = pich == 0 ? 0 : Convert.ToInt32(pich * py);
            pictureBox2.Location = new Point(x, y);
        }

        private Size GetCurrentPreviewOverlaySize()
        {
            return pictureBox2.Size.IsEmpty ? new Size(yzr * 2, yzr * 2) : pictureBox2.Size;
        }

        private bool TryGetCurrentOverlayPosition(out float px, out float py)
        {
            px = Convert.ToSingle(textPx.Text);
            py = Convert.ToSingle(textPy.Text);

            if (viewPdfimgs == null || previewPageCache == null || imgStartPage <= 0)
            {
                return false;
            }

            StampPlacement placement = stampPlacements.ForPage(previewPath, imgStartPage).LastOrDefault();
            if (placement != null)
            {
                px = placement.X;
                py = placement.Y;
                return true;
            }

            if (IsPlacementStampType(comboYz.SelectedIndex) || comboQfz.SelectedIndex == 4)
            {
                return false;
            }

            return true;
        }

        private int GetPreviewSizeValue()
        {
            return PreviewValueSanitizer.ClampInt(textCC.Text, 40, 0, 100);
        }

        private int GetPreviewOpacityValue()
        {
            return PreviewValueSanitizer.ClampInt(textOpacity.Text, 60, 0, 100);
        }

        private int GetPreviewRotationValue()
        {
            return PreviewValueSanitizer.ClampInt(textRotation.Text, 0, -360, 360);
        }

        private int GetWhiteTransparencyTolerance()
        {
            return WhiteTransparencyHelper.ClampTolerance(PreviewValueSanitizer.ClampInt(txtAllow.Text, 20, 0, 50));
        }

        private Bitmap GetTransparentPreviewStamp(string stampPath, int tolerance)
        {
            string cacheKey = stampPath + "|" + tolerance.ToString();
            lock (transparentStampCacheSync)
            {
                if (cachedTransparentStamp == null || cachedTransparentStampKey != cacheKey)
                {
                    ClearTransparentStampCache();
                    using (Bitmap sourceStamp = new Bitmap(stampPath))
                    {
                        cachedTransparentStamp = WhiteTransparencyHelper.Apply(sourceStamp, tolerance);
                    }

                    cachedTransparentStampKey = cacheKey;
                }

                return new Bitmap(cachedTransparentStamp);
            }
        }

        private void ClearTransparentStampCache()
        {
            lock (transparentStampCacheSync)
            {
                if (cachedTransparentStamp != null)
                {
                    cachedTransparentStamp.Dispose();
                    cachedTransparentStamp = null;
                }

                cachedTransparentStampKey = "";
            }
        }

        private void CancelPreviewOverlayRefresh()
        {
            CancellationTokenSource currentPreviewOverlayCts = Interlocked.Exchange(ref previewOverlayCts, null);
            if (currentPreviewOverlayCts != null)
            {
                currentPreviewOverlayCts.Cancel();
                currentPreviewOverlayCts.Dispose();
            }
        }

        private void previewUnsigned_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back && e.KeyChar != (char)Keys.Delete)
            {
                e.Handled = true;
            }
        }

        private void previewSigned_KeyPress(object sender, KeyPressEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            bool isMinus = e.KeyChar == '-';
            bool isControl = e.KeyChar == (char)Keys.Back || e.KeyChar == (char)Keys.Delete;
            bool allowLeadingMinus = isMinus && textBox.SelectionStart == 0 && !textBox.Text.Contains("-");
            if (!char.IsDigit(e.KeyChar) && !isControl && !allowLeadingMinus)
            {
                e.Handled = true;
            }
        }

        private void previewSize_Leave(object sender, EventArgs e)
        {
            NormalizeUnsignedPreviewTextBox(textCC, true);
            RefreshPreviewOverlay();
        }

        private void previewOpacity_Leave(object sender, EventArgs e)
        {
            NormalizeUnsignedPreviewTextBox(textOpacity, true);
            RefreshPreviewOverlay();
        }

        private void previewRotation_Leave(object sender, EventArgs e)
        {
            NormalizeSignedPreviewTextBox(textRotation, true);
            RefreshPreviewOverlay();
        }

        private void NormalizeUnsignedPreviewTextBox(TextBox textBox, bool clamp)
        {
            string filtered = PreviewValueSanitizer.KeepDigitsOnly(textBox.Text);
            if (filtered != textBox.Text)
            {
                int selectionStart = textBox.SelectionStart;
                textBox.Text = filtered;
                textBox.SelectionStart = Math.Min(selectionStart, textBox.Text.Length);
            }

            if (clamp)
            {
                int value = ReferenceEquals(textBox, textCC) ? GetPreviewSizeValue() : GetPreviewOpacityValue();
                int fallbackValue = ReferenceEquals(textBox, textCC) ? 40 : 60;
                textBox.Text = (textBox.Text.Length == 0 ? fallbackValue : value).ToString();
            }
        }

        private void NormalizeSignedPreviewTextBox(TextBox textBox, bool clamp)
        {
            string filtered = PreviewValueSanitizer.KeepSignedInteger(textBox.Text);
            if (filtered != textBox.Text)
            {
                int selectionStart = textBox.SelectionStart;
                textBox.Text = filtered;
                textBox.SelectionStart = Math.Min(selectionStart, textBox.Text.Length);
            }

            if (clamp)
            {
                textBox.Text = GetPreviewRotationValue().ToString();
            }
        }
        private void BeginSpecifiedPageStampMode()
        {
            if (previewPdfRenderer == null || string.IsNullOrWhiteSpace(previewPath) || imgPageCount < 1)
            {
                SetOperationHint("请先加载 PDF，再设置指定范围。", true);
                MessageBox.Show("请先加载 PDF，再设置指定范围。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (comboBoxYz.SelectedValue == null)
            {
                MessageBox.Show("请先选择印章！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RevertStampTypeSelection();
                return;
            }
            string specifiedStampPath = comboBoxYz.SelectedValue.ToString();
            if (!File.Exists(specifiedStampPath))
            {
                MessageBox.Show("印章图片不存在，请重新选择。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RevertStampTypeSelection();
                return;
            }

            using (PageRangeDialog dialog = new PageRangeDialog(imgPageCount, specifiedPageRange))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (!PageRange.TryCreate(dialog.StartPage.ToString(), dialog.EndPage.ToString(), imgPageCount, out PageRange range, out string error))
                {
                    SetOperationHint(error, true);
                    MessageBox.Show(error, "页码范围错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                specifiedPageRange = range;
                specifiedRangeFirstClickPending = true;
                activeSpecifiedBatchId = 0;
                yzType = SpecifiedPageStampType;
                comboYz.SelectedIndex = SpecifiedPageStampType;
                imgStartPage = range.EndPage;
                SynchronizeVisibleModeControls();
                UpdatePlacementOperationHint();
                _ = viewPDFPage();
            }
        }

        //兼容旧的下拉框事件入口。
        private void comboYz_SelectionChangeCommitted(object sender, EventArgs e)
        {
            if (comboYz.SelectedIndex == SpecifiedPageStampType)
            {
                BeginSpecifiedPageStampMode();
                return;
            }

            yzType = comboYz.SelectedIndex;
            lastCommittedYzType = yzType;
            SynchronizeVisibleModeControls();
            UpdatePlacementOperationHint();
        }

        private void RevertStampTypeSelection()
        {
            int fallback = lastCommittedYzType >= 0 && lastCommittedYzType < comboYz.Items.Count ? lastCommittedYzType : CustomPlacementStampType;
            suppressYzSelectionChange = true;
            comboYz.SelectedIndex = fallback;
            suppressYzSelectionChange = false;
            yzType = comboYz.SelectedIndex;
            ApplyPreviewPanelLayout(comboYz.SelectedIndex, comboQfz.SelectedIndex);
        }


        private void comboQfz_SelectionChangeCommitted(object sender, EventArgs e)
        {
            qfzType = comboQfz.SelectedIndex;
            SynchronizeVisibleModeControls();
            UpdatePlacementOperationHint();
        }

        private void ApplyPreviewPanelLayout(int stampType, int seamStampType)
        {
            LayoutPreviewPage();
        }
        //只允许录入数字
        private void txtAllow_KeyPress(object sender, KeyPressEventArgs e)
        {
            // 允许数字、退格键（Backspace）和删除键（Delete）
            if (!char.IsDigit(e.KeyChar) && e.KeyChar != (char)Keys.Back && e.KeyChar != (char)Keys.Delete)
            {
                e.Handled = true;   // 拦截非法字符
            }
        }
        //防粘右键粘贴非数字
        private void txtAllow_TextChanged(object sender, EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            string filtered = new string(tb.Text.Where(c => char.IsDigit(c)).ToArray());

            if (tb.Text != filtered)
            {
                int sel = tb.SelectionStart;
                tb.Text = filtered;
                tb.SelectionStart = Math.Min(sel, tb.Text.Length);
            }

            ClearTransparentStampCache();
            RefreshPreviewOverlay();
        }

        private sealed class PreviewOverlayRequest
        {
            public float Px { get; set; }
            public float Py { get; set; }
            public bool Visible { get; set; }
            public string StampPath { get; set; }
            public int SizeMm { get; set; }
            public int Opacity { get; set; }
            public int Rotation { get; set; }
            public int WhiteTransparencyTolerance { get; set; }
            public bool UseWhiteTransparency { get; set; }
            public bool UseOriginalRotationCrop { get; set; }
            public int PreviewWidth { get; set; }
            public int PdfWidth { get; set; }
            public int FallbackSquareSize { get; set; }
        }

        private sealed class PreviewOverlayRenderResult : IDisposable
        {
            private PreviewOverlayRenderResult(Bitmap bitmap, Size overlaySize, bool hasPreview)
            {
                Bitmap = bitmap;
                OverlaySize = overlaySize;
                HasPreview = hasPreview;
            }

            public Bitmap Bitmap { get; private set; }
            public Size OverlaySize { get; }
            public bool HasPreview { get; }

            public static PreviewOverlayRenderResult Empty(Size overlaySize)
            {
                return new PreviewOverlayRenderResult(null, overlaySize, false);
            }

            public static PreviewOverlayRenderResult Success(Bitmap bitmap, Size overlaySize)
            {
                return new PreviewOverlayRenderResult(bitmap, overlaySize, true);
            }

            public Bitmap DetachBitmap()
            {
                Bitmap bitmap = Bitmap;
                Bitmap = null;
                return bitmap;
            }

            public void Dispose()
            {
                if (Bitmap != null)
                {
                    Bitmap.Dispose();
                    Bitmap = null;
                }
            }
        }
    }
}
