using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioSessionTests
{
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
