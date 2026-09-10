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
        private const string AutoStampOffsetIniKey = "autoStampCenterOffsets";
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

        // ---------- 盖章渲染方案（4 组，每组 7 参数 + 是否已保存） ----------
        public static bool TexPreset1Exists = false, TexPreset2Exists = false, TexPreset3Exists = false, TexPreset4Exists = false;
        public static int TexPreset1Brightness = 0, TexPreset1Blob = 0, TexPreset1Gradient = 0, TexPreset1White = 0,
            TexPreset1Spot = 0, TexPreset1Radial = 0, TexPreset1Cast = 0;
        public static int TexPreset2Brightness = 0, TexPreset2Blob = 0, TexPreset2Gradient = 0, TexPreset2White = 0,
            TexPreset2Spot = 0, TexPreset2Radial = 0, TexPreset2Cast = 0;
        public static int TexPreset3Brightness = 0, TexPreset3Blob = 0, TexPreset3Gradient = 0, TexPreset3White = 0,
            TexPreset3Spot = 0, TexPreset3Radial = 0, TexPreset3Cast = 0;
        public static int TexPreset4Brightness = 0, TexPreset4Blob = 0, TexPreset4Gradient = 0, TexPreset4White = 0,
            TexPreset4Spot = 0, TexPreset4Radial = 0, TexPreset4Cast = 0;
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
        /// <summary>印章勾选集合（显示名，按勾选顺序；多选随机盖章使用）。</summary>
        public static List<string> LastSelectedStampNames = new List<string>();
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
        public static bool ContextExcludeSpaces = false; // 上下文范围是否排除空格（默认关闭=空格计入）

        // 左侧区域折叠状态（1=展开，0=收起；按文字盖章默认收起，印章参数/其他设置默认折叠，关闭时保存）
        public static int FoldAutoText = 0;
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
                TexPreset1Exists = ini.GetIniInt(Section, "texPreset1Exists", 0) == 1;
                TexPreset1Brightness = ini.GetIniInt(Section, "texPreset1Brightness", 0);
                TexPreset1Blob = ini.GetIniInt(Section, "texPreset1Blob", 0);
                TexPreset1Gradient = ini.GetIniInt(Section, "texPreset1Gradient", 0);
                TexPreset1White = ini.GetIniInt(Section, "texPreset1White", 0);
                TexPreset1Spot = ini.GetIniInt(Section, "texPreset1Spot", 0);
                TexPreset1Radial = ini.GetIniInt(Section, "texPreset1Radial", 0);
                TexPreset1Cast = ini.GetIniInt(Section, "texPreset1Cast", 0);
                TexPreset2Exists = ini.GetIniInt(Section, "texPreset2Exists", 0) == 1;
                TexPreset2Brightness = ini.GetIniInt(Section, "texPreset2Brightness", 0);
                TexPreset2Blob = ini.GetIniInt(Section, "texPreset2Blob", 0);
                TexPreset2Gradient = ini.GetIniInt(Section, "texPreset2Gradient", 0);
                TexPreset2White = ini.GetIniInt(Section, "texPreset2White", 0);
                TexPreset2Spot = ini.GetIniInt(Section, "texPreset2Spot", 0);
                TexPreset2Radial = ini.GetIniInt(Section, "texPreset2Radial", 0);
                TexPreset2Cast = ini.GetIniInt(Section, "texPreset2Cast", 0);
                TexPreset3Exists = ini.GetIniInt(Section, "texPreset3Exists", 0) == 1;
                TexPreset3Brightness = ini.GetIniInt(Section, "texPreset3Brightness", 0);
                TexPreset3Blob = ini.GetIniInt(Section, "texPreset3Blob", 0);
                TexPreset3Gradient = ini.GetIniInt(Section, "texPreset3Gradient", 0);
                TexPreset3White = ini.GetIniInt(Section, "texPreset3White", 0);
                TexPreset3Spot = ini.GetIniInt(Section, "texPreset3Spot", 0);
                TexPreset3Radial = ini.GetIniInt(Section, "texPreset3Radial", 0);
                TexPreset3Cast = ini.GetIniInt(Section, "texPreset3Cast", 0);
                TexPreset4Exists = ini.GetIniInt(Section, "texPreset4Exists", 0) == 1;
                TexPreset4Brightness = ini.GetIniInt(Section, "texPreset4Brightness", 0);
                TexPreset4Blob = ini.GetIniInt(Section, "texPreset4Blob", 0);
                TexPreset4Gradient = ini.GetIniInt(Section, "texPreset4Gradient", 0);
                TexPreset4White = ini.GetIniInt(Section, "texPreset4White", 0);
                TexPreset4Spot = ini.GetIniInt(Section, "texPreset4Spot", 0);
                TexPreset4Radial = ini.GetIniInt(Section, "texPreset4Radial", 0);
                TexPreset4Cast = ini.GetIniInt(Section, "texPreset4Cast", 0);
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
                {
                    string selRaw = Content(ini, "lastSelectedStampNames", "");
                    LastSelectedStampNames = new List<string>();
                    if (!string.IsNullOrWhiteSpace(selRaw))
                    {
                        foreach (string n in selRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string nm = n.Trim();
                            if (nm.Length > 0) LastSelectedStampNames.Add(nm);
                        }
                    }
                }
                WindowWidth = ini.GetIniInt(Section, "windowWidth", WindowWidth);
                WindowHeight = ini.GetIniInt(Section, "windowHeight", WindowHeight);
                WindowLeft = ini.GetIniInt(Section, "windowLeft", WindowLeft);
                WindowTop = ini.GetIniInt(Section, "windowTop", WindowTop);
                LeftPanelWidth = ini.GetIniInt(Section, "leftPanelWidth", LeftPanelWidth);
                ContextKeywords = Content(ini, "contextKeywords", ContextKeywords);
                ContextRange = ini.GetIniInt(Section, "contextRange", ContextRange);
                ContextMatch = ini.GetIniInt(Section, "contextMatch", ContextMatch);
            ContextExcludeSpaces = ini.GetIniInt(Section, "contextExcludeSpaces", 0) == 1;
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
                if (LastSelectedStampNames != null && LastSelectedStampNames.Count > 0)
                    ini.WriteIniString(Section, "lastSelectedStampNames", string.Join(";", LastSelectedStampNames));
                else
                    ini.WriteIniString(Section, "lastSelectedStampNames", "");
                ini.WriteIniString(Section, "contextKeywords", ContextKeywords ?? "");
                ini.WriteIniInt(Section, "contextRange", ContextRange);
                ini.WriteIniInt(Section, "contextMatch", ContextMatch);
            ini.WriteIniInt(Section, "contextExcludeSpaces", ContextExcludeSpaces ? 1 : 0);
                ini.WriteIniInt(Section, "contextFilterEnabled", ContextFilterEnabled ? 1 : 0);
            }
            catch
            {
            }
        }

        /// <summary>盖章成功后保存一次签名配置（记住用户最近用的签名证书）。</summary>
        /// <summary>盖章渲染方案是否存在（idx 1~4）。</summary>
        public static bool TexPresetExists(int idx)
        {
            if (idx == 1) return TexPreset1Exists;
            if (idx == 2) return TexPreset2Exists;
            if (idx == 3) return TexPreset3Exists;
            if (idx == 4) return TexPreset4Exists;
            return false;
        }

        /// <summary>盖章渲染方案参数（idx 1~4，7 参数顺序：明暗/斑点大小/渐变/露白/内部斑点/径向/色调）；未保存返回 null。</summary>
        public static int[] GetTexPreset(int idx)
        {
            if (!TexPresetExists(idx)) return null;
            switch (idx)
            {
                case 1: return new[] { TexPreset1Brightness, TexPreset1Blob, TexPreset1Gradient, TexPreset1White, TexPreset1Spot, TexPreset1Radial, TexPreset1Cast };
                case 2: return new[] { TexPreset2Brightness, TexPreset2Blob, TexPreset2Gradient, TexPreset2White, TexPreset2Spot, TexPreset2Radial, TexPreset2Cast };
                case 3: return new[] { TexPreset3Brightness, TexPreset3Blob, TexPreset3Gradient, TexPreset3White, TexPreset3Spot, TexPreset3Radial, TexPreset3Cast };
                case 4: return new[] { TexPreset4Brightness, TexPreset4Blob, TexPreset4Gradient, TexPreset4White, TexPreset4Spot, TexPreset4Radial, TexPreset4Cast };
            }
            return null;
        }

        /// <summary>保存盖章渲染方案（idx 1~4），立即写入 ini。</summary>
        public static void SetTexPreset(int idx, int[] v)
        {
            if (idx < 1 || idx > 4 || v == null || v.Length != 7) return;
            switch (idx)
            {
                case 1:
                    TexPreset1Exists = true;
                    TexPreset1Brightness = v[0]; TexPreset1Blob = v[1]; TexPreset1Gradient = v[2];
                    TexPreset1White = v[3]; TexPreset1Spot = v[4]; TexPreset1Radial = v[5]; TexPreset1Cast = v[6];
                    break;
                case 2:
                    TexPreset2Exists = true;
                    TexPreset2Brightness = v[0]; TexPreset2Blob = v[1]; TexPreset2Gradient = v[2];
                    TexPreset2White = v[3]; TexPreset2Spot = v[4]; TexPreset2Radial = v[5]; TexPreset2Cast = v[6];
                    break;
                case 3:
                    TexPreset3Exists = true;
                    TexPreset3Brightness = v[0]; TexPreset3Blob = v[1]; TexPreset3Gradient = v[2];
                    TexPreset3White = v[3]; TexPreset3Spot = v[4]; TexPreset3Radial = v[5]; TexPreset3Cast = v[6];
                    break;
                case 4:
                    TexPreset4Exists = true;
                    TexPreset4Brightness = v[0]; TexPreset4Blob = v[1]; TexPreset4Gradient = v[2];
                    TexPreset4White = v[3]; TexPreset4Spot = v[4]; TexPreset4Radial = v[5]; TexPreset4Cast = v[6];
                    break;
            }
            SaveTexturePresets();
        }

        /// <summary>将 4 组渲染方案写入 ini。</summary>
        public static void SaveTexturePresets()
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string[] names = { "Brightness", "Blob", "Gradient", "White", "Spot", "Radial", "Cast" };
                for (int i = 1; i <= 4; i++)
                {
                    ini.WriteIniInt(Section, "texPreset" + i + "Exists", TexPresetExists(i) ? 1 : 0);
                    int[] v = GetTexPreset(i);
                    for (int k = 0; k < 7; k++)
                    {
                        ini.WriteIniInt(Section, "texPreset" + i + names[k], v != null ? v[k] : 0);
                    }
                }
            }
            catch
            {
            }
        }

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

        /// <summary>读取印章图片路径列表（兼容旧调用，内部基于条目列表）。</summary>
        public static List<string> LoadStampPaths()
        {
            List<string> result = new List<string>();
            foreach (StampEntry e in LoadStampEntries())
            {
                result.Add(e.Path);
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

        /// <summary>追加印章图片路径到 config.ini（显示名默认=文件名不含后缀；兼容旧调用）。</summary>
        public static void AppendStampPaths(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                string path = p.Trim();
                if (path.Length > 0)
                {
                    AppendStampEntry(Path.GetFileNameWithoutExtension(path), path);
                }
            }
        }

        /// <summary>从 config.ini 删除指定印章路径（大小写不敏感；同路径的多个条目一并删除）。</summary>
        public static void RemoveStampPath(string path)
        {
            try
            {
                List<StampEntry> entries = LoadStampEntries();
                entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
                SaveStampEntriesInternal(entries);
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>按显示名删除指定印章条目（大小写不敏感）。</summary>
        public static void RemoveStampEntry(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;
            try
            {
                List<StampEntry> entries = LoadStampEntries();
                entries.RemoveAll(e => string.Equals(e.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
                SaveStampEntriesInternal(entries);
                LastSelectedStampNames.RemoveAll(n => string.Equals(n, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
                RemoveStampParamsSection(displayName.Trim());
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>印章重命名：更新显示名（参数记忆迁移到新名，勾选集合同步）。</summary>
        public static void RenameStampEntry(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
            oldName = oldName.Trim();
            newName = newName.Trim();
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                List<StampEntry> entries = LoadStampEntries();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].DisplayName, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        entries[i] = new StampEntry(newName, entries[i].Path);
                    }
                }
                SaveStampEntriesInternal(entries);
                // 参数记忆迁移：旧节复制到新节后删除旧节
                StampParams legacy = LoadStampParamsCore(oldName);
                if (HasStampParams(oldName))
                {
                    SaveStampParams(newName, legacy);
                    RemoveStampParamsSection(oldName);
                }
                // 勾选集合同步
                for (int i = 0; i < LastSelectedStampNames.Count; i++)
                {
                    if (string.Equals(LastSelectedStampNames[i], oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        LastSelectedStampNames[i] = newName;
                    }
                }
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>印章条目（显示名 + 文件路径）。显示名默认=文件名不含后缀，可重命名，全列表唯一。</summary>
        public sealed class StampEntry
        {
            public StampEntry(string displayName, string path)
            {
                DisplayName = displayName ?? string.Empty;
                Path = path ?? string.Empty;
            }

            public string DisplayName { get; }
            public string Path { get; }
        }

        /// <summary>读取印章条目列表（含显示名）；旧格式（纯路径）自动识别，显示名取文件名不含后缀。</summary>
        public static List<StampEntry> LoadStampEntries()
        {
            List<StampEntry> result = new List<StampEntry>();
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string raw = Content(ini, StampPathsIniKey, "");

                // 旧用户迁移：config.ini 中没有 stampPaths，但 yz.log 存在 → 自动迁移
                if (string.IsNullOrWhiteSpace(raw) && File.Exists(StampsLogPath))
                {
                    List<StampEntry> oldEntries = new List<StampEntry>();
                    foreach (string line in File.ReadAllLines(StampsLogPath))
                    {
                        string p = line.Trim();
                        if (p.Length > 0)
                        {
                            oldEntries.Add(new StampEntry(Path.GetFileNameWithoutExtension(p), p));
                        }
                    }
                    if (oldEntries.Count > 0)
                    {
                        SaveStampEntriesInternal(oldEntries);
                        try { File.Delete(StampsLogPath); } catch { }
                        return oldEntries;
                    }
                }

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    foreach (string item in raw.Split(StampPathsSeparator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string seg = item.Trim();
                        if (seg.Length == 0) continue;
                        string displayName;
                        string path;
                        int bar = seg.IndexOf('|');
                        if (bar > 0 && bar < seg.Length - 1)
                        {
                            displayName = seg.Substring(0, bar).Trim();
                            path = seg.Substring(bar + 1).Trim();
                            if (displayName.Length == 0) displayName = Path.GetFileNameWithoutExtension(path);
                        }
                        else
                        {
                            path = seg;
                            displayName = Path.GetFileNameWithoutExtension(path);
                        }
                        if (path.Length > 0)
                        {
                            result.Add(new StampEntry(displayName, path));
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

        /// <summary>内部方法：将印章条目列表写入 config.ini（显示名|路径，分号分隔）。</summary>
        private static void SaveStampEntriesInternal(List<StampEntry> entries)
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                List<string> segs = new List<string>();
                foreach (StampEntry e in entries)
                {
                    if (string.IsNullOrWhiteSpace(e.Path)) continue;
                    string name = string.IsNullOrWhiteSpace(e.DisplayName)
                        ? Path.GetFileNameWithoutExtension(e.Path)
                        : e.DisplayName.Trim();
                    segs.Add(name + "|" + e.Path.Trim());
                }
                ini.WriteIniString(Section, StampPathsIniKey, string.Join(";", segs));
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>追加一个印章条目（显示名唯一由调用方保证；同路径允许多次导入、显示名不同即可）。</summary>
        public static void AppendStampEntry(string displayName, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string name = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(path)
                    : displayName.Trim();
                List<StampEntry> entries = LoadStampEntries();
                if (!entries.Exists(e => string.Equals(e.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(new StampEntry(name, path.Trim()));
                    SaveStampEntriesInternal(entries);
                }
            }
            catch
            {
                // 保存失败不影响主流程
            }
        }

        /// <summary>判断某显示名在印章列表中是否已存在（大小写不敏感）。</summary>
        public static bool StampDisplayNameExists(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return false;
            return LoadStampEntries().Exists(e =>
                string.Equals(e.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase));
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
                List<string> offsets = LoadAutoStampOffsets();
                int oldIdx = history.FindIndex(x => string.Equals(x, keyword, StringComparison.Ordinal));
                string keepOffset = "0|0|0";
                if (oldIdx >= 0 && oldIdx < offsets.Count)
                {
                    keepOffset = offsets[oldIdx];
                }
                if (oldIdx >= 0)
                {
                    history.RemoveAt(oldIdx);
                    if (oldIdx < offsets.Count)
                    {
                        offsets.RemoveAt(oldIdx);
                    }
                }
                history.Insert(0, keyword.Trim());
                offsets.Insert(0, keepOffset);
                if (history.Count > MaxAutoStampHistory)
                {
                    history.RemoveRange(MaxAutoStampHistory, history.Count - MaxAutoStampHistory);
                    offsets.RemoveRange(MaxAutoStampHistory, offsets.Count - MaxAutoStampHistory);
                }

                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, AutoStampHistoryIniKey, string.Join(AutoStampHistorySeparator, history));
                ini.WriteIniString(Section, AutoStampOffsetIniKey, string.Join(AutoStampHistorySeparator, offsets));
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
                List<string> offsets = LoadAutoStampOffsets();
                int idx = history.FindIndex(x => string.Equals(x, keyword, StringComparison.Ordinal));
                history.RemoveAll(x => string.Equals(x, keyword, StringComparison.Ordinal));
                if (idx >= 0 && idx < offsets.Count)
                {
                    offsets.RemoveAt(idx);
                }
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, AutoStampHistoryIniKey, string.Join(AutoStampHistorySeparator, history));
                ini.WriteIniString(Section, AutoStampOffsetIniKey, string.Join(AutoStampHistorySeparator, offsets));
            }
            catch
            {
            }
        }

        // ---------- 按文字盖章中心偏移记忆（随搜索文字保存，与历史列表同索引对应） ----------

        /// <summary>读取中心偏移记忆串（与历史同序，每项 "1|x|y"，分隔符同历史）。</summary>
        private static List<string> LoadAutoStampOffsets()
        {
            List<string> result = new List<string>();
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string raw = ini.ContentValue(Section, AutoStampOffsetIniKey);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return result;
                }

                foreach (string item in raw.Split(new[] { AutoStampHistorySeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = item.Trim();
                    if (t.Length > 0)
                    {
                        result.Add(t);
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        private static void SaveAutoStampOffsets(List<string> offsets)
        {
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(Section, AutoStampOffsetIniKey, string.Join(AutoStampHistorySeparator, offsets));
            }
            catch
            {
            }
        }

        /// <summary>取某盖章文字记忆的中心偏移（enabled=是否勾选；无记忆返回 false/0/0）。</summary>
        public static void GetCenterOffsetForKeyword(string keyword, out bool enabled, out float offsetXmm, out float offsetYmm)
        {
            enabled = false;
            offsetXmm = 0f;
            offsetYmm = 0f;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            List<string> history = LoadAutoStampHistory();
            List<string> offsets = LoadAutoStampOffsets();
            int idx = history.FindIndex(x => string.Equals(x, keyword.Trim(), StringComparison.Ordinal));
            if (idx < 0 || idx >= offsets.Count)
            {
                return;
            }

            string[] parts = offsets[idx].Split('|');
            if (parts.Length < 3)
            {
                return;
            }

            enabled = parts[0] == "1";
            float.TryParse(parts[1], out offsetXmm);
            float.TryParse(parts[2], out offsetYmm);
        }

        /// <summary>把某盖章文字的中心偏移写入记忆（该词必须已在历史中；越界值限制到 ±500mm）。</summary>
        public static void SetCenterOffsetForKeyword(string keyword, bool enabled, float offsetXmm, float offsetYmm)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            List<string> history = LoadAutoStampHistory();
            int idx = history.FindIndex(x => string.Equals(x, keyword.Trim(), StringComparison.Ordinal));
            if (idx < 0)
            {
                return;
            }

            float cx = Math.Max(-500f, Math.Min(500f, offsetXmm));
            float cy = Math.Max(-500f, Math.Min(500f, offsetYmm));
            List<string> offsets = LoadAutoStampOffsets();
            while (offsets.Count < history.Count)
            {
                offsets.Add("0|0|0");
            }
            offsets[idx] = (enabled ? "1" : "0") + "|" + cx.ToString("0.##") + "|" + cy.ToString("0.##");
            SaveAutoStampOffsets(offsets);
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
            public bool TextureQuality = false; // 盖章渲染（勾选后按四维上限随机生成印泥质感）
            public int TextureBrightness = 0;  // 明暗强度上限 0-100
            public int TextureBlob = 0;        // 斑块大小上限 0-100
            public int TextureGradient = 0;    // 渐变上限 0-100
            public int TextureWhite = 0;       // 局部露白上限 0-100
            public int TextureSpot = 0;        // 内部斑点上限 0-100
            public int TextureRadial = 0;       // 径向压印上限 0-100（中心深边缘浅）
            public int TextureCast = 0;         // 整体色偏上限 0-100（印泥批次色差）
        }

        private static string StampParamSection(string stampFileName)
        {
            return "stamp_" + (string.IsNullOrWhiteSpace(stampFileName) ? "default" : stampFileName.Trim());
        }

        /// <summary>某显示名（或文件名）是否已有保存的参数记录。</summary>
        public static bool HasStampParams(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string sec = StampParamSection(key);
                return !string.IsNullOrEmpty(ini.ContentValue(sec, "size"));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读取参数核心实现（按指定 key 读节）。</summary>
        private static StampParams LoadStampParamsCore(string key)
        {
            StampParams p = new StampParams();
            if (string.IsNullOrWhiteSpace(key))
            {
                return p;
            }
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                string sec = StampParamSection(key);
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
                p.TextureQuality = ini.GetIniInt(sec, "textureQuality", 0) == 1;
                p.TextureBrightness = ini.GetIniInt(sec, "textureBrightness", p.TextureBrightness);
                p.TextureBlob = ini.GetIniInt(sec, "textureBlob", p.TextureBlob);
                p.TextureGradient = ini.GetIniInt(sec, "textureGradient", p.TextureGradient);
                p.TextureWhite = ini.GetIniInt(sec, "textureWhite", p.TextureWhite);
                p.TextureSpot = ini.GetIniInt(sec, "textureSpot", p.TextureSpot);
                p.TextureRadial = ini.GetIniInt(sec, "textureRadial", p.TextureRadial);
                p.TextureCast = ini.GetIniInt(sec, "textureCast", p.TextureCast);
            }
            catch
            {
                // 读取失败保持默认
            }
            return p;
        }

        /// <summary>读取某印章参数（key=显示名）。旧配置按完整文件名保存的节自动迁移到显示名节，不丢记忆。</summary>
        public static StampParams LoadStampParams(string stampFileName)
        {
            if (string.IsNullOrWhiteSpace(stampFileName))
            {
                return new StampParams();
            }
            string key = stampFileName.Trim();
            if (HasStampParams(key))
            {
                return LoadStampParamsCore(key);
            }
            // 旧配置兼容：若同路径的完整文件名节存在则迁移（调用方提供 legacyFileName 时才会走到）
            return LoadStampParamsCore(key);
        }

        /// <summary>按显示名读取参数，旧版完整文件名节存在时自动迁移到显示名节（兼容 2.3 前配置）。</summary>
        public static StampParams LoadStampParamsMigrate(string displayName, string legacyFileName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return new StampParams();
            }
            string key = displayName.Trim();
            if (HasStampParams(key))
            {
                return LoadStampParamsCore(key);
            }
            if (!string.IsNullOrWhiteSpace(legacyFileName) && HasStampParams(legacyFileName.Trim()))
            {
                StampParams p = LoadStampParamsCore(legacyFileName.Trim());
                SaveStampParams(key, p);
                RemoveStampParamsSection(legacyFileName.Trim());
                return p;
            }
            return LoadStampParamsCore(key);
        }

        /// <summary>删除某 key 的参数节（WritePrivateProfileString 传 null 键删除整节）。</summary>
        public static void RemoveStampParamsSection(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            try
            {
                IniFileHelper ini = new IniFileHelper(IniPath);
                ini.WriteIniString(StampParamSection(key.Trim()), null, null);
            }
            catch
            {
                // 保存失败不影响主流程
            }
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
                ini.WriteIniInt(sec, "textureQuality", p.TextureQuality ? 1 : 0);
                ini.WriteIniInt(sec, "textureBrightness", p.TextureBrightness);
                ini.WriteIniInt(sec, "textureBlob", p.TextureBlob);
                ini.WriteIniInt(sec, "textureGradient", p.TextureGradient);
                ini.WriteIniInt(sec, "textureWhite", p.TextureWhite);
                ini.WriteIniInt(sec, "textureSpot", p.TextureSpot);
                ini.WriteIniInt(sec, "textureRadial", p.TextureRadial);
                ini.WriteIniInt(sec, "textureCast", p.TextureCast);
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
