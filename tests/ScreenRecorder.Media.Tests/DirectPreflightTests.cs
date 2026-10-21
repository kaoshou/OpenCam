using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Media.Preflight;

namespace ScreenRecorder.Media.Tests;

public class DirectPreflightTests
{
    [Fact]
    public void DirectPreviewPreservesNativeCropAndPinnedWindowsInput()
    {
        var config = new RecordingConfiguration { CaptureSource = CaptureSourceType.CustomRegion,
            AudioSource = AudioSourceType.SystemAndMicrophone, MonitorIndex = 1 };
        var mac = new MacOsFFmpegProvider(new OffsetDisplay());
        var original = mac.BuildInputArguments(config, -2460, 200, 1280, 720, false, false, "-f s16le -i pipe:8", "-f s16le -i pipe:9");
        var preview = mac.BuildDirectPreviewArguments(config, -2460, 200, 1280, 720, false, false, "-f s16le -i pipe:8", "-f s16le -i pipe:9");
        Assert.Contains("-filter:v \"crop=1280:720:100:200\"", original);
        Assert.Contains("-vf \"crop=1280:720:100:200,fps=10,scale=960:540", preview);
        Assert.DoesNotContain("-filter:v", preview); // do not let a second video filter override the crop
        Assert.Contains("-map \"[aout]\"", preview);
        Assert.True(preview.IndexOf("-f null -", StringComparison.Ordinal) < preview.IndexOf("-vf", StringComparison.Ordinal));
        const string pinnedVideo = "-f lavfi -i \"ddagrab=output_idx=2:offset_x=100:offset_y=200:video_size=1280x720,hwdownload,format=bgra\" ";
        var windows = new WindowsFFmpegProvider();
        var pinned = windows.BuildDirectPreviewArguments(config, -2460, 200, 1280, 720, false, true, "-f s16le -i pipe:8", null, pinnedVideo);
        Assert.StartsWith(pinnedVideo, pinned);
        Assert.Contains("-c:v rawvideo", pinned);
        Assert.DoesNotContain("matroska", pinned);
        Assert.Contains("-c:v libx264", windows.BuildOutputArguments(config, HardwareEncoderType.SoftwareCpu, "trial.mkv"));
    }

    private sealed class OffsetDisplay : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(1, "left", new(-2560, 0, 2560, 1600), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => GetMonitors()[0].Bounds;
    }

