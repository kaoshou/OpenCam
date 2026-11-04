// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using ScreenRecorder.Core;
using ScreenRecorder.Infrastructure.Session;
using ScreenRecorder.Infrastructure.Storage;

namespace ScreenRecorder.UI.Services;

internal static class SupportDiagnostics
{
    internal const int MaximumLogBytes = 2 * 1024 * 1024;
    internal const string ReleasesUrl = "https://github.com/kaoshou/OpenCam/releases/latest";
    internal static string LogDirectory => Path.Combine(UserDataPaths.LocalApplicationData, "ScreenRecorder", "Logs");

    internal static string EnvironmentSummary()
    {
        var session = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") : null;
        session = session is "x11" or "wayland" ? session : "not reported";
        return $"OpenCam {ProductInfo.PackageDisplayVersion}\nOS: {RuntimeInformation.OSDescription}\n" +
            $"Architecture: {RuntimeInformation.OSArchitecture} / Process: {RuntimeInformation.ProcessArchitecture}\n" +
            $".NET: {RuntimeInformation.FrameworkDescription}\nCapture core: OBS/libobs\nDesktop session: {session}\n" +
            ScreenRecorder.UI.Projects.EditorRequestMetrics.Shared.Describe();
    }

    internal static async Task ExportAsync(string logDirectory, string destination, string summary, DateTime today,
        CancellationToken ct = default)
    {
        var full = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a new .zip file.");
        using var parent = BoundDirectory.Open(Path.GetDirectoryName(full)!);
        var name = Path.GetFileName(full);
        var created = false;
        try
        {
            using (var output = parent.CreateNew(name))
            {
                created = true;
                using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                await AddTextAsync(zip, "environment.txt", summary, ct);
                var included = 0; var omitted = 0;
                if (Directory.Exists(logDirectory))
                {
                    using var logs = BoundDirectory.Open(logDirectory);
                    // Exactly four dates, direct children only. Never walk a project directory.
                    for (var day = 0; day < 4; day++)
                    {
                        var leaf = "app-" + today.Date.AddDays(-day).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";
                        FileStream source;
                        try { source = logs.Read(leaf, shareWrite: true); }
                        catch (InvalidDataException) { omitted++; continue; }
                        catch (IOException) { omitted++; continue; }
                        catch (UnauthorizedAccessException) { omitted++; continue; }
                        using (source)
                        {
                            var length = source.Length;
                            if (length > MaximumLogBytes) { omitted++; continue; }
                            using var target = zip.CreateEntry("logs/" + leaf, CompressionLevel.Fastest).Open();
                            // Copy only the length observed at open; a live log can keep growing.
                            var buffer = new byte[65536];
                            while (length > 0)
                            {
                                var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(length, buffer.Length)), ct);
                                if (count == 0) break;
                                await target.WriteAsync(buffer.AsMemory(0, count), ct); length -= count;
                            }
                            included++;
                        }
                    }
                }
                await AddTextAsync(zip, "README.txt",
                    "OpenCam diagnostics — review before sharing. No files are uploaded automatically.\n" +
                    "Contains an environment summary and up to four recent app logs (maximum 2 MiB each).\n" +
                    "Recordings, audio, subtitles, projects and settings are not collected.\n" +
                    "Logs may contain file paths, device names and other private details. This is NOT an anonymized archive.\n" +
                    $"Included logs: {included}; unavailable/oversized/unsafe logs omitted: {omitted}.\n", ct);
            }
        }
        catch
        {
            if (created)
                try { parent.DeleteOwnedFile(name); }
                catch (Exception cleanup) { Serilog.Log.Warning(cleanup, "Could not remove incomplete diagnostics archive"); }
            throw;
        }
    }

    private static async Task AddTextAsync(ZipArchive zip, string name, string text, CancellationToken ct)
    {
        using var output = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        await output.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
    }
}
