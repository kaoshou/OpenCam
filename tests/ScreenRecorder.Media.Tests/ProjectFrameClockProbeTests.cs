// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectFrameClockProbeTests
{
    [Fact]
    public async Task DecodedPresentationOrderIncludesEveryFrameDespiteBFrames()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-clock-probe-");
        try
        {
            var path = Path.Combine(directory.FullName, "source.mkv");
            var start = new ProcessStartInfo(FFmpegDiscovery.FindFFmpegExecutable()!) {
                UseShellExecute = false, RedirectStandardError = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i",
                "testsrc=duration=1:size=64x64:rate=10", "-c:v", "libx264", "-bf", "3", "-pix_fmt", "yuv420p", path })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode); Assert.Empty(await errors);
            using var source = File.OpenRead(path);
            var pts = new List<long>();
            await foreach (var time in new ProjectFrameClockProbe().ReadAsync(source)) pts.Add(time);
            Assert.Equal(new long[] { 0,100000,200000,300000,400000,500000,600000,700000,800000,900000 }, pts);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task InvalidMediaCannotLookLikeACompleteEmptyClockSequence()
    {
        using var source = new MemoryStream([1,2,3]);
        await Assert.ThrowsAnyAsync<InvalidDataException>(async () => {
            await foreach (var unused in new ProjectFrameClockProbe().ReadAsync(source)) { }
        });
    }

    [Fact]
    public async Task PreCancelledProbeDoesNotReadOrCloseTheOwnedSource()
    {
        using var source = new MemoryStream([1,2,3]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var unused in new ProjectFrameClockProbe().ReadAsync(source, new(true))) { }
        });
        Assert.Equal(0, source.Position); Assert.True(source.CanRead);
    }
}
