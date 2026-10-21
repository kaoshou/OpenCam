using System.Collections.Concurrent;
using System.Text.Json;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Media.FFmpeg;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.UI.Preflight;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScreenRecorder.Media.Tests;

public class PreflightDiagnosticsTests
{
    [Fact]
    public async Task PollingDuringInitializationDoesNotPoisonTheLaterRealPreview()
    {
        var executable = FFmpegDiscovery.FindFFmpegExecutable()!;
        var bytes = await RecordingContentExportIntegrationTests.Run(executable,
            ["-f", "lavfi", "-i", "color=red:s=64x36:r=30:d=6", "-c:v", "libx264", "-f", "matroska", "pipe:1"]);
        var engine = new QuietEngine { StartGate = new(TaskCreationOptions.RunContinuationsAsynchronously), Payload = bytes };
        await using var live = new PreflightLiveSession(() => new(engine, []), new Displays(), executable);
        var operation = Guid.NewGuid(); var server = Guid.NewGuid();
        var model = new RecordingPreflightViewModel(operation);
        var starting = live.StartAsync(new() { AudioSource = AudioSourceType.None }, default);
        await engine.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var pending = live.ReadStatus(DateTimeOffset.UtcNow);
                Assert.Null(pending.ErrorCode);
                Assert.Equal(PreflightPhase.Preparing, pending.Phase);
                Assert.False(pending.HasFrame);
                Assert.Null(live.ReadFrame());
                model.Apply(new(server, pending with { OperationId = operation }, false, i + 1));
                Assert.False(model.CanStart);
            }
        }
        finally { engine.StartGate.TrySetResult(); await starting; }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        ProjectFrameReply? frame;
        while ((frame = live.ReadFrame()) is null) await Task.Delay(10, deadline.Token);
        Assert.True(frame.Rgba![0] > 200 && frame.Rgba[2] < 30);
        var ready = live.ReadStatus(DateTimeOffset.UtcNow);
        Assert.Null(ready.ErrorCode);
        Assert.True(ready.HasFrame);
        Assert.False(ready.AudioReady);
        model.Apply(new(server, ready with { OperationId = operation }, false, 4));
        Assert.True(model.CanStart); // Explicit recording does not require a successful audio check.
        Assert.False(model.Status.AudioReady); // A video frame still does not prove audio readiness.
        Assert.False(model.CanTrial);
    }

    [Fact]
    public async Task PollingDuringConfirmedStopDoesNotBecomeACaptureFailure()
    {
        var engine = new QuietEngine { StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var live = new PreflightLiveSession(() => new(engine, []), new Displays(), FFmpegDiscovery.FindFFmpegExecutable()!);
        await live.StartAsync(new() { AudioSource = AudioSourceType.None }, default);
        var stopping = live.StopAsync(default);
        await engine.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var status = live.ReadStatus(DateTimeOffset.UtcNow);
            Assert.Null(status.ErrorCode);
            Assert.False(status.HasFrame);
        }
        finally { engine.StopGate.TrySetResult(); await stopping; }
        Assert.Null(live.ReadStatus(DateTimeOffset.UtcNow).ErrorCode);
    }

    [Fact]
    public async Task UnexpectedCaptureExitAfterInitializationStillInvalidatesTheCheck()
    {
        var engine = new QuietEngine();
        await using var live = new PreflightLiveSession(() => new(engine, []), new Displays(), FFmpegDiscovery.FindFFmpegExecutable()!);
        await live.StartAsync(new() { AudioSource = AudioSourceType.None }, default);
        engine.IsRunning = false;
        Assert.Equal("PreflightCaptureFailed", live.ReadStatus(DateTimeOffset.UtcNow).ErrorCode);
        engine.IsRunning = true;
        Assert.Equal("PreflightCaptureFailed", live.ReadStatus(DateTimeOffset.UtcNow).ErrorCode);
        Assert.Null(live.ReadFrame());
    }

    [Fact]
    public async Task MissingPreviewReportsProducerAndDecoderStateOnlyOnceWithoutAllowingStart()
    {
        using var capture = new DiagnosticCapture();
        var engine = new QuietEngine();
        await using var live = new PreflightLiveSession(() => new(engine, []), new Displays(), FFmpegDiscovery.FindFFmpegExecutable()!);
        await live.StartAsync(new() { AudioSource = AudioSourceType.None }, default);
        var later = DateTimeOffset.UtcNow.AddSeconds(6);
        Assert.False(live.ReadStatus(later).HasFrame);
        Assert.False(live.ReadStatus(later.AddSeconds(1)).HasFrame);
        var warning = Assert.Single(capture.Events, e => e.MessageTemplate.Text.Contains("Preflight preview has no frame"));
        Assert.Equal("True", warning.Properties["EngineRunning"].ToString());
        Assert.Equal("5", warning.Properties["CapturedFrames"].ToString());
        Assert.Equal("False", warning.Properties["DecoderExited"].ToString());
    }
    [Fact]
    public async Task InvalidPreviewStreamReportsDecoderFailureWithoutPublishingFrame()
    {
        using var capture = new DiagnosticCapture();
        await using var preview = new PreflightPreviewPipe(FFmpegDiscovery.FindFFmpegExecutable()!);
        await preview.Input.WriteAsync(new byte[4096]);
        await Assert.ThrowsAsync<InvalidDataException>(() => preview.CompleteAsync(default));
        Assert.Null(preview.LatestFrame);
        Assert.Equal(0, preview.FrameCount);
        var failure = Assert.Single(capture.Events, e => e.MessageTemplate.Text.Contains("Preflight preview decoder exited"));
        Assert.NotEqual("0", failure.Properties["ExitCode"].ToString());
        var stderr = Assert.IsType<ScalarValue>(failure.Properties["Stderr"]).Value as string;
        Assert.Contains("EBML", stderr);
        Assert.InRange(stderr!.Length, 1, 4096);
    }

    [Fact]
    public async Task FailedInitializationLogsOriginalExceptionAndKeepsStartBlocked()
    {
        using var capture = new DiagnosticCapture();
        await using var coordinator = new PreflightCoordinator(new(), () => new FailingSession());
        await using var dispatcher = new PreflightIpcDispatcher(coordinator);
        var id = Guid.NewGuid();
        var response = await dispatcher.DispatchAsync(new() { MessageType = "BeginPreflight",
            PayloadJson = JsonSerializer.Serialize(new PreflightRequest(id, Configuration: new())) });
        Assert.True(response.Success, response.ErrorMessage);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        PreflightReply reply;
        do
        {
            await Task.Delay(10, deadline.Token);
            response = await dispatcher.DispatchAsync(new() { MessageType = "GetPreflightStatus",
                PayloadJson = JsonSerializer.Serialize(new PreflightRequest(id)) });
            reply = JsonSerializer.Deserialize<PreflightReply>(response.DataJson!)!;
        } while (reply.Status.Phase != PreflightPhase.Failed);
        Assert.False(reply.Status.HasFrame);
        Assert.NotNull(reply.Status.ErrorCode);
        var failure = Assert.Single(capture.Events, e => e.Exception is IOException);
        Assert.Equal("preflight-start-fault", failure.Exception!.Message);
    }

    private sealed class FailingSession : IPreflightLiveSession
    {
        public Task StartAsync(RecordingConfiguration config, CancellationToken ct) => throw new IOException("preflight-start-fault");
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty;
        public ProjectFrameReply? ReadFrame() => null;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Displays : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => [new(0, "test", new(0, 0, 64, 36), true, 1)];
        public MonitorInfo? GetPrimaryMonitor() => GetMonitors()[0];
        public CaptureRegion GetVirtualScreenBounds() => new(0, 0, 64, 36);
    }
    // Native capture is unavailable on the test host. Model a running producer
    // whose encoded bytes have not reached the real preview decoder.
    private sealed class QuietEngine : IScreenRecorderEngine, IBoundRecordingOutput
    {
        public bool IsRunning { get; set; }
        public TaskCompletionSource? StartGate, StopGate;
        public TaskCompletionSource StartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[]? Payload;
        public bool UseSyntheticCaptureSource { get; set; }
        public TimeSpan CurrentRecordedTime => TimeSpan.FromSeconds(1);
        public long CurrentFramesRecorded => 5;
        public HardwareEncoderType ActiveEncoder => HardwareEncoderType.SoftwareCpu;
        public CaptureSelection? ActiveCapture => null;
        public Stream? OutputStream { get; set; }
        public event EventHandler<string>? EngineErrorOccurred { add { } remove { } }
        public event EventHandler<string>? EngineWarningOccurred { add { } remove { } }
        public event EventHandler? AudioDeviceLost { add { } remove { } }
        public async Task StartRecordingAsync(string path, RecordingConfiguration config, CaptureRegion bounds, CancellationToken ct)
        {
            StartEntered.TrySetResult();
            if (StartGate is { } gate) await gate.Task.WaitAsync(ct);
            IsRunning = true;
            if (Payload is { } bytes) await OutputStream!.WriteAsync(bytes, ct);
        }
        public async Task StopRecordingAsync(CancellationToken ct)
        {
            IsRunning = false; StopEntered.TrySetResult();
            if (StopGate is { } gate) await gate.Task.WaitAsync(ct);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DiagnosticCapture : IDisposable
    {
        private readonly ILogger _previous = Log.Logger;
        private readonly Logger _logger;
        private readonly Sink _sink = new();
        public IEnumerable<LogEvent> Events => _sink.Events;
        public DiagnosticCapture() => Log.Logger = _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();
        public void Dispose() { Log.Logger = _previous; _logger.Dispose(); }
    }
    private sealed class Sink : ILogEventSink
    {
        public readonly ConcurrentQueue<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
