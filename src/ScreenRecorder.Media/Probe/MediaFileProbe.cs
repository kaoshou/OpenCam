// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Probe;

public class MediaFileProbe : IMediaProbeService
{
    private readonly string? _ffprobePath;

    public MediaFileProbe(string? ffprobePath = null)
    {
        _ffprobePath = ffprobePath ?? FFmpegDiscovery.FindFFprobeExecutable();
    }

    public async Task<MediaProbeResult> ProbeAsync(string mediaFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_ffprobePath) || !File.Exists(_ffprobePath))
        {
            throw new FileNotFoundException($"找不到 ffprobe 執行檔: {_ffprobePath ?? "未設定"}");
        }

        if (!File.Exists(mediaFilePath))
        {
            return new MediaProbeResult(false, string.Empty, TimeSpan.Zero, 0, 0, 0, null, 0, 0, 0, null, 0, null);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "format=duration,size,format_name", "-show_entries", "stream=codec_type,codec_name,width,height,r_frame_rate,sample_rate", "-of", "json", Path.GetFullPath(mediaFilePath) })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var jsonOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errOutputTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var jsonOutput = await jsonOutputTask;
        await errOutputTask;

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(jsonOutput))
        {
            return new MediaProbeResult(false, string.Empty, TimeSpan.Zero, 0, 0, 0, null, 0, 0, 0, null, 0, jsonOutput);
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            string formatName = string.Empty;
            double durationSeconds = 0;
            long sizeBytes = 0;

            if (root.TryGetProperty("format", out var formatProp))
            {
                if (formatProp.TryGetProperty("format_name", out var fn)) formatName = fn.GetString() ?? string.Empty;
                if (formatProp.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), out var dVal)) durationSeconds = dVal;
                if (formatProp.TryGetProperty("size", out var sz) && long.TryParse(sz.GetString(), out var sVal)) sizeBytes = sVal;
            }

            int videoCount = 0;
            int audioCount = 0;
            string? videoCodec = null;
            int width = 0;
            int height = 0;
            double fps = 0;
            string? audioCodec = null;
            int sampleRate = 0;

            if (root.TryGetProperty("streams", out var streamsProp) && streamsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamsProp.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                    var codecName = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;

                    if (codecType == "video")
                    {
                        videoCount++;
                        videoCodec ??= codecName;
                        if (stream.TryGetProperty("width", out var w)) width = w.GetInt32();
                        if (stream.TryGetProperty("height", out var h)) height = h.GetInt32();
                        if (stream.TryGetProperty("r_frame_rate", out var rf))
                        {
                            var parts = rf.GetString()?.Split('/');
                            if (parts != null && parts.Length == 2 &&
                                double.TryParse(parts[0], out var num) &&
                                double.TryParse(parts[1], out var den) && den > 0)
                            {
                                fps = num / den;
                            }
                        }
                    }
                    else if (codecType == "audio")
                    {
                        audioCount++;
                        audioCodec ??= codecName;
                        if (stream.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out var srVal))
                        {
                            sampleRate = srVal;
                        }
                    }
                }
            }

            return new MediaProbeResult(
                IsValid: true,
                FormatName: formatName,
                Duration: TimeSpan.FromSeconds(durationSeconds),
                FileSizeBytes: sizeBytes,
                VideoStreamCount: videoCount,
                AudioStreamCount: audioCount,
                VideoCodec: videoCodec,
                Width: width,
                Height: height,
                Fps: fps,
                AudioCodec: audioCodec,
                SampleRate: sampleRate,
                RawJsonOutput: jsonOutput
            );
        }
        catch
        {
            return new MediaProbeResult(false, string.Empty, TimeSpan.Zero, 0, 0, 0, null, 0, 0, 0, null, 0, jsonOutput);
        }
    }
}
