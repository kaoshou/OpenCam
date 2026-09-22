// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace ScreenRecorder.UI.Services;

internal static class PlatformFolderOpener
{
    internal static ProcessStartInfo? BuildStartInfo(
        bool isWindows,
        bool isMacOS,
        string? lastOutputFilePath,
        string outputDirectory)
    {
        var hasFile =
            !string.IsNullOrWhiteSpace(lastOutputFilePath) &&
            File.Exists(lastOutputFilePath);
        var target = hasFile ? lastOutputFilePath! : outputDirectory;
        if (!hasFile && !Directory.Exists(target))
        {
            return null;
        }

        var info = new ProcessStartInfo { UseShellExecute = false };
        if (isMacOS)
        {
            info.FileName = "/usr/bin/open";
            if (hasFile)
            {
                info.ArgumentList.Add("-R");
            }

            info.ArgumentList.Add(target);
            return info;
        }

        if (isWindows)
        {
            info.FileName = "explorer.exe";
            info.ArgumentList.Add(hasFile ? "/select," + target : target);
            return info;
        }

        return null;
    }

    internal static (bool Success, string? Error) TryOpen(
        string? lastOutputFilePath,
        string outputDirectory)
    {
        try
        {
            var startInfo = BuildStartInfo(
                OperatingSystem.IsWindows(),
                OperatingSystem.IsMacOS(),
                lastOutputFilePath,
                outputDirectory);
            if (startInfo is null)
            {
                return (false, "找不到有效的輸出路徑或不支援目前平台");
            }

            using var process = Process.Start(startInfo);
            return process is null
                ? (false, "檔案管理程式無法啟動")
                : (true, null);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Unable to open output location: {0}", ex);
            return (false, ex.Message);
        }
    }
}
