using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Platform.Windows;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioCaptureTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task BothSourcesInitializeBeforeEitherCompletes()
    {
        var system = new SystemSource { HoldStart = true };
        var mic = new GatedMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system, mic);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = capture.StartAsync(new() { AudioSource = AudioSourceType.SystemAndMicrophone }, null, deadline.Token);
        try
        {
            await system.Entered.Task.WaitAsync(deadline.Token);
            await mic.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(capture.IsRunning);
        }
        finally
        {
            system.Release.TrySetResult();
            mic.Release.TrySetResult();
            await start;
            await capture.StopAsync(deadline.Token);
        }
        Assert.False(system.IsCapturing);
        Assert.False(mic.IsCapturing);
    }

    private sealed class GatedMicrophone : IMicrophoneCapture
    {
        public bool IsSupported => true;
        public bool IsCapturing { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
        public async Task<MicrophoneCaptureInfo?> StartCaptureAsync(string? deviceId, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            IsCapturing = true;
            return new("", 48000, 2, "-re -f lavfi -i sine=frequency=880:sample_rate=48000");
        }
        public Task StopCaptureAsync(CancellationToken cancellationToken = default)
        { IsCapturing = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task FailedMicrophoneCancelsAndJoinsPendingSystemStart()
    {
        var system = new SystemSource { HoldStart = true };
        var mic = new FailingMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system, mic);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => capture.StartAsync(
            new() { AudioSource = AudioSourceType.SystemAndMicrophone }, null, deadline.Token));
        Assert.False(system.IsCapturing);
        Assert.True(system.Stops > 0);
        Assert.False(capture.IsRunning);
        Assert.NotNull(capture.ErrorCode);
    }

    [Fact]
    public async Task CancelledDualSourceStartReleasesBothSources()
    {
        var system = new SystemSource { HoldStart = true };
        var mic = new GatedMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system, mic);
        using var cancellation = new CancellationTokenSource();
        var start = capture.StartAsync(new() { AudioSource = AudioSourceType.SystemAndMicrophone }, null, cancellation.Token);
        await Task.WhenAll(system.Entered.Task, mic.Entered.Task).WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(system.IsCapturing);
        Assert.False(mic.IsCapturing);
        Assert.True(system.Stops > 0);
        Assert.False(capture.IsRunning);
    }

    [Fact]
    public async Task FailureJoinsCancellationIgnoringSourceBeforeCleanup()
    {
        var system = new SystemSource { HoldStart = true, IgnoreCancellation = true };
        var mic = new FailingMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system, mic);
        var start = capture.StartAsync(new() { AudioSource = AudioSourceType.SystemAndMicrophone }, null, default);
        try
        {
            await system.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(mic.Started);
            Assert.False(start.IsCompleted);
            Assert.Equal(0, system.Stops);
        }
        finally { system.Release.TrySetResult(); }
        await Assert.ThrowsAsync<IOException>(() => start);
        Assert.True(system.Stops > 0);
        Assert.False(system.IsCapturing);
        Assert.False(capture.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableBestEffortMeterDoesNotPreventActualAudioTrial(bool throws)
    {
        var meter = new UnavailableMeter(throws);
        await using var capture = new FFmpegPreflightAudioCapture(new ToneInput(), observer: meter);
        using var output = new MemoryStream();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await capture.StartAsync(new() { AudioSource = AudioSourceType.MicrophoneOnly, MicrophoneDeviceId = "selected-device" },
            output, deadline.Token);
        Assert.True(capture.IsRunning);
        Assert.Null(capture.ErrorCode);
        Assert.Equal(PreflightSourceState.Unavailable, capture.ReadMicrophone(DateTimeOffset.UtcNow).State);
        await Task.Delay(200, deadline.Token);
        await capture.StopAsync(deadline.Token);
        Assert.Null(capture.ErrorCode);
        Assert.True(output.Length > 0);
        Assert.True(meter.Stopped);
    }

    [Fact]
    public async Task UnavailableMeterDoesNotHideActualCaptureStartupFailure()
    {
        var meter = new UnavailableMeter(false);
        await using var capture = new FFmpegPreflightAudioCapture(new InvalidInput(), observer: meter);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<IOException>(() => capture.StartAsync(new()
            { AudioSource = AudioSourceType.MicrophoneOnly, MicrophoneDeviceId = "selected-device" }, null, deadline.Token));
        Assert.False(capture.IsRunning);
        Assert.NotNull(capture.ErrorCode);
        Assert.True(meter.Stopped);
    }

    [Fact]
    public async Task UnavailableMeterStillRequiresConfirmedShutdown()
    {
        var meter = new UnavailableMeter(false);
        await using var capture = new FFmpegPreflightAudioCapture(new ToneInput(), observer: meter);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await capture.StartAsync(new() { AudioSource = AudioSourceType.MicrophoneOnly, MicrophoneDeviceId = "selected-device" }, null, deadline.Token);
        meter.FailStop = true;
        try { await Assert.ThrowsAsync<AggregateException>(() => capture.StopAsync(deadline.Token)); }
        finally { meter.FailStop = false; }
        await capture.StopAsync(deadline.Token);
    }

    private sealed class InvalidInput : IPreflightAudioPlatformProvider
    {
        public string BuildPreflightAudioInputArguments(RecordingConfiguration configuration, bool synthetic,
            string? systemAudioInput, string? microphoneAudioInput) => "-f lavfi -i opencam_invalid_audio_filter";
    }

    private sealed class ToneInput : IPreflightAudioPlatformProvider
    {
        public string BuildPreflightAudioInputArguments(RecordingConfiguration configuration, bool synthetic,
            string? systemAudioInput, string? microphoneAudioInput) =>
            PreflightAudioFormat.BuildInput(configuration, true, null, null, mac: false);
    }
    private sealed class UnavailableMeter(bool throws) : IMicrophoneLevelObserver
    {
        public bool Stopped, FailStop;
        public Task<bool> StartAsync(string deviceId, CancellationToken ct) => throws
            ? Task.FromException<bool>(new IOException("meter initialization unavailable")) : Task.FromResult(false);
        public AudioLevelSample? ReadLatestLevel(DateTimeOffset now) =>
            throw new InvalidOperationException("An unavailable meter must not be read.");
        public Task StopAsync(CancellationToken ct)
        { Stopped = true; return FailStop ? Task.FromException(new IOException("meter shutdown unconfirmed")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task StartWaitsForAcknowledgement()
    {
        var system = new SystemSource { HoldStart = true };
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var start = capture.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, null, deadline.Token);
        await system.Entered.Task.WaitAsync(deadline.Token);
        Assert.False(start.IsCompleted);
        Assert.False(capture.IsRunning);
        Assert.Equal(PreflightSourceState.Waiting, capture.ReadSystemAudio(DateTimeOffset.UtcNow).State);
        system.Release.TrySetResult();
        await start;
        Assert.True(capture.IsRunning);
        await capture.StopAsync(deadline.Token);
        Assert.False(capture.IsRunning);
    }

    [Fact]
    public async Task UnselectedSourceNeverStarts()
    {
        var mic = new FailingMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), new SystemSource(), mic);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await capture.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, null, deadline.Token);
        Assert.True(capture.IsRunning);
        Assert.False(mic.Started);
        Assert.Equal(PreflightSourceState.Disabled, capture.ReadMicrophone(DateTimeOffset.UtcNow).State);
        await capture.StopAsync(deadline.Token);
    }

    [Fact]
    public async Task PartialInitializationReleasesSources()
    {
        var system = new SystemSource();
        var mic = new FailingMicrophone();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system, mic);
        await Assert.ThrowsAsync<IOException>(() => capture.StartAsync(
            new() { AudioSource = AudioSourceType.SystemAndMicrophone }, null, default));
        Assert.True(mic.Started);
        Assert.False(system.IsCapturing);
        Assert.True(system.Stops > 0);
        Assert.False(capture.IsRunning);
        Assert.NotNull(capture.ErrorCode);
    }

    [Fact]
    public async Task StoppedCaptureCannotReadLevels()
    {
        using var diagnostics = new PreflightTrialTestDiagnostics();
        var clock = IndependentClockSnapshot.Capture();
        var system = new SystemSource();
        await using var capture = new FFmpegPreflightAudioCapture(new WindowsFFmpegProvider(), system);
        using var observer = new DelayedHeartbeatStackObserver(() => !capture.IsRunning);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await capture.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, null, deadline.Token);
            Assert.Equal(.2, capture.ReadSystemAudio(DateTimeOffset.UtcNow).Rms);
            await capture.StopAsync(deadline.Token);
            var reads = system.Reads;
            Assert.Equal(PreflightSourceState.Waiting, capture.ReadSystemAudio(DateTimeOffset.UtcNow).State);
            Assert.Equal(reads, system.Reads);
        }
        finally { output.WriteLine($"Stopped audio capture clocks: {clock.ElapsedSummary()}\n{diagnostics.Snapshot()}"); }
    }

    private sealed class SystemSource : ISystemAudioLoopbackCapture, IAudioLevelSource
    {
        public bool IsSupported => true;
        public bool IsCapturing { get; private set; }
        public bool HoldStart { get; init; }
        public bool IgnoreCancellation { get; init; }
        public int Stops, Reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
        public async Task<SystemAudioCaptureInfo?> StartCaptureAsync(int monitorIndex, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (HoldStart) await Release.Task.WaitAsync(IgnoreCancellation ? CancellationToken.None : cancellationToken);
            IsCapturing = true;
            return new("", 48000, 2, "-re -f lavfi -i sine=frequency=440:sample_rate=48000");
        }
        public Task StopCaptureAsync(CancellationToken cancellationToken = default)
        { IsCapturing = false; Stops++; return Task.CompletedTask; }
        public AudioLevelSample? ReadLatestLevel(DateTimeOffset now)
        {
            if (!IsCapturing) throw new ObjectDisposedException("system");
            Reads++; return new(.2, .3, now);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingMicrophone : IMicrophoneCapture
    {
        public bool Started;
        public bool IsSupported => true;
        public bool IsCapturing => false;
        public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
        public Task<MicrophoneCaptureInfo?> StartCaptureAsync(string? deviceId, CancellationToken cancellationToken = default)
        { Started = true; throw new IOException("microphone initialization failed"); }
        public Task StopCaptureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
