using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PDFQFZ.Library
{
    public static class OutputFileNamingPolicy
    {
        private const string DefaultMarker = "已盖章";

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
