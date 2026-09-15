using System.Diagnostics;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Remux;

public class StreamCopyRemuxer : IStreamCopyRemuxer
{
    private readonly string? _ffmpegPath;

    public StreamCopyRemuxer(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? FFmpegDiscovery.FindFFmpegExecutable();
    }

    public async Task<bool> RemuxToMp4Async(
        string mkvInputPath, 
        string mp4OutputPath, 
        IProgress<double>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException($"找不到 FFmpeg 執行檔: {_ffmpegPath ?? "未設定"}");
        }

        if (!File.Exists(mkvInputPath))
        {
            throw new FileNotFoundException($"找不到來源 MKV 工作檔: {mkvInputPath}");
        }

        var outputDirectory = Path.GetDirectoryName(mp4OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // 使用 stream copy，絕不重新編碼：-c copy -movflags +faststart
        var arguments = $"-y -i \"{mkvInputPath}\" -c copy -movflags +faststart \"{mp4OutputPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();

            var errorOutputTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            var stdOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(errorOutputTask, stdOutputTask);

            if (process.ExitCode != 0)
            {
                // Remux 失敗，若產生了不完整的 MP4 檔則刪除該殘留 MP4，但絕對保留原始 MKV
                if (File.Exists(mp4OutputPath))
                {
                    try { File.Delete(mp4OutputPath); } catch { }
                }
                return false;
            }

            // 驗證生成的 MP4 檔案是否存在且非 0 byte
            if (!File.Exists(mp4OutputPath)) return false;
            var fileInfo = new FileInfo(mp4OutputPath);
            return fileInfo.Length > 0;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }

            if (File.Exists(mp4OutputPath))
            {
                try { File.Delete(mp4OutputPath); } catch { }
            }
            throw;
        }
    }

    public async Task<bool> ConcatAndRemuxToMp4Async(
        IReadOnlyList<string> mkvInputPaths,
        string mp4OutputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (mkvInputPaths == null || mkvInputPaths.Count == 0)
        {
            throw new ArgumentException("來源 MKV 清單不得為空", nameof(mkvInputPaths));
        }

        var validPaths = mkvInputPaths.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) && new FileInfo(p).Length > 0).ToList();
        if (validPaths.Count == 0)
        {
            throw new FileNotFoundException("未在輸入清單中找到任何有效之 MKV 分段工作檔");
        }

        // 若僅有單一段落，直接執行常規 Remux
        if (validPaths.Count == 1)
        {
            return await RemuxToMp4Async(validPaths[0], mp4OutputPath, progress, cancellationToken);
        }

        if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException($"找不到 FFmpeg 執行檔: {_ffmpegPath ?? "未設定"}");
        }

        var outputDirectory = Path.GetDirectoryName(mp4OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // 建立 FFmpeg concat demuxer 清單檔案
        var listFilePath = Path.Combine(outputDirectory ?? Path.GetTempPath(), $"concat_list_{Guid.NewGuid():N}.txt");
        try
        {
            var lines = validPaths.Select(p => $"file '{p.Replace('\\', '/')}'");
            await File.WriteAllLinesAsync(listFilePath, lines, cancellationToken);

            // 使用 concat demuxer 執行無損流式拼接 (絕不二次重新編碼)
            var arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy -movflags +faststart \"{mp4OutputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var errorOutputTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            var stdOutputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            await Task.WhenAll(errorOutputTask, stdOutputTask);

            if (process.ExitCode != 0)
            {
                if (File.Exists(mp4OutputPath))
                {
                    try { File.Delete(mp4OutputPath); } catch { }
                }
                return false;
            }

            if (!File.Exists(mp4OutputPath)) return false;
            return new FileInfo(mp4OutputPath).Length > 0;
        }
        finally
        {
            if (File.Exists(listFilePath))
            {
                try { File.Delete(listFilePath); } catch { }
            }
        }
    }
}
