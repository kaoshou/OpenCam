// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;

namespace ScreenRecorder.ProjectPreviewProbe;

/// <summary>Two-clip streaming/device-clock acceptance probe, never captures a screen or microphone.</summary>
internal static class StreamingPlaybackProbe
{
    internal static async Task<int> RunAsync(string[] args)
    {
        string Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault()
            ?? throw new ArgumentException(name + " required");
        var ffmpeg = Option("--ffmpeg");
        var helper = Option("--audio-helper");
        var root = Directory.CreateTempSubdirectory("opencam-streaming-av-");
        try
        {
            for (var clip = 0; clip < 2; clip++)
                await BoundedProcess.RunAsync(ffmpeg, ["-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
                    "testsrc2=size=64x36:rate=30:duration=0.8", "-f", "lavfi", "-i",
                    "aevalsrc=if(between(t\\,0.25\\,0.30)\\,0.01\\,0):s=48000:d=0.8",
                    "-filter_complex", FormattableString.Invariant($"[0:v]setpts=PTS+{2+clip}/TB[v];[1:a]asetpts=PTS+{2+clip+(clip==0?.2:0)}/TB[a]"),
                    "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-bf", "0", "-c:a", "pcm_f32le",
                    "-fps_mode", "passthrough", Path.Combine(root.FullName, $"clip-{clip}.mkv")], null, default);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("--stream-pcm");
            using var child = Process.Start(start) ?? throw new IOException("Audio helper did not start");
            long clockSamples = -1;
            var firstVideo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var published = 0;
            var maxLate = 0d;
            JsonElement? result = null;
            var clock = ReadClock();
            var errors = child.StandardError.ReadToEndAsync(stop.Token);
            var video = Video();
            var audio = Audio();
            try
            {
                await Task.WhenAll(video, audio, clock).WaitAsync(stop.Token);
                await child.WaitForExitAsync(stop.Token);
                if (child.ExitCode != 0 || result is null || result.Value.GetProperty("status").GetString() != "PASS"
                    || published != 48 || maxLate > .04)
                    throw new InvalidDataException($"Streaming acceptance failed: frames={published}, maxLateMs={maxLate*1000:F2}; {await errors}");
                Console.WriteLine(JsonSerializer.Serialize(new { status = "PARTIAL_PASS", publishedFrames = published,
                    maximumLatenessMs = maxLate * 1000, device = result,
                    remaining = "Seek/restart generations, early stop, visible GUI and full format matrix are not covered by this run." }));
                return 2;
            }
            finally
            {
                stop.Cancel();
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync();
                try { await Task.WhenAll(video, audio, clock, errors); } catch { }
            }

            async Task ReadClock()
            {
                while (await child.StandardOutput.ReadLineAsync(stop.Token) is {} line)
                {
                    if (line.Length > 4096) throw new InvalidDataException("Audio clock row quota exceeded");
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("sampleClock", out var sample))
                        Interlocked.Exchange(ref clockSamples, sample.GetInt64());
                    if (doc.RootElement.TryGetProperty("status", out _)) result = doc.RootElement.Clone();
                }
                if (result is null || result.Value.GetProperty("status").GetString() != "PASS")
                { stop.Cancel(); throw new InvalidDataException("Audio helper ended without successful streaming acknowledgement"); }
            }
            async Task Video()
            {
                for (var clip = 0; clip < 2; clip++)
                {
                    await using var source = File.OpenRead(Path.Combine(root.FullName, $"clip-{clip}.mkv"));
                    var offset = clip * .8 - (2 + clip);
                    await StreamingVideoDecoder.DecodeAsync(ffmpeg, source, 64, 36, async (frame, ct) =>
                    {
                        firstVideo.TrySetResult();
                        var seconds = frame.Pts * (double)frame.TimeNumerator / frame.TimeDenominator + offset;
                        while (Interlocked.Read(ref clockSamples) / 48000d < seconds) await Task.Delay(1, ct);
                        ct.ThrowIfCancellationRequested();
                        maxLate = Math.Max(maxLate, Interlocked.Read(ref clockSamples) / 48000d - seconds);
                        published++; // Actual hash-validated packet; no GUI claim.
                    }, stop.Token);
                }
            }
            async Task Audio()
            {
                await firstVideo.Task.WaitAsync(stop.Token);
                for (var clip = 0; clip < 2; clip++)
                {
                    await using var source = File.OpenRead(Path.Combine(root.FullName, $"clip-{clip}.mkv"));
                    await StreamingAudioDecoder.DecodeAsync(ffmpeg, source, 2 + clip, 38400,
                        async (pcm, ct) => await child.StandardInput.BaseStream.WriteAsync(pcm, ct), stop.Token);
                }
                child.StandardInput.Close();
            }
        }
        catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { status = "FAIL", reason = ex.Message })); return 1; }
        finally { Console.WriteLine("Evidence retained: " + root.FullName); }
    }
}
