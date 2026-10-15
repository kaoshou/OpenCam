using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class PreflightLiveSessionTests
{
    [Fact]
    public async Task FailedStopRetainsOwnershipAndStalePollCannotReadAnotherCheck()
    {
        var capture = new RecordingCaptureLease();
        var session = new Live { FailStop = true };
        await using var coordinator = new PreflightCoordinator(capture, () => session);
        var id = Guid.NewGuid();
        var status = await coordinator.BeginAsync(id, new(), default);
        Assert.Equal(PreflightSourceState.Silent, coordinator.ReadStatus(id, status.ConfigurationRevision).Microphone.State);
        Assert.Equal(PreflightSourceState.Live, coordinator.ReadStatus(id, status.ConfigurationRevision).SystemAudio.State);
        Assert.Throws<InvalidOperationException>(() => coordinator.ReadStatus(Guid.NewGuid(), status.ConfigurationRevision));
        await Assert.ThrowsAsync<IOException>(() => coordinator.EndAsync(id, default));
        Assert.True(capture.IsOwned);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.BeginAsync(Guid.NewGuid(), new(), default));
        session.FailStop = false;
        await coordinator.EndAsync(id, default);
        Assert.False(capture.IsOwned);
        Assert.Throws<InvalidOperationException>(() => coordinator.ReadStatus(id, status.ConfigurationRevision));
    }

    [Fact]
    public async Task CallerCannotMutateFrozenSettingsWhileStartIsPending()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Live { Entered = entered, Finish = finish };
        await using var coordinator = new PreflightCoordinator(new(), () => session);
        var configuration = new RecordingConfiguration { Fps = 30 };
        var id = Guid.NewGuid();
        var start = coordinator.BeginAsync(id, configuration, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        configuration.Fps = 60;
        finish.SetResult();
        var status = await start;
        Assert.Equal(30, session.Configuration!.Fps);
        var frame = coordinator.ReadFrame(id, status.ConfigurationRevision)!;
        Assert.Equal(id, frame.ClipId);
        Assert.Equal(status.ConfigurationRevision, frame.Revision);
        await coordinator.EndAsync(id, default);
    }

    [Fact]
    public async Task RealStreamingPreviewProducesBoundedFramesWithoutSavingVideo()
    {
        var bytes = await RecordingContentExportIntegrationTests.Run(FFmpegDiscovery.FindFFmpegExecutable()!,
            ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=1", "-c:v", "libx264", "-f", "matroska", "pipe:1"]);
        await using var preview = new PreflightPreviewPipe(FFmpegDiscovery.FindFFmpegExecutable()!);
        await preview.Input.WriteAsync(bytes);
        await preview.CompleteAsync(default);
        Assert.InRange(preview.FrameCount, 1, 5);
        var frame = preview.LatestFrame!;
        Assert.Equal(960 * 540 * 4, frame.Length);
        Assert.True(frame[0] > 200 && frame[2] < 30); // real red video, RGBA (same as project/trial preview)
    }

    [Fact]
    public async Task ActualSyntheticEngineChecksInMemoryAndInvalidatesChangedDisplay()
    {
        var displays = new Displays();
        var provider = new MacOsFFmpegProvider(displays);
        var engine = new FFmpegScreenRecorderEngine(provider, pinnedEncoder: HardwareEncoderType.SoftwareCpu) { UseSyntheticCaptureSource = true };
        await using var session = new PreflightLiveSession(() => new(engine, []), displays, FFmpegDiscovery.FindFFmpegExecutable()!);
        var unused = Path.Combine(Path.GetTempPath(), "OpenCam-preflight-never-written-" + Guid.NewGuid().ToString("N"));
        await session.StartAsync(new() { AudioSource = AudioSourceType.None, OutputDirectory = unused }, default);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (session.ReadFrame() is null) await Task.Delay(50, deadline.Token);
        Assert.False(Directory.Exists(unused));
        Assert.True(session.ReadStatus(DateTimeOffset.UtcNow).HasFrame);
        displays.Name = "replacement";
        Assert.Equal("PreflightDeviceChanged", session.ReadStatus(DateTimeOffset.UtcNow).ErrorCode);
        Assert.Null(session.ReadFrame());
        displays.Name = "original";
        Assert.Equal("PreflightDeviceChanged", session.ReadStatus(DateTimeOffset.UtcNow).ErrorCode);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).HasFrame);
        await session.StopAsync(default);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task CancelDuringPreparationJoinsBeforeAnotherCaptureCanAcquire()
    {
        var capture = new RecordingCaptureLease();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new Live { Entered = entered, Finish = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var coordinator = new PreflightCoordinator(capture, () => session);
        var id = Guid.NewGuid();
        var starting = coordinator.BeginAsync(id, new(), default);
        await entered.Task;
        await coordinator.EndAsync(id, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        await using var next = await capture.AcquireAsync(Guid.NewGuid(), default);
    }

    private sealed class Displays : IDisplayService
    {
        public string Name = "original";
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, Name, new(0, 0, 64, 36), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 64, 36);
    }

    private sealed class Live : IPreflightLiveSession
    {
        public bool FailStop;
        public RecordingConfiguration? Configuration;
        public TaskCompletionSource? Entered, Finish;
        public async Task StartAsync(RecordingConfiguration snapshot, CancellationToken ct)
        { Configuration = snapshot; Entered?.SetResult(); if (Finish is not null) await Finish.Task.WaitAsync(ct); }
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty with
        { Phase = PreflightPhase.Checking, Microphone = new(PreflightSourceState.Silent), SystemAudio = new(PreflightSourceState.Live, .5, .8), HasFrame = true };
        public ProjectFrameReply? ReadFrame() => new(0, 0, Guid.Empty, [0, 0, 255, 255]) { PixelWidth = 1, PixelHeight = 1 };
        public Task StopAsync(CancellationToken ct) => FailStop ? Task.FromException(new IOException("stop not confirmed")) : Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
