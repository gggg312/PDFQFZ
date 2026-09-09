using System;
using System.Collections.Generic;
using System.IO;
using PDFQFZ.Library;

namespace PDFQFZ.WPF.Services
{
    /// <summary>
    /// 程序配置：读写程序目录下 config.ini（[config] 节）+ 按文字盖章历史。
    /// 字段口径与原 WinForms 版 Form1.cs 保持一致。
    /// </summary>
    internal static class AppConfig
    {
        private const string Section = "config";
        private const string AutoStampHistoryIniKey = "autoStampHistory";
        private const string AutoStampHistorySeparator = "||";
        private const int MaxAutoStampHistory = 10;

        private const int CustomPlacementStampType = 1;
        private const int SpecifiedPageStampType = 2;

        // ---------- 配置字段 ----------
        public static int WjType = 1;         // 0目录/1文件
        public static int QfzType = 1;        // 骑缝章 0加盖/1不加/2单页/3双页/4随意
        public static int YzType = 1;         // 盖章方式 0无/1手动/2指定范围（默认手动点击盖章）
        public static int QmType = 0;         // 数字签名 0不使用/1自生成/2自定义
        public static int WzType = 3;         // 骑缝章位置（comboSeamPosition 索引：0上/1下/2左/3右）
        public static int QbFlag = 0;         // 旋转切边 0切/1不切
        public static int Size = 20;          // 印章尺寸mm
        public static int Rotation = 0;       // 旋转角度
        public static int Opacity = 100;      // 不透明度
        public static int WzPercent = 50;     // 骑缝章位置%
        public static int MaxFgs = 500;       // 最大分割数（默认500，满足水印/大面积骑缝章场景）
        public static int OutputQualityDpi = 150; // 合并模式输出清晰度（300/200/150/96/72，默认标准150）
        public static int YzIndex = -1;       // 印章索引（历史兼容，WPF 以路径为准）
        public static string SignText = "";   // 签名文本/证书名
        public static string Password = "";   // 签名密码
        public static int FixType = 0;        // 输出后缀类型
        public static string FixStr = "";     // 输出后缀文本
        public static string FixStr2 = "_加密"; // 加密后缀
        public static string SignBuiltInPath = "";
        public static string SignBuiltInPass = "";
        public static string SignCustomPath = "";
        public static string SignCustomPass = "";
        public static string LastStampImagePath = ""; // 上次选择的印章路径（WPF 新增，便于记忆）
        public static int WindowWidth = 1280;
        public static int WindowHeight = 840;
        public static int WindowLeft = -1;
        public static int WindowTop = -1;
        public static int LeftPanelWidth = -1;  // 分隔条拖动后的左栏宽度（px，-1=未拖动过，用默认比例）

        // 按文字盖章——上下文过滤（WPF 新增）
        public static bool ContextFilterEnabled = false; // 是否启用附近关键词过滤（默认关闭）
        public static string ContextKeywords = "盖章,公章"; // 附近关键词，逗号分隔
        public static int ContextRange = 10;            // 上下文范围（字，默认10）
        public static int ContextMatch = 0;             // 0=任一关键词（或），1=全部关键词（且）

        // 左侧区域折叠状态（1=展开，0=收起；按文字盖章默认展开，印章参数/其他设置默认折叠，关闭时保存）
        public static int FoldAutoText = 1;
        public static int FoldSealParams = 0;
        public static int FoldOther = 0;

        public static string IniPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"); }
        }

        /// <summary>印章列表在 config.ini 中的键名（V2.0.1 起从 yz.log 合并到此）。</summary>
        private const string StampPathsIniKey = "stampPaths";
        /// <summary>印章路径分隔符（分号，路径中不会出现）。</summary>
        private static readonly char[] StampPathsSeparator = new[] { ';' };

