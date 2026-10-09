// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMediaProcessTests
{
    [MacOsOnlyFact]
    public async Task RenamedSourceStillUsesOriginalHandle()
    {
        var directory = Directory.CreateTempSubdirectory("OpenCam-frame-process-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(directory.FullName, "source.mkv");
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(source);
            source.Position = 0;
            var process = new MacProjectMediaProcess(ffmpeg);
            var job = ProjectMediaJob.ExtractFrame(2200, new(1, 1000), 160, 90);
            var before = await process.RunAsync(job, [source], null, default);
            var reference = await ReferenceFrame(ffmpeg, path);
            Assert.Equal(reference, before.Output); // Independent ordinal 6, not a second seek with the same implementation.
            File.Move(path, path + ".original");
            await File.WriteAllTextAsync(path, "Not the original source");
            source.Position = 0;
            var after = await process.RunAsync(job, [source], null, default);
            Assert.Equal(before.Output, after.Output);
            Assert.Equal(160 * 90 * 4, after.Output.Length);
            source.Position = 0;
            Assert.Equal(hash, await SHA256.HashDataAsync(source));
        }
        finally { directory.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task BoundOutput_RemainsOriginalAfterParentRename()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-frame-output-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            var target = Directory.CreateDirectory(Path.Combine(root.FullName, "target"));
            var destination = Path.Combine(target.FullName, "frame.rgba");
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite);
            Directory.Move(target.FullName, target.FullName + ".original");
            Directory.CreateDirectory(target.FullName);
            await File.WriteAllTextAsync(destination, "Keep replacement");
            var result = await new MacProjectMediaProcess(ffmpeg).RunAsync(
                ProjectMediaJob.ExtractFrame(2200, new(1, 1000), 160, 90), [source], output, default);
            Assert.Empty(result.Output);
            Assert.Equal(160 * 90 * 4, output.Length);
            Assert.Equal("Keep replacement", await File.ReadAllTextAsync(destination));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task PreCancelled_DoesNotReadOrCloseCallerSource()
    {
        var path = Path.GetTempFileName();
        try
        {
            await using var source = File.OpenRead(path);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new MacProjectMediaProcess(FFmpegDiscovery.FindFFmpegExecutable()!).RunAsync(
                    ProjectMediaJob.ExtractFrame(0, new(1, 1000), 160, 90), [source], null, new(true)));
            Assert.True(source.CanRead);
            Assert.Equal(0, source.Position);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(160, 0)]
    [InlineData(16384, 16384)]
    public void FrameGeometryQuota_IsValidatedBeforeStartingChild(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectMediaJob.ExtractFrame(0, new(1, 1000), width, height));

    [MacOsOnlyFact]
    public async Task AspectRatio_IsPreservedInsideOddSizedCanvas()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-media-aspect-");
        try
        {
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            var path = Path.Combine(root.FullName, "source.mkv");
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            var result = await new MacProjectMediaProcess(ffmpeg).RunAsync(
                ProjectMediaJob.ExtractFrame(2200, new(1, 1000), 161, 161), [source], null, default);
            Assert.Equal(161 * 161 * 4, result.Output.Length);
            // 16:9 image fitted in a square; top/bottom are opaque black rather than stretched.
            for (var x = 0; x < 161; x++)
                Assert.Equal(new byte[] { 0, 0, 0, 255 }, result.Output[(x * 4)..(x * 4 + 4)]);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void TimestampOutsideExactFilterPrecision_IsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectMediaJob.ExtractFrame(9_007_199_254_740_993, new(1, 1_000_000), 160, 90));

    [MacOsOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public async Task RunningChildCancellation_ReapsOwnedProcess()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-media-cancel-");
        try
        {
            var helper = Path.Combine(root.FullName, "wait.sh");
            var pidFile = Path.Combine(root.FullName, "pid");
            await File.WriteAllTextAsync(helper, $"#!/bin/sh\necho $$ > '{pidFile}'\nexec /bin/sleep 60\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var source = File.OpenRead(helper);
            using var cancel = new CancellationTokenSource();
            var running = new MacProjectMediaProcess(helper).RunAsync(
                ProjectMediaJob.ExtractFrame(0, new(1, 1000), 160, 90), [source], null, cancel.Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(pidFile) || new FileInfo(pidFile).Length == 0)
                await Task.Delay(10, deadline.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
            var watch = Stopwatch.StartNew();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
            Assert.True(source.CanRead);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    public async Task MalformedDiagnostic_HitsQuotaAndReturnsPromptly()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-media-quota-");
        try
        {
            var helper = Path.Combine(root.FullName, "flood.sh");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nexec /usr/bin/yes 'diagnostic flood' >&2\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using var source = File.OpenRead(helper);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new MacProjectMediaProcess(helper).RunAsync(
                    ProjectMediaJob.ExtractFrame(0, new(1, 1000), 160, 90), [source], null, default)
                .WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("limit", error.Message);
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task MissingFrame_FailsRatherThanPublishingEmptyImage()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-media-missing-");
        try
        {
            var path = Path.Combine(root.FullName, "source.mkv");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new MacProjectMediaProcess(ffmpeg).RunAsync(
                    ProjectMediaJob.ExtractFrame(999000, new(1, 1000), 160, 90), [source], null, default));
        }
        finally { root.Delete(true); }
    }

    [MacOsOnlyFact]
    public async Task RetainedEndFence_DoesNotShowFirstDiscardedFrame()
    {
        var root = Directory.CreateTempSubdirectory("OpenCam-preview-fence-");
        try
        {
            var path = Path.Combine(root.FullName, "source.mkv");
            var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable()!;
            await Generate(ffmpeg, path);
            await using var source = File.OpenRead(path);
            var process = new MacProjectMediaProcess(ffmpeg);
            // Next available frame is PTS 2200. It must not appear when the retained interval ends there.
            await Assert.ThrowsAsync<InvalidDataException>(() => process.RunAsync(
                ProjectMediaJob.ExtractFrame(2199, new(1, 1000), 160, 90, 2200), [source], null, default));
            source.Position = 0;
            var retained = await process.RunAsync(
                ProjectMediaJob.ExtractFrame(2199, new(1, 1000), 160, 90, 2201), [source], null, default);
            Assert.Equal(await ReferenceFrame(ffmpeg, path), retained.Output);
        }
        finally { root.Delete(true); }
    }

    private static async Task<byte[]> ReferenceFrame(string ffmpeg, string source)
    {
        var start = new ProcessStartInfo(ffmpeg) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", source, "-vf",
            "select=eq(n\\,6),scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2:color=black",
            "-frames:v", "1", "-threads", "1", "-c:v", "rawvideo", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1" })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancel = timeout.Token.Register(() => { try { child.Kill(); } catch (InvalidOperationException) { } });
        var errors = child.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        await child.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        await child.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, child.ExitCode);
        Assert.Empty(await errors);
        return output.ToArray();
    }

    private static async Task Generate(string ffmpeg, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-n", "-f", "lavfi", "-i",
            "testsrc2=size=320x180:rate=30:duration=1", "-vf", "setpts=PTS+2/TB",
            "-c:v", "libx264", "-bf", "0", "-an", destination }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        using var cancel = timeout.Token.Register(() => { try { child.Kill(); } catch (InvalidOperationException) { } });
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, child.ExitCode);
        Assert.Empty(await errors);
    }
}
