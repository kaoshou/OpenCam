// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Probe;

/// <summary>Streams every decoded presentation timestamp from an already bound MKV.
/// Packet/decode order and progress delivery are deliberately not used as video clocks.</summary>
public sealed class ProjectFrameClockProbe(string? executable = null)
{
    public async IAsyncEnumerable<long> ReadAsync(Stream source, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek) throw new InvalidDataException("A seekable source handle is required.");
        source.Position = 0;
        var path = executable ?? FFmpegDiscovery.FindFFprobeExecutable() ?? throw new FileNotFoundException("ffprobe is unavailable.");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-protocol_whitelist", "pipe", "-f", "matroska", "-i", "pipe:0",
            "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "compact=p=1:nk=0" })
            start.ArgumentList.Add(arg);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        var token = lifetime.Token;
        using var process = Process.Start(start) ?? throw new IOException("Cannot start frame clock probe.");
        var work = Task.WhenAll(Feed(), DrainErrors(), process.WaitForExitAsync(token));
        long count = 0, previous = long.MinValue;
        try
        {
            await foreach (var line in Lines(process.StandardOutput, token))
            {
                if (!line.StartsWith("frame|", StringComparison.Ordinal)) continue;
                var value = line.Split('|').SingleOrDefault(x => x.StartsWith("best_effort_timestamp_time=", StringComparison.Ordinal));
                if (value == null || !decimal.TryParse(value.AsSpan("best_effort_timestamp_time=".Length),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
                    seconds is < -60 or > 315360000)
                    throw new InvalidDataException("Video frame has no valid presentation timestamp.");
                var time = checked((long)decimal.Round(seconds * 1_000_000, 0, MidpointRounding.AwayFromZero));
                if (time <= previous) throw new InvalidDataException("Video presentation timestamps are not strictly increasing.");
                previous = time; count++;
                yield return time;
            }
            await work.WaitAsync(token);
            if (process.ExitCode != 0 || count == 0) throw new InvalidDataException("Video clock sequence could not be decoded completely.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            lifetime.Cancel();
            // Do not return an owned source while its feeder is still reading it.
            try { await work.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) when (work.IsCompleted) { _ = work.Exception; }
        }

        async Task Feed()
        {
            try { await source.CopyToAsync(process.StandardInput.BaseStream, token); }
            finally { process.StandardInput.Close(); }
        }
        async Task DrainErrors()
        {
            var buffer = new char[1024];
            while (await process.StandardError.ReadAsync(buffer.AsMemory(), token) > 0) { }
        }
    }

    private static async IAsyncEnumerable<string> Lines(StreamReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new char[2048];
        var line = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') { yield return line.ToString(); line.Clear(); }
                else if (buffer[i] != '\r')
                {
                    if (line.Length >= 4096) throw new InvalidDataException("Unexpected frame clock output.");
                    line.Append(buffer[i]);
                }
            }
        if (line.Length > 0) yield return line.ToString();
    }
}