        /// <summary>旧版印章列表文件（V2.0.1 前使用，仅用于迁移）。</summary>
        public static string StampsLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yz.log"); }
        }

        public static string CertDefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pdfqfz.pfx"); }
        }

        /// <summary>启动时从 config.ini 加载全部配置。</summary>
        public static void LoadFromIni()
        {
            try
            {
                if (!File.Exists(IniPath))
                {
                    return;
                }

                IniFileHelper ini = new IniFileHelper(IniPath);
                WjType = ini.GetIniInt(Section, "wjType", WjType);
                QfzType = ini.GetIniInt(Section, "qfzType", QfzType);
                string yzTypeRaw = Content(ini, "yzType", "");
                string yzTypeVersion = Content(ini, "yzTypeVersion", "");
                YzType = NormalizeStampTypeFromConfig(yzTypeRaw, yzTypeVersion);
                QmType = ini.GetIniInt(Section, "qmType", QmType);
                WzType = ini.GetIniInt(Section, "wzType", WzType);
                QbFlag = ini.GetIniInt(Section, "qbflag", QbFlag);
                Size = ini.GetIniInt(Section, "size", Size);
                Rotation = ini.GetIniInt(Section, "rotation", Rotation);
                Opacity = ini.GetIniInt(Section, "opacity", Opacity);
                WzPercent = ini.GetIniInt(Section, "wz", WzPercent);
                MaxFgs = ini.GetIniInt(Section, "maxfgs", MaxFgs);
                OutputQualityDpi = ini.GetIniInt(Section, "outputQualityDpi", OutputQualityDpi);
                YzIndex = ini.GetIniInt(Section, "yzIndex", YzIndex);
                SignText = Content(ini, "signText", SignText);
                Password = Content(ini, "signPassword", Password);
                FixType = ini.GetIniInt(Section, "fixType", FixType);
                FixStr = Content(ini, "fixStr", FixStr);
                FixStr2 = Content(ini, "fixStr2", FixStr2);
                SignBuiltInPath = Content(ini, "signBuiltInPath", SignBuiltInPath);
                SignBuiltInPass = Content(ini, "signBuiltInPass", SignBuiltInPass);
                SignCustomPath = Content(ini, "signCustomPath", SignCustomPath);
                SignCustomPass = Content(ini, "signCustomPass", SignCustomPass);
                LastStampImagePath = Content(ini, "lastStampImagePath", LastStampImagePath);
                WindowWidth = ini.GetIniInt(Section, "windowWidth", WindowWidth);
                WindowHeight = ini.GetIniInt(Section, "windowHeight", WindowHeight);
                WindowLeft = ini.GetIniInt(Section, "windowLeft", WindowLeft);
                WindowTop = ini.GetIniInt(Section, "windowTop", WindowTop);
                LeftPanelWidth = ini.GetIniInt(Section, "leftPanelWidth", LeftPanelWidth);
                ContextKeywords = Content(ini, "contextKeywords", ContextKeywords);
                ContextRange = ini.GetIniInt(Section, "contextRange", ContextRange);
                ContextMatch = ini.GetIniInt(Section, "contextMatch", ContextMatch);
                ContextFilterEnabled = ini.GetIniInt(Section, "contextFilterEnabled", 0) == 1;
                FoldAutoText = ini.GetIniInt(Section, "foldAutoText", FoldAutoText);
                FoldSealParams = ini.GetIniInt(Section, "foldSealParams", FoldSealParams);
                FoldOther = ini.GetIniInt(Section, "foldOther", FoldOther);
            }
            catch
            {
                // 读取失败时保持默认值
            }
        }

        /// <summary>保存窗口状态（关闭时调用）。</summary>
        /// <summary>立即把分隔条位置写入配置文件（不依赖窗口关闭，拖动完就落盘，防止异常退出丢失）。</summary>
        public static void SaveLeftPanelWidth()
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniInt(Section, "leftPanelWidth", LeftPanelWidth);
            }
            catch
            {
            }
        }

        /// <summary>窗口关闭时保存窗口位置/大小（连同分隔条位置）。</summary>
        public static void SaveWindowState(double left, double top, double width, double height)
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniInt(Section, "windowWidth", (int)width);
                ini.WriteIniInt(Section, "windowHeight", (int)height);
                ini.WriteIniInt(Section, "windowLeft", (int)left);
                ini.WriteIniInt(Section, "windowTop", (int)top);
                ini.WriteIniInt(Section, "leftPanelWidth", LeftPanelWidth);
                ini.WriteIniInt(Section, "foldAutoText", FoldAutoText);
                ini.WriteIniInt(Section, "foldSealParams", FoldSealParams);
                ini.WriteIniInt(Section, "foldOther", FoldOther);
            }
            catch
            {
            }
        }

        /// <summary>保存界面关键配置（更改时调用）。</summary>
        public static void SaveUiConfig()
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniInt(Section, "wjType", WjType);
                ini.WriteIniInt(Section, "qfzType", QfzType);
                ini.WriteIniInt(Section, "yzType", YzType);
                ini.WriteIniInt(Section, "yzTypeVersion", 3);
                ini.WriteIniInt(Section, "qmType", QmType);
                ini.WriteIniInt(Section, "wzType", WzType);
                ini.WriteIniInt(Section, "qbflag", QbFlag);
                ini.WriteIniInt(Section, "size", Size);
                ini.WriteIniInt(Section, "rotation", Rotation);
                ini.WriteIniInt(Section, "opacity", Opacity);
                ini.WriteIniInt(Section, "wz", WzPercent);
                ini.WriteIniInt(Section, "maxfgs", MaxFgs);
                ini.WriteIniInt(Section, "outputQualityDpi", OutputQualityDpi);
                ini.WriteIniInt(Section, "yzIndex", YzIndex);
                ini.WriteIniString(Section, "fixStr", FixStr);
                ini.WriteIniString(Section, "fixStr2", FixStr2);
                ini.WriteIniString(Section, "lastStampImagePath", LastStampImagePath);
                ini.WriteIniString(Section, "contextKeywords", ContextKeywords ?? "");
                ini.WriteIniInt(Section, "contextRange", ContextRange);
                ini.WriteIniInt(Section, "contextMatch", ContextMatch);
                ini.WriteIniInt(Section, "contextFilterEnabled", ContextFilterEnabled ? 1 : 0);
            }
            catch
            {
            }
        }

        /// <summary>盖章成功后保存一次签名配置（记住用户最近用的签名证书）。</summary>
        public static void SaveSuccessfulStampConfig()
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                if (QmType == 1)
                {
                    SignBuiltInPath = SignText;
                    SignBuiltInPass = Password;
                    ini.WriteIniInt(Section, "fixType", FixType);
                    ini.WriteIniString(Section, "signBuiltInPath", SignBuiltInPath);
                    ini.WriteIniString(Section, "signBuiltInPass", SignBuiltInPass);
                }
                else if (QmType == 2)
                {
                    SignCustomPath = SignText;
                    SignCustomPass = Password;
                    ini.WriteIniInt(Section, "fixType", FixType);
                    ini.WriteIniString(Section, "signCustomPath", SignCustomPath);
                    ini.WriteIniString(Section, "signCustomPass", SignCustomPass);
                }
            }
            catch
            {
            }
        }

        // ---------- 印章列表（V2.0.1 起合并到 config.ini 的 stampPaths 键） ----------

        /// <summary>读取印章图片路径列表（保留顺序，不做去重；首次运行自动从旧版 yz.log 迁移）。</summary>
        public static List<string> LoadStampPaths()
        {
            List<string> result = new List<string>();
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string raw = Content(ini, StampPathsIniKey, "");

                // 旧用户迁移：config.ini 中没有 stampPaths，但 yz.log 存在 → 自动迁移
                if (string.IsNullOrWhiteSpace(raw) && File.Exists(StampsLogPath))
                {
                    List<string> oldPaths = new List<string>();
                    foreach (string line in File.ReadAllLines(StampsLogPath))
                    {
                        string p = line.Trim();
                        if (p.Length > 0) oldPaths.Add(p);
                    }
                    if (oldPaths.Count > 0)
                    {
                        SaveStampPathsInternal(oldPaths);
                        try { File.Delete(StampsLogPath); } catch { }
                        return oldPaths;
                    }
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    foreach (string item in raw.Split(StampPathsSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string path = item.Trim();
                        if (path.Length > 0)
                        {
                            result.Add(path);
                        }
                    }
                }
            }
            catch
            {
                // 读取失败时返回空列表
            }
            return result;
        }

        /// <summary>内部方法：将印章路径列表写入 config.ini（用分号分隔）。</summary>
        private static void SaveStampPathsInternal(List<string> paths)
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, StampPathsIniKey, string.Join(";", paths));
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>追加印章图片路径到 config.ini（去重，新路径加到末尾）。</summary>
        public static void AppendStampPaths(IEnumerable<string> paths)
        {
            try
            {
                List<string> existing = LoadStampPaths();
                foreach (string p in paths)
                {
                    string path = p.Trim();
                    if (path.Length > 0 && !existing.Exists(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Add(path);
                    }
                }
                SaveStampPathsInternal(existing);
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>从 config.ini 删除指定印章路径（大小写不敏感）。</summary>
        public static void RemoveStampPath(string path)
        {
            try
            {
                List<string> paths = LoadStampPaths();
                paths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                SaveStampPathsInternal(paths);
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        // ---------- 按文字盖章历史 ----------

        /// <summary>读取已保存的盖章文字历史（最新在前）。</summary>
        public static List<string> LoadAutoStampHistory()
        {
            List<string> result = new List<string>();
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string raw = ini.ContentValue(Section, AutoStampHistoryIniKey);
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
            }
            return result;
        }

        /// <summary>记录一条盖章文字（最新在前、去重、最多 10 条）。</summary>
        public static void RecordAutoStampKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            try
            {
                List<string> history = LoadAutoStampHistory();
                history.RemoveAll(x => string.Equals(x, keyword, StringComparison.Ordinal));
                history.Insert(0, keyword.Trim());
                if (history.Count > MaxAutoStampHistory)
                {
                    history.RemoveRange(MaxAutoStampHistory, history.Count - MaxAutoStampHistory);
                }

                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, AutoStampHistoryIniKey, string.Join(AutoStampHistorySeparator, history));
            }
            catch
            {
            }
        }

        /// <summary>从历史中删除指定文字（下拉项右侧删除叉）。</summary>
        public static void RemoveAutoStampKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            try
            {
                List<string> history = LoadAutoStampHistory();
                history.RemoveAll(x => string.Equals(x, keyword, StringComparison.Ordinal));
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, AutoStampHistoryIniKey, string.Join(AutoStampHistorySeparator, history));
            }
            catch
            {
            }
        }

        // ---------- 按印章保存参数（每个印章记住自己上次用的尺寸/旋转/不透明度等 7 项） ----------

        /// <summary>单个印章的参数快照（随印章文件名分别保存，切换印章时恢复）。</summary>
        public sealed class StampParams
        {
            public int Size = 40;            // 印章尺寸 mm
            public int Rotation = 0;         // 旋转角度
            public int RotationHandle = 0;   // 旋转处理 0=旋转切边 1=不切边
            public int Opacity = 60;         // 不透明度 %
            public bool RandomParams = false;// 盖章随机旋转
            public int RandomRange = 5;       // 盖章随机旋转角度范围（±N°）
            public int RandomOffsetMm = 5;    // 盖章随机位移距离（任意方向 0~N mm）
            public bool RemoveWhite = false; // 去除白色背景
            public int Tolerance = 20;       // 容差
            public int MaxSplit = 500;       // 骑缝章最大分割数（随印章记忆，默认500）
        }

        private static string StampParamSection(string stampFileName)
        {
            return "stamp_" + (string.IsNullOrWhiteSpace(stampFileName) ? "default" : stampFileName.Trim());
        }

        /// <summary>读取某印章上次保存的参数；未保存过则返回默认值。</summary>
        public static StampParams LoadStampParams(string stampFileName)
        {
            StampParams p = new StampParams();
            if (string.IsNullOrWhiteSpace(stampFileName))
            {
                return p;
            }
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string sec = StampParamSection(stampFileName);
                p.Size = ini.GetIniInt(sec, "size", p.Size);
                p.Rotation = ini.GetIniInt(sec, "rotation", p.Rotation);
                p.RotationHandle = ini.GetIniInt(sec, "rotationHandle", p.RotationHandle);
                p.Opacity = ini.GetIniInt(sec, "opacity", p.Opacity);
                p.RandomParams = ini.GetIniInt(sec, "randomParams", 0) == 1;
                p.RandomRange = ini.GetIniInt(sec, "randomRange", p.RandomRange);
                p.RandomOffsetMm = ini.GetIniInt(sec, "randomOffsetMm", p.RandomOffsetMm);
                p.RemoveWhite = ini.GetIniInt(sec, "removeWhite", 0) == 1;
                p.Tolerance = ini.GetIniInt(sec, "tolerance", p.Tolerance);
                p.MaxSplit = ini.GetIniInt(sec, "maxSplit", p.MaxSplit);
            }
            catch
            {
                // 读取失败保持默认
            }
            return p;
        }

        /// <summary>保存某印章当前参数（切换印章或关闭程序时调用）。</summary>
        public static void SaveStampParams(string stampFileName, StampParams p)
        {
            if (string.IsNullOrWhiteSpace(stampFileName) || p == null)
            {
                return;
            }
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string sec = StampParamSection(stampFileName);
                ini.WriteIniInt(sec, "size", p.Size);
                ini.WriteIniInt(sec, "rotation", p.Rotation);
                ini.WriteIniInt(sec, "rotationHandle", p.RotationHandle);
                ini.WriteIniInt(sec, "opacity", p.Opacity);
                ini.WriteIniInt(sec, "randomParams", p.RandomParams ? 1 : 0);
                ini.WriteIniInt(sec, "randomRange", p.RandomRange);
                ini.WriteIniInt(sec, "randomOffsetMm", p.RandomOffsetMm);
                ini.WriteIniInt(sec, "removeWhite", p.RemoveWhite ? 1 : 0);
                ini.WriteIniInt(sec, "tolerance", p.Tolerance);
                if (p.MaxSplit > 0) ini.WriteIniInt(sec, "maxSplit", p.MaxSplit);
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        // ---------- 兼容转换 ----------

        private static string Content(IniFileHelper ini, string key, string defaultValue)
        {
            string value = ini.ContentValue(Section, key);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        private static int NormalizeStampTypeFromConfig(string value, string version)
        {
            int configuredType = ToIntOrDefault(value, 0);
            if (version == "3")
            {
                return configuredType >= 0 && configuredType <= SpecifiedPageStampType ? configuredType : 0;
            }

            if (version == "2")
            {
                if (configuredType == 3)
                {
                    return SpecifiedPageStampType;
                }
                return configuredType == 2 ? CustomPlacementStampType : 0;
            }

            if (configuredType == 4 || configuredType == 3)
            {
                return CustomPlacementStampType;
            }

            if (configuredType >= 5)
            {
                return SpecifiedPageStampType;
            }

            return configuredType == 0 ? 0 : CustomPlacementStampType;
        }

        private static int ToIntOrDefault(string text, int defaultValue)
        {
            return int.TryParse(text, out int value) ? value : defaultValue;
        }
    }
}
