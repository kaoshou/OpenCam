using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioSessionTests
{
    [Theory]
    [InlineData(AudioSourceType.MicrophoneOnly)]
    [InlineData(AudioSourceType.SystemOnly)]
    [InlineData(AudioSourceType.SystemAndMicrophone)]
    public async Task StatusDuringInitialIdentityReadDoesNotLatchADeviceChange(AudioSourceType source)
    {
        var capture = new ControlledCapture();
        capture.Ready.TrySetResult();
        using var releaseIdentity = new ManualResetEventSlim();
        var enteredIdentity = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await using var session = new PreflightAudioSession(() => capture, _ =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                enteredIdentity.TrySetResult();
                if (!releaseIdentity.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return "unchanged-device";
        });
        var start = Task.Run(() => session.StartAsync(new() { AudioSource = source }, default));
        PreflightStatus pending;
        try
        {
            await enteredIdentity.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending = session.ReadStatus(DateTimeOffset.UtcNow);
        }
        finally
        {
            releaseIdentity.Set();
            await start.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(PreflightPhase.Preparing, pending.Phase);
        Assert.False(pending.AudioReady);
        Assert.Null(pending.ErrorCode);
        var ready = session.ReadStatus(DateTimeOffset.UtcNow);
        Assert.Null(ready.ErrorCode);
        Assert.True(ready.AudioReady);
    }

    [Fact]
    public async Task DeviceChangeAfterIdentityReadButBeforeCaptureAcknowledgementIsStillDetected()
    {
        var capture = new ControlledCapture();
        var identity = "first";
        await using var session = new PreflightAudioSession(() => capture, _ => identity);
        var start = session.StartAsync(new() { AudioSource = AudioSourceType.MicrophoneOnly }, default);
        identity = "changed";
        var pending = session.ReadStatus(DateTimeOffset.UtcNow);
        capture.Ready.TrySetResult();
        await start;
        Assert.Equal("PreflightAudioUnavailable", pending.ErrorCode);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).AudioReady);
    }

    [Fact]
    public async Task DisablingLastSourceAllowsConfirmationWithoutOpeningDevicesOrTrial()
    {
        await using var session = new PreflightAudioSession(() => throw new InvalidOperationException("No devices selected."));
        await session.StartAsync(new() { AudioSource = AudioSourceType.None }, default);
        var status = session.ReadStatus(DateTimeOffset.UtcNow);
        Assert.True(status.AudioReady);
        Assert.Equal(PreflightSourceState.Disabled, status.Microphone.State);
        Assert.Equal(PreflightSourceState.Disabled, status.SystemAudio.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RecordTrialAsync(default));
    }

    [Fact]
    public async Task AudioReadyRequiresAcknowledgementButNeverRequiresVideoFrames()
    {
        var capture = new ControlledCapture();
        await using var session = new PreflightAudioSession(() => capture);
        var start = session.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, default);
        Assert.Equal(PreflightPhase.Preparing, session.ReadStatus(DateTimeOffset.UtcNow).Phase);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).AudioReady);
        capture.Ready.TrySetResult();
        await start;
        var status = session.ReadStatus(DateTimeOffset.UtcNow);
        Assert.True(status.AudioReady);
        Assert.Equal(PreflightSourceState.Silent, status.SystemAudio.State);
        Assert.False(status.HasFrame);
        Assert.Null(session.ReadFrame());
        await session.StopAsync(default);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).AudioReady);
        Assert.True(capture.Disposed);
    }

    [Fact]
    public async Task ChangedAudioIdentityInvalidatesConfirmation()
    {
        var capture = new ControlledCapture();
        capture.Ready.TrySetResult();
        var identity = "first";
        await using var session = new PreflightAudioSession(() => capture, _ => identity);
        await session.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, default);
        identity = "changed";
        var status = session.ReadStatus(DateTimeOffset.UtcNow);
        Assert.False(status.AudioReady);
        Assert.NotNull(status.ErrorCode);
    }

    private sealed class ControlledCapture : IPreflightAudioCapture
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; }
        public bool Disposed;
        public string? ErrorCode => null;
        public async Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)
        { await Ready.Task.WaitAsync(ct); IsRunning = true; }
        public Task StopAsync(CancellationToken ct) { IsRunning = false; return Task.CompletedTask; }
        public PreflightSourceResult ReadMicrophone(DateTimeOffset now) => new(PreflightSourceState.Disabled);
        public PreflightSourceResult ReadSystemAudio(DateTimeOffset now) => new(PreflightSourceState.Silent);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
