// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.ProjectPreviewProbe;

namespace ScreenRecorder.Media.Tests;

public sealed class StreamingPreviewProbeTests
{
    [MacOsOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public async Task MalformedMetadataAbortsAndJoinsBothReadersWithoutWaitingForWatchdog()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-stream-malformed-");
        try
        {
            var helper = Path.Combine(root.FullName, "bad-helper.sh");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nprintf '" + new string('x', 5000) + "' >&2\nexec /bin/sleep 30\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var source = File.OpenRead(helper);
            await Assert.ThrowsAsync<InvalidDataException>(() => StreamingVideoDecoder.DecodeAsync(helper, source, 64, 36,
                (_, _) => throw new Xunit.Sdk.XunitException("Malformed data was published"), default).WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(source.CanRead);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task StreamingPcmPreservesLateAudioAndEndsAtExactRequestedSample()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-pcm-stream-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "audio.mkv");
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
                "aevalsrc=if(between(t\\,0.25\\,0.30)\\,0.01\\,0):s=48000:d=0.6",
                "-af", "asetpts=PTS+2.2/TB", "-c:a", "pcm_f32le", path }) start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, child.ExitCode);
            Assert.Empty(await error);
            await using var source = File.OpenRead(path);
            using var pcm = new MemoryStream();
            var blocks = 0;
            await StreamingAudioDecoder.DecodeAsync(ffmpeg, source, 2m, 38400, async (samples, ct) =>
            {
                Assert.InRange(samples.Length, 8, 1024 * 8);
                Assert.Equal(0, samples.Length % 8);
                blocks++;
                await pcm.WriteAsync(samples, ct);
            }, default);
            var bytes = pcm.ToArray();
            Assert.Equal(38400 * 8, bytes.Length);
            Assert.True(blocks > 1);
            var first = Enumerable.Range(0, 38400).First(i => Math.Abs(BitConverter.ToSingle(bytes, i * 8)) > .001);
            Assert.InRange(first, 21552, 21648); // Marker .25 + late audio .20 = .45s, within 1ms.
            Assert.All(Enumerable.Range(0, first), i => Assert.Equal(0f, BitConverter.ToSingle(bytes, i * 8)));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task StreamingDeliversBeforeCompletionAndRetainsVfrSourceTimestamps()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-stream-probe-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await Generate(ffmpeg, path);
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            await using var source = File.OpenRead(path);
            var frames = new List<StreamingVideoFrame>();
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var run = StreamingVideoDecoder.DecodeAsync(ffmpeg, source, 64, 36, async (frame, ct) =>
            {
                frames.Add(frame);
                if (frames.Count == 1) { first.SetResult(); await release.Task.WaitAsync(ct); }
            }, timeout.Token);
            await first.Task.WaitAsync(timeout.Token);
            Assert.False(run.IsCompleted);
            Assert.Single(frames);
            release.SetResult();
            await run.WaitAsync(timeout.Token);
            // Source keeps ordinal 0, 1, 3, 6 at a 10Hz source time base, starting at 2s.
            Assert.Equal(new decimal[] { 2m, 2.1m, 2.3m, 2.6m }, frames.Select(f => f.Pts * (decimal)f.TimeNumerator / f.TimeDenominator));
            Assert.All(frames, f => Assert.Equal(64 * 36 * 4, f.Rgba.Length));
            Assert.True(frames.Select(f => Convert.ToHexString(SHA256.HashData(f.Rgba))).Distinct().Count() > 1);
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task CancelWhileConsumerIsPausedJoinsDecoderAndSuppressesFurtherFrames()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-stream-cancel-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            using var cancel = new CancellationTokenSource();
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            var run = StreamingVideoDecoder.DecodeAsync(ffmpeg, source, 64, 36, async (_, ct) =>
            {
                Interlocked.Increment(ref count);
                first.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }, cancel.Token);
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
            await Task.Delay(250);
            Assert.Equal(1, count);
            Assert.True(source.CanRead);
            source.Position = 0;
            var restarted = 0;
            await StreamingVideoDecoder.DecodeAsync(ffmpeg, source, 64, 36,
                (_, _) => { restarted++; return Task.CompletedTask; }, default);
            Assert.Equal(4, restarted);
        }
        finally { root.Delete(true); }
    }

    private static async Task Generate(string ffmpeg, string path)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
            "testsrc2=size=64x36:rate=10:duration=0.8", "-vf",
            "select='eq(n,0)+eq(n,1)+eq(n,3)+eq(n,6)',setpts=PTS+2/TB",
            "-fps_mode", "passthrough", "-c:v", "libx264", "-bf", "0", "-an", path }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var stderr = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, child.ExitCode);
        Assert.Empty(await stderr);
    }
}
