// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectSourceProbeTests
{
    [Fact]
    public async Task Probe_UsesRealMkvPacketTimestamps_AndNeverChangesOriginal()
    {
        var dir = Directory.CreateTempSubdirectory("OpenCam-project-probe-");
        try
        {
            var path = Path.Combine(dir.FullName, "segment.mkv");
            var start = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!) {
                UseShellExecute = false, RedirectStandardError = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=1:size=320x240:rate=30",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", path }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await errors);
            var original = SHA256.HashData(await File.ReadAllBytesAsync(path));
            await using var stream = File.OpenRead(path);
            var info = await new ProjectSourceProbe().ProbeAsync(stream);
            Assert.Equal(320, info.Width);
            Assert.Equal(240, info.Height);
            Assert.Equal("h264", info.VideoCodec);
            Assert.Equal(1, info.Timing.TimeBase.Numerator);
            Assert.Equal(1000, info.Timing.TimeBase.Denominator);
            Assert.Equal(0, info.Timing.StartPts);
            Assert.InRange(info.Timing.DurationTs, 999, 1001);
            Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Probe_InvalidBytes_AreNotPublishableVideo()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => new ProjectSourceProbe().ProbeAsync(stream));
    }

    [Fact]
    public async Task Probe_PreCancelled_DoesNotConsumeSource()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProjectSourceProbe().ProbeAsync(stream, new(true)));
        Assert.Equal(0, stream.Position);
    }
}
