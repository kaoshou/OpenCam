// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.FFmpeg;

namespace ScreenRecorder.Media.Probe;

/// <summary>Feeds an already bound MKV handle to ffprobe. No manifest path reaches the child process.</summary>
public sealed class ProjectSourceProbe(string? executable = null) : IProjectSourceProbe
{
    public async Task<ProjectMediaInfo> ProbeAsync(Stream source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!source.CanRead || !source.CanSeek) throw new InvalidDataException("A seekable source handle is required.");
        source.Position = 0;
        var path = executable ?? FFmpegDiscovery.FindFFprobeExecutable()
            ?? throw new FileNotFoundException("ffprobe is unavailable.");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-protocol_whitelist", "pipe", "-f", "matroska", "-i", "pipe:0",
            "-show_packets", "-show_streams", "-show_entries",
            "packet=stream_index,pts,duration:stream=index,codec_type,codec_name,width,height,time_base,start_pts,duration_ts",
            "-of", "compact=p=1:nk=0" }) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        using var process = new Process { StartInfo = start };
        process.Start();
        Task work = Task.CompletedTask;
        ProjectMediaInfo? result = null;
        try
        {
            work = Task.WhenAll(FeedAsync(), ReadAsync(), DrainErrorsAsync(), process.WaitForExitAsync(token));
            await work.WaitAsync(token);
            if (process.ExitCode != 0 || result is null) throw new InvalidDataException("MKV has no valid timed video stream.");
            return result;
        }
        catch (IOException ex) { throw new InvalidDataException("Cannot probe project source.", ex); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            timeout.Cancel();
            try { await work.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }

        async Task FeedAsync()
        {
            try { await source.CopyToAsync(process.StandardInput.BaseStream, token); }
            finally { process.StandardInput.Close(); }
        }
        async Task DrainErrorsAsync()
        {
            var buffer = new char[1024];
            while (await process.StandardError.ReadAsync(buffer.AsMemory(), token) > 0) { }
        }
        async Task ReadAsync()
        {
            // Constant memory even for long recordings; no packet/frame arrays.
            var packets = new Dictionary<int, (long First, long End)>();
            string? audio = null;
            ProjectMediaInfo? video = null;
            var line = new StringBuilder();
            var buffer = new char[2048];
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer.AsMemory(), token)) > 0)
            {
                for (var n = 0; n < count; n++)
                {
                    if (buffer[n] == '\n') { ParseLine(line.ToString()); line.Clear(); }
                    else if (buffer[n] != '\r')
                    {
                        if (line.Length >= 4096) throw new InvalidDataException("Unexpected ffprobe output.");
                        line.Append(buffer[n]);
                    }
                }
            }
            if (line.Length > 0) ParseLine(line.ToString());
            if (video is not null) result = video with { AudioCodec = audio };

            void ParseLine(string text)
            {
                if (text.Length == 0) return;
                var parts = text.Split('|');
                var values = new Dictionary<string, string>();
                foreach (var part in parts.Skip(1))
                {
                    var pair = part.Split('=', 2);
                    if (pair.Length == 2) values[pair[0]] = pair[1];
                }
                if (parts[0] == "packet")
                {
                    if (!Number(values, "stream_index", out var index) || index is < 0 or > 255 ||
                        !Number(values, "pts", out var pts) || !Number(values, "duration", out var duration) || duration <= 0)
                        return;
                    var end = checked(pts + duration);
                    if (packets.TryGetValue((int)index, out var previous))
                        packets[(int)index] = (Math.Min(previous.First, pts), Math.Max(previous.End, end));
                    else packets.Add((int)index, (pts, end));
                }
                else if (parts[0] == "stream")
                {
                    values.TryGetValue("codec_name", out var codec);
                    if (values.GetValueOrDefault("codec_type") == "audio") audio ??= codec;
                    if (values.GetValueOrDefault("codec_type") != "video" || video is not null) return;
                    if (!Number(values, "index", out var index) || !packets.TryGetValue((int)index, out var range) ||
                        !Number(values, "width", out var width) || !Number(values, "height", out var height) ||
                        width is < 1 or > 16384 || height is < 1 or > 16384 || string.IsNullOrEmpty(codec))
                        throw new InvalidDataException("Missing video timing or geometry.");
                    var rational = values.GetValueOrDefault("time_base", "").Split('/');
                    if (rational.Length != 2 || !long.TryParse(rational[0], out var num) ||
                        !long.TryParse(rational[1], out var den) || num <= 0 || den <= 0 || num > int.MaxValue || den > int.MaxValue)
                        throw new InvalidDataException("Invalid source time base.");
                    video = new(new(new(num, den), range.First, checked(range.End - range.First)),
                        (int)width, (int)height, codec, null);
                }
            }
        }
    }

    private static bool Number(Dictionary<string, string> values, string name, out long value) =>
        long.TryParse(values.GetValueOrDefault(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
