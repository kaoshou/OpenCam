using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public class PreflightAudioTrialTests
{
    [Fact]
    public async Task TimerStartsAfterAudioAcknowledgementAndCannotReplayBeforeSealAndProbe()
    {
        var live = new Capture(); live.Ready.TrySetResult();
        var trial = new Capture();
        var probeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeRelease = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        string? directory = null;
        await using var session = new PreflightAudioSession(() => calls++ == 0 ? live : trial,
            storage: () => { var s = new PreflightTrialStorage(); directory = s.DirectoryPath; return s; },
            validate: async (stream, ct) =>
            {
                Assert.True(stream.CanRead); Assert.False(stream.CanWrite);
                probeEntered.TrySetResult();
                return await probeRelease.Task.WaitAsync(ct);
            });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await session.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, deadline.Token);
        var recording = session.RecordTrialAsync(deadline.Token);
        await trial.Entered.Task.WaitAsync(deadline.Token);
        Assert.Equal(PreflightPhase.TrialStarting, session.ReadStatus(DateTimeOffset.UtcNow).Phase);
        Assert.Equal(0, session.ReadStatus(DateTimeOffset.UtcNow).TrialSeconds);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).HasTrial);
        trial.Ready.TrySetResult();
        await probeEntered.Task.WaitAsync(deadline.Token);
        Assert.Equal(PreflightPhase.TrialFinalizing, session.ReadStatus(DateTimeOffset.UtcNow).Phase);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).HasTrial);
        Assert.False(recording.IsCompleted);
        Assert.True(trial.Disposed);
        probeRelease.TrySetResult(TimeSpan.FromSeconds(5));
        await recording;
        Assert.Equal(PreflightPhase.Reviewing, session.ReadStatus(DateTimeOffset.UtcNow).Phase);
        Assert.True(session.ReadStatus(DateTimeOffset.UtcNow).HasTrial);
        Assert.Equal(5, session.ReadStatus(DateTimeOffset.UtcNow).TrialSeconds);
        Assert.Null(session.ReadFrame());
        await session.StopAsync(deadline.Token);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task CancelThenRecheckCannotReplayOldTrial()
    {
        var live = new Capture(); live.Ready.TrySetResult();
        var trial = new Capture();
        var calls = 0;
        string? directory = null;
        await using var session = new PreflightAudioSession(() => calls++ == 0 ? live : trial,
            storage: () => { var s = new PreflightTrialStorage(); directory = s.DirectoryPath; return s; },
            validate: (_, _) => Task.FromResult(TimeSpan.FromSeconds(5)));
        await session.StartAsync(new() { AudioSource = AudioSourceType.SystemOnly }, default);
        using var cancel = new CancellationTokenSource();
        var recording = session.RecordTrialAsync(cancel.Token);
        await trial.Entered.Task;
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recording);
        Assert.False(session.ReadStatus(DateTimeOffset.UtcNow).HasTrial);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PlayTrialAsync(default));
        await session.StopAsync(default);
        Assert.False(Directory.Exists(directory));
        Assert.True(trial.Disposed);
    }

    private sealed class Capture : IPreflightAudioCapture
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning { get; private set; }
        public bool Disposed;
        public string? ErrorCode => null;
        public async Task StartAsync(RecordingConfiguration configuration, Stream? trialOutput, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Ready.Task.WaitAsync(ct);
            if (trialOutput is not null) await trialOutput.WriteAsync(new byte[32], ct);
            IsRunning = true;
        }
        public Task StopAsync(CancellationToken ct) { IsRunning = false; return Task.CompletedTask; }
        public PreflightSourceResult ReadMicrophone(DateTimeOffset now) => new(PreflightSourceState.Disabled);
        public PreflightSourceResult ReadSystemAudio(DateTimeOffset now) => new(PreflightSourceState.Live, .2, .3);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
