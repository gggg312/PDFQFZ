using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDFQFZ.Library
{
    /// <summary>
    /// 输出文件名格式自定义选项（V132 起使用）。
    /// 文件名组成：原名 + "_" + 命名文字 + 时间戳 + 编号（或按 BeforeName 反置），编号自动递增防覆盖。
    /// </summary>
    public class OutputNamingOptions
    {
        /// <summary>命名文字（用户自定义，可包含"V"等前缀；空则只显示时间戳+编号）。</summary>
        public string Mark = "已盖章V";
        /// <summary>true=标记在文件名前，false=在文件名后。</summary>
        public bool BeforeName = false;
        /// <summary>递增类型：0=数字 1、2、3，1=大写字母 A、B、C，2=小写字母 a、b、c。</summary>
        public int SeqType = 0;
        /// <summary>编号位数（1/2/3，数字补零；字母类型忽略）。</summary>
        public int Pad = 1;
        /// <summary>是否插入时间戳（插在命名文字之后、编号之前）。</summary>
        public bool UseTimestamp = false;
        /// <summary>时间戳格式（仅允许数字与连字符，如 yyyyMMdd / yyyy-MM-dd / yyyyMMdd-HHmm）。</summary>
        public string TsFormat = "yyyyMMdd";
    }

    /// <summary>
    /// 输出文件命名策略：编号递增防覆盖、源文件旧标记剥离、时间戳/字母递进等。
    /// 旧版 GetNextOutputPath(..., string marker, bool markerBeforeSource) 保留兼容（测试与历史调用），
    /// 新代码一律使用 OutputNamingOptions 重载。
    /// </summary>
    public static class OutputFileNamingPolicy
    {
        private const string DefaultMarker = "已盖章";

        // ==================== 旧版（兼容保留） ====================

        public static string GetNextOutputPath(
            string destinationDirectory,
            string sourceFilePath,
            string marker,
            bool markerBeforeSource)
        {
            IEnumerable<string> existingFileNames = Directory.Exists(destinationDirectory)
                ? Directory.EnumerateFiles(destinationDirectory).Select(Path.GetFileName)
                : Enumerable.Empty<string>();

            string fileName = GetNextFileName(sourceFilePath, marker, markerBeforeSource, existingFileNames);
            string outputPath = Path.Combine(destinationDirectory, fileName);

            // 防覆盖：若计算出的输出路径与源文件相同（同名同目录），把源文件名视为已存在，版本+1 避开
            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourceFilePath), StringComparison.OrdinalIgnoreCase))
            {
                fileName = GetNextFileName(sourceFilePath, marker, markerBeforeSource,
                    existingFileNames.Concat(new[] { fileName }));
                outputPath = Path.Combine(destinationDirectory, fileName);
            }
            return outputPath;
        }

        public static string GetNextFileName(
            string sourceFilePath,
            string marker,
            bool markerBeforeSource,
            IEnumerable<string> existingFileNames)
        {
            marker = string.IsNullOrWhiteSpace(marker) ? DefaultMarker : marker.Trim();
            string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string extension = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".pdf";
            }

            // 源文件名本身已带标记（如 xxx_已盖章V1.pdf 再次作为源文件盖章）时，先剥离标记段，
            // 避免重复拼接出 xxx_已盖章V1_已盖章V1.pdf
            if (markerBeforeSource)
            {
                string strippedPrefix = Regex.Replace(sourceName,
                    "^" + Regex.Escape(marker) + "V[1-9][0-9]*_", "", RegexOptions.IgnoreCase);
                if (strippedPrefix != sourceName)
                {
                    sourceName = strippedPrefix;
                }
            }
            else
            {
                string strippedSuffix = Regex.Replace(sourceName,
                    "_" + Regex.Escape(marker) + "V[1-9][0-9]*$", "", RegexOptions.IgnoreCase);
                if (strippedSuffix != sourceName)
                {
                    sourceName = strippedSuffix;
                }
            }

            string pattern = markerBeforeSource
                ? "^" + Regex.Escape(marker) + "V(?<version>[1-9][0-9]*)_" + Regex.Escape(sourceName) + "(?:_.*)?" + Regex.Escape(extension) + "$"
                : "^" + Regex.Escape(sourceName + "_" + marker) + "V(?<version>[1-9][0-9]*)(?:_.*)?" + Regex.Escape(extension) + "$";

            int highestVersion = 0;
            foreach (string existingFileName in existingFileNames ?? Enumerable.Empty<string>())
            {
                Match match = Regex.Match(existingFileName ?? "", pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups["version"].Value, out int version))
                {
                    highestVersion = Math.Max(highestVersion, version);
                }
            }

            int nextVersion = highestVersion + 1;
            return markerBeforeSource
                ? marker + "V" + nextVersion + "_" + sourceName + extension
                : sourceName + "_" + marker + "V" + nextVersion + extension;
        }

        // ==================== 新版（V132 输出文件名格式自定义） ====================

        public static string GetNextOutputPath(string destinationDirectory, string sourceFilePath, OutputNamingOptions options)
        {
            IEnumerable<string> existingFileNames = Directory.Exists(destinationDirectory)
                ? Directory.EnumerateFiles(destinationDirectory).Select(Path.GetFileName)
                : Enumerable.Empty<string>();

            string fileName = GetNextFileName(sourceFilePath, options, existingFileNames);
            string outputPath = Path.Combine(destinationDirectory, fileName);

            if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourceFilePath), StringComparison.OrdinalIgnoreCase))
            {
                fileName = GetNextFileName(sourceFilePath, options, existingFileNames.Concat(new[] { fileName }));
                outputPath = Path.Combine(destinationDirectory, fileName);
            }
            return outputPath;
        }

        /// <summary>
        /// 计算下一个输出文件名（不含目录）。
        /// 组成（默认位置=后）：源文件名_命名文字[时间戳][_编号].pdf
        /// 时间戳存在时，时间戳与编号之间自动加 "_" 分隔，避免数字粘连（如 已盖章V20260911_1）。
        /// </summary>
        public static string GetNextFileName(string sourceFilePath, OutputNamingOptions options, IEnumerable<string> existingFileNames)
        {
            options = options ?? new OutputNamingOptions();
            string mark = (options.Mark ?? "").Trim();
            int seqType = NormalizeSeqType(options.SeqType);
            int pad = NormalizePad(options.Pad);

            string sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string extension = Path.GetExtension(sourceFilePath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".pdf";
            }

            // 1) 源文件名已带当前规则产出的旧标记段时先剥离，避免重复拼接
            sourceName = StripExistingMarker(sourceName, mark, options.BeforeName);

            // 2) 在已有文件名中查找最高编号（数字或字母），取下一个
            string tsPattern = @"(?:[0-9]+(?:-[0-9]+)*)?";
            string seqPattern = GetSeqPattern(seqType);
            string pattern;
            if (options.BeforeName)
            {
                pattern = "^" + Regex.Escape(mark) + tsPattern + "_?(?<seq>" + seqPattern + ")_"
                          + Regex.Escape(sourceName) + "(?:_.*)?" + Regex.Escape(extension) + "$";
            }
            else
            {
                pattern = "^" + Regex.Escape(sourceName + "_" + mark) + tsPattern + "_?(?<seq>" + seqPattern + ")"
                          + "(?:_.*)?" + Regex.Escape(extension) + "$";
            }

            int highest = 0;
            foreach (string existingFileName in existingFileNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(existingFileName))
                {
                    continue;
                }
                Match match = Regex.Match(existingFileName, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    continue;
                }
                string seqValue = match.Groups["seq"].Value;
                int value = seqType == 0 ? ParseNumber(seqValue) : AlphaToNumber(seqValue);
                if (value > highest)
                {
                    highest = value;
                }
            }
            int next = highest + 1;

            // 3) 拼接输出
            string ts = options.UseTimestamp ? FormatTimestamp(DateTime.Now, options.TsFormat) : "";
            string seqText = FormatSequence(next, seqType, pad);
            string body = mark + ts + (ts.Length > 0 ? "_" + seqText : seqText);

            return options.BeforeName
                ? body + "_" + sourceName + extension
                : sourceName + "_" + body + extension;
        }

        /// <summary>把源文件名中已带的本规则旧标记段剥离（如 xxx_已盖章V1 / 已盖章V1_xxx / xxx_已盖章20260911_1）。</summary>
        private static string StripExistingMarker(string sourceName, string mark, bool beforeName)
        {
            // 编号形态不区分类型（数字/字母均可），时间戳段兼容纯数字与连字符数字，避免切换类型后旧标记剥不掉
            string tsPattern = @"(?:[0-9]+(?:-[0-9]+)*)?";
            string anySeq = "[A-Za-z0-9]+";
            string stripped;
            if (beforeName)
            {
                stripped = Regex.Replace(sourceName,
                    "^" + Regex.Escape(mark) + tsPattern + "_?" + anySeq + "_",
                    "", RegexOptions.IgnoreCase);
            }
            else
            {
                stripped = Regex.Replace(sourceName,
                    "_" + Regex.Escape(mark) + tsPattern + "_?" + anySeq + "$",
                    "", RegexOptions.IgnoreCase);
            }
            return stripped != sourceName ? stripped : sourceName;
        }

        /// <summary>把编号格式化为显示文本：数字补零 / 大写字母 / 小写字母。</summary>
        public static string FormatSequence(int seq, int seqType, int pad)
        {
            if (seq <= 0)
            {
                seq = 1;
            }
            if (seqType == 0)
            {
                string s = seq.ToString();
                int p = NormalizePad(pad);
                while (s.Length < p)
                {
                    s = "0" + s;
                }
                return s;
            }
            string alpha = NumberToAlpha(seq);
            return seqType == 2 ? alpha.ToLowerInvariant() : alpha;
        }

        /// <summary>按格式生成时间戳文本（非法文件名字符自动剔除）。</summary>
        public static string FormatTimestamp(DateTime now, string format)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                format = "yyyyMMdd";
            }
            string s;
            try
            {
                s = now.ToString(format);
            }
            catch
            {
                s = now.ToString("yyyyMMdd");
            }
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                s = s.Replace(invalid[i].ToString(), "");
            }
            return s;
        }

        private static string GetSeqPattern(int seqType)
        {
            switch (seqType)
            {
                case 1: return "[A-Z]+";
                case 2: return "[a-z]+";
                default: return "[0-9]+";
            }
        }

        private static int NormalizeSeqType(int seqType)
        {
            if (seqType < 0) return 0;
            if (seqType > 2) return 2;
            return seqType;
        }

        private static int NormalizePad(int pad)
        {
            if (pad < 1) return 1;
            if (pad > 3) return 3;
            return pad;
        }

        private static int ParseNumber(string s)
        {
            return int.TryParse(s, out int v) ? v : 0;
        }

        /// <summary>字母编号转数字：A=1 … Z=26，AA=27，AB=28…（大小写统一按大写处理）。</summary>
        private static int AlphaToNumber(string s)
        {
            int n = 0;
            foreach (char c in s)
            {
                char upper = char.ToUpperInvariant(c);
                if (upper < 'A' || upper > 'Z')
                {
                    return 0;
                }
                n = n * 26 + (upper - 'A' + 1);
            }
            return n;
        }

        /// <summary>数字转字母编号：1=A … 26=Z，27=AA，28=AB…。</summary>
        private static string NumberToAlpha(int n)
        {
            const string alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            string s = "";
            int v = n;
            while (v > 0)
            {
                v--;
                s = alpha[v % 26] + s;
                v /= 26;
            }
            return s.Length > 0 ? s : "A";
        }

        public static string AddSuffix(string versionedOutputPath, string suffix)
        {
            return Path.Combine(
                Path.GetDirectoryName(versionedOutputPath) ?? "",
                Path.GetFileNameWithoutExtension(versionedOutputPath) + "_" + suffix + Path.GetExtension(versionedOutputPath));
        }

        public static string BuildSuccessMessage(string sourceFileName, string actualOutputPath)
        {
            return "成功！“" + sourceFileName + "”盖章完成！输出文件名“" + Path.GetFileName(actualOutputPath) + "”";
        }
    }
}
