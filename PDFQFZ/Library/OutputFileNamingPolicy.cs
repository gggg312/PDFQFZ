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
            return Path.Combine(destinationDirectory, fileName);
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
