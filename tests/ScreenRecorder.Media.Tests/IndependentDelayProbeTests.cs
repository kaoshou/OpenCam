// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class IndependentDelayProbeTests
{
    [Fact]
    public void CompletedTaskIsObservedWithoutAnAwaitingContinuation()
    {
        // Catches a probe that relies on a queued continuation to observe completion.
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observed = new ManualResetEventSlim();
        var messages = new ConcurrentQueue<string>();
        using var probe = new IndependentDelayProbe(delay.Task, message => {
            messages.Enqueue(message);
            observed.Set();
        });
        delay.SetResult();
        Assert.True(observed.Wait(TimeSpan.FromSeconds(5)), "Independent observer did not report task completion.");
        Assert.Contains(messages, message => message.Contains("status=RanToCompletion"));
    }

    [Fact]
    public void DisposingPendingProbeJoinsObserverAndPreventsLaterReports()
    {
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new ConcurrentQueue<string>();
        var probe = new IndependentDelayProbe(delay.Task, messages.Enqueue);
        probe.Dispose();
        delay.SetResult();
        Assert.Empty(messages); // Join, not a sleep, establishes that no observer remains.
    }
}
