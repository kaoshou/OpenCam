using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Projects;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightTrialPhaseTests
{
    // A gate before actual engine startup makes false "recording" reports observable
    // without depending on how quickly the native runner happens to start FFmpeg.
    [Fact]
    public async Task IpcReportsStartingUntilEngineAcknowledgesAndFinalizingUntilStopCompletes()
    {
        using var diagnostics = new PreflightTrialTestDiagnostics();
        var clock = IndependentClockSnapshot.Capture();
        void Trace(string stage) => Serilog.Log.Debug("Preflight phase observation: stage={Stage}, clocks={Clocks}", stage, clock.ElapsedSummary());
        var root = Directory.CreateTempSubdirectory("OpenCam-trial-phases-");
        var displays = new Displays();
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        var engine = new GatedEngine(executable);
        var live = new PreflightLiveSession(() => new(new FFmpegScreenRecorderEngine(new WindowsFFmpegProvider(),
            ffmpegPath: executable, pinnedEncoder: HardwareEncoderType.SoftwareCpu, livePreviewOutput: true)
            { UseSyntheticCaptureSource = true }, []), displays, executable);
        var coordinator = new PreflightCoordinator(new(), () => new PreflightTrialSession(live, (_, _) => new(engine, []),
            () => new(root.FullName), new(() => new MacProjectMediaProcess(executable), () => throw new NotSupportedException())));
        var dispatcher = new PreflightIpcDispatcher(coordinator);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var observer = new DelayedHeartbeatStackObserver(() => !engine.StopEntered.Task.IsCompleted);
        var id = Guid.NewGuid();
        var request = new PreflightRequest(id, Configuration: new() { AudioSource = AudioSourceType.None, Fps = 10 });
        async Task<PreflightStatus> Send(string command)
        {
            var response = await dispatcher.DispatchAsync(new() { MessageType = command, PayloadJson = JsonSerializer.Serialize(request) });
            Assert.True(response.Success, response.ErrorMessage);
            var reply = JsonSerializer.Deserialize<PreflightReply>(response.DataJson!)!;
            request = request with { ConfigurationRevision = reply.Status.ConfigurationRevision, ServerInstanceId = reply.ServerInstanceId };
            return reply.Status;
        }
        async Task<PreflightStatus> Until(PreflightPhase phase)
        {
            Trace($"waiting-for-{phase}");
            while (true)
            {
                var status = await Send("GetPreflightStatus");
                if (status.Phase == phase) { Trace($"received-{phase}"); return status; }
                Assert.Null(status.ErrorCode);
                await Task.Delay(20, deadline.Token);
            }
        }
        try
        {
            Trace("begin-check");
            await Send("BeginPreflight");
            await Until(PreflightPhase.Checking);
            while (!(await Send("GetPreflightStatus")).HasFrame) await Task.Delay(20, deadline.Token);
            Trace("check-frame-ready");
            Assert.Equal(PreflightPhase.TrialStarting, (await Send("RecordPreflightTrial")).Phase);
            await engine.StartEntered.Task.WaitAsync(deadline.Token);
            Trace("trial-start-gate-entered");
            var starting = await Send("GetPreflightStatus");
            Assert.Equal(PreflightPhase.TrialStarting, starting.Phase);
            Assert.Equal(0, starting.TrialSeconds);
            Assert.False(starting.HasTrial);
            engine.AllowStart.TrySetResult();
            Trace("trial-start-gate-released");
            var recording = await Until(PreflightPhase.TrialRecording);
            Assert.True(engine.IsRunning);
            Assert.InRange(recording.TrialSeconds, 0, 5);
            await engine.StopEntered.Task.WaitAsync(deadline.Token);
            Trace("trial-stop-gate-entered");
            Assert.Equal(PreflightPhase.TrialFinalizing, (await Send("GetPreflightStatus")).Phase);
            engine.AllowStop.TrySetResult();
            Trace("trial-stop-gate-released");
            Assert.True((await Until(PreflightPhase.Reviewing)).HasTrial);
            Assert.False(engine.IsRunning);
        }
        catch (Exception error)
        {
            Trace("failed-before-cleanup");
            throw new Xunit.Sdk.XunitException($"{error}\nPhase diagnostics before cleanup:\n{diagnostics.Snapshot()}");
        }
        finally
        {
            engine.AllowStart.TrySetResult(); engine.AllowStop.TrySetResult();
            await dispatcher.DisposeAsync();
            await coordinator.DisposeAsync();
            Assert.Empty(root.EnumerateFileSystemInfos());
            root.Delete();
        }
    }

    private sealed class GatedEngine(string executable) : IScreenRecorderEngine, IBoundRecordingOutput
    {
        private readonly FFmpegScreenRecorderEngine _inner = new(new WindowsFFmpegProvider(), ffmpegPath: executable,
            pinnedEncoder: HardwareEncoderType.SoftwareCpu) { UseSyntheticCaptureSource = true };
        public readonly TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource AllowStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource AllowStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream? OutputStream { get => _inner.OutputStream; set => _inner.OutputStream = value; }
        public bool IsRunning => _inner.IsRunning;
        public bool UseSyntheticCaptureSource { get => true; set { } }
        public TimeSpan CurrentRecordedTime => _inner.CurrentRecordedTime;
        public long CurrentFramesRecorded => _inner.CurrentFramesRecorded;
        public HardwareEncoderType ActiveEncoder => _inner.ActiveEncoder;
        public CaptureSelection? ActiveCapture => _inner.ActiveCapture;
        public event EventHandler<string>? EngineErrorOccurred { add => _inner.EngineErrorOccurred += value; remove => _inner.EngineErrorOccurred -= value; }
        public event EventHandler<string>? EngineWarningOccurred { add => _inner.EngineWarningOccurred += value; remove => _inner.EngineWarningOccurred -= value; }
        public event EventHandler? AudioDeviceLost { add => _inner.AudioDeviceLost += value; remove => _inner.AudioDeviceLost -= value; }
        public async Task StartRecordingAsync(string path, RecordingConfiguration config, CaptureRegion bounds, CancellationToken ct = default)
        { StartEntered.TrySetResult(); await AllowStart.Task.WaitAsync(ct); await _inner.StartRecordingAsync(path, config, bounds, ct); }
        public async Task StopRecordingAsync(CancellationToken ct = default)
        { StopEntered.TrySetResult(); await AllowStop.Task.WaitAsync(ct); await _inner.StopRecordingAsync(ct); }
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
    private sealed class Displays : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "synthetic", new(0, 0, 128, 72), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => GetMonitors()[0].Bounds;
    }
}
