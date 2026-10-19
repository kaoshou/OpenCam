// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class PreflightStatusSnapshotTests
{
    [Fact]
    public async Task PollingStatusDuringFailedStartupNeverLosesTheCapturedOperation()
    {
        await using var coordinator = new PreflightCoordinator(new(), () => new FailingSession());
        using var stop = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var errors = new ConcurrentQueue<Exception>();
        var reader = new Thread(() =>
        {
            entered.Set();
            while (!stop.IsSet)
            {
                try { coordinator.ReadCurrentStatus(); }
                catch (Exception error) { if (errors.Count < 16) errors.Enqueue(error); }
            }
        }) { IsBackground = true };
        reader.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            for (var i = 0; i < 2000; i++)
                await Assert.ThrowsAsync<IOException>(() => coordinator.BeginAsync(Guid.NewGuid(), new(), default));
        }
        finally { stop.Set(); reader.Join(); }
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Select(e => e.ToString())));
        Assert.Null(coordinator.CurrentOperation);
        Assert.Equal(PreflightStatus.Empty, coordinator.ReadCurrentStatus());
        Assert.Throws<InvalidOperationException>(() => coordinator.ReadStatus(Guid.NewGuid(), 1));
    }

    private sealed class FailingSession : IPreflightLiveSession
    {
        public Task StartAsync(RecordingConfiguration config, CancellationToken ct) => throw new IOException("startup-fault");
        public PreflightStatus ReadStatus(DateTimeOffset now) => PreflightStatus.Empty;
        public ProjectFrameReply? ReadFrame() => null;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
