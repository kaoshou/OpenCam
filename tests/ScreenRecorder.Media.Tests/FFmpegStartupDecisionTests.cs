// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class FFmpegStartupDecisionTests
{
    [Fact]
    public async Task FrameConfirmedBeforeDelayedDecisionIsNotDiscardedBecauseDeadlineQueuedFirst()
    {
        var frame = new TaskCompletionSource<bool>();
        var exit = new TaskCompletionSource();
        var deadline = new TaskCompletionSource();
        var context = new QueuedContext();
        var previous = SynchronizationContext.Current;
        Task<bool> decision;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            decision = FFmpegScreenRecorderEngine.WaitForStartupConfirmationAsync(
                frame.Task, exit.Task, deadline.Task, default);
            SynchronizationContext.SetSynchronizationContext(null);
            deadline.SetResult(); // Decision continuation is queued, not yet executed.
            frame.SetResult(true); // Genuine progress arrives before that decision runs.
            context.RunPending();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(await decision);
    }

    [Fact]
    public async Task ExitedEncoderIsNotAcceptedEvenWithBufferedProgress()
    {
        Assert.False(await FFmpegScreenRecorderEngine.WaitForStartupConfirmationAsync(
            Task.FromResult(true), Task.CompletedTask, new TaskCompletionSource().Task, default));
    }

    [Fact]
    public async Task DeadlineWithoutProgressStillRejectsStartup()
    {
        Assert.False(await FFmpegScreenRecorderEngine.WaitForStartupConfirmationAsync(
            new TaskCompletionSource<bool>().Task, new TaskCompletionSource().Task, Task.CompletedTask, default));
    }

    [Fact]
    public async Task CancellationWinsEvenWhenProgressIsAvailable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FFmpegScreenRecorderEngine.WaitForStartupConfirmationAsync(Task.FromResult(true),
                new TaskCompletionSource().Task, Task.CompletedTask, cancellation.Token));
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));
        public void RunPending()
        {
            Assert.NotEmpty(_pending);
            while (_pending.TryDequeue(out var work)) work.Callback(work.State);
        }
    }
}