    [Fact]
    public async Task FragmentedWritesPublishOnlyNewestCompleteImmutablePicture()
    {
        using var sink = new LatestPreviewFrameStream();
        var first = Enumerable.Repeat((byte)17, LivePreviewFormat.FrameBytes).ToArray();
        sink.Write(first, 0, first.Length - 1);
        Assert.Null(sink.LatestFrame);
        await sink.WriteAsync(first.AsMemory(first.Length - 1));
        var held = sink.LatestFrame!;
        Assert.Equal(first, held);
        var second = Enumerable.Repeat((byte)91, first.Length).ToArray();
        sink.Write(second);
        sink.Write(first, 0, 31); // incomplete third frame must not replace second
        Assert.Equal(second, sink.LatestFrame);
        Assert.All(held, value => Assert.Equal(17, value));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sink.WriteAsync(first.AsMemory(), cancelled.Token));
        Assert.Equal(second, sink.LatestFrame);
        sink.Dispose();
        Assert.Null(sink.LatestFrame);
        Assert.Throws<ObjectDisposedException>(() => sink.Write(first));
    }

    // A container/encoded stream cannot be consumed directly as complete RGBA pictures.
    [Theory]
    [InlineData(false, AudioSourceType.None)]
    [InlineData(false, AudioSourceType.SystemOnly)]
    [InlineData(false, AudioSourceType.MicrophoneOnly)]
    [InlineData(false, AudioSourceType.SystemAndMicrophone)]
    [InlineData(true, AudioSourceType.None)]
    [InlineData(true, AudioSourceType.SystemOnly)]
    [InlineData(true, AudioSourceType.MicrophoneOnly)]
    [InlineData(true, AudioSourceType.SystemAndMicrophone)]
    public async Task LiveOutputCanBeDisplayedWithoutAContainerOrDecoder(bool mac, AudioSourceType audio)
    {
        using var diagnostics = new PreflightTrialTestDiagnostics();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        double? startupMs = null, stoppedMs = null;
        long? finalBytes = null;
        IFFmpegPlatformProvider provider = mac ? new MacOsFFmpegProvider() : new WindowsFFmpegProvider();
        using var output = new MemoryStream();
        await using var engine = new FFmpegScreenRecorderEngine(provider,
            ffmpegPath: FFmpegDiscovery.FindFFmpegExecutable()!,
            pinnedEncoder: HardwareEncoderType.SoftwareCpu, livePreviewOutput: true)
            { UseSyntheticCaptureSource = true, OutputStream = output };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task? startupTask = null;
        var startupAcknowledged = 0;
        using var startupObserver = new DelayedHeartbeatStackObserver(() =>
        {
            if (Volatile.Read(ref startupAcknowledged) != 0) return false;
            Serilog.Log.Debug("Direct preview startup observation: elapsedMs={ElapsedMs}, task={Task}, deadlineCanceled={Canceled}, frames={Frames}, seconds={Seconds}, running={Running}",
                elapsed.Elapsed.TotalMilliseconds, startupTask?.Status, deadline.IsCancellationRequested,
                engine.CurrentFramesRecorded, engine.CurrentRecordedTime.TotalSeconds, engine.IsRunning);
            return true;
        });
        try
        {
            startupTask = engine.StartRecordingAsync("unused.mkv", new() { AudioSource = audio, Fps = 10 },
                new(0, 0, 128, 72), deadline.Token);
            await startupTask;
            Volatile.Write(ref startupAcknowledged, 1);
            startupMs = elapsed.Elapsed.TotalMilliseconds;
            await engine.StopRecordingAsync(deadline.Token);
            stoppedMs = elapsed.Elapsed.TotalMilliseconds;
            var bytes = output.ToArray();
            finalBytes = bytes.LongLength;
            Assert.True(bytes.Length >= 960 * 540 * 4);
            Assert.Equal(0, bytes.Length % (960 * 540 * 4));
            Assert.All(Enumerable.Range(0, 960 * 540).Select(i => bytes[i * 4 + 3]), alpha => Assert.Equal(255, alpha));
            Assert.Contains(Enumerable.Range(0, 960 * 540), i => bytes[i * 4] > 32 || bytes[i * 4 + 1] > 32 || bytes[i * 4 + 2] > 32);
            Assert.False(engine.IsRunning);
        }
        catch (Exception error)
        {
            // Preserve the failure before Dispose performs another stop. Do not
            // inspect the MemoryStream while its producer may still be writing.
            ThreadPool.GetAvailableThreads(out var available, out _);
            throw new Xunit.Sdk.XunitException($"{error}\nDirect preview diagnostics before cleanup: " +
                $"macProvider={mac}, audio={audio}, elapsedMs={elapsed.Elapsed.TotalMilliseconds:R}, " +
                $"startupMs={startupMs:R}, stoppedMs={stoppedMs:R}, stopMs={stoppedMs - startupMs:R}, " +
                $"deadlineCanceled={deadline.IsCancellationRequested}, bytes={finalBytes}, " +
                $"frames={engine.CurrentFramesRecorded}, seconds={engine.CurrentRecordedTime.TotalSeconds:R}, " +
                $"running={engine.IsRunning}, workers={ThreadPool.ThreadCount}, available={available}, " +
                $"pending={ThreadPool.PendingWorkItemCount}\n{diagnostics.Snapshot()}");
        }
    }
}
