// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;

namespace ScreenRecorder.Media.Tests;

// Test-only observation: never completes, cancels, or awaits the task being measured.
// In particular, observation must not need the worker/timer continuation under investigation.
internal sealed class IndependentDelayProbe : IDisposable
{
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;

    public IndependentDelayProbe(Task delay, Action<string> trace)
    {
        var started = Stopwatch.GetTimestamp();
        var ticks = Environment.TickCount64;
        _thread = new Thread(() => {
            try
            {
                var nextReport = 5000d;
                while (!_stop.Wait(50))
                {
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var completed = delay.IsCompleted;
                    if (!completed && elapsed < nextReport) continue;
                    ThreadPool.GetAvailableThreads(out var available, out _);
                    trace($"fault-delay independent observation: elapsedMs={elapsed:F1}, " +
                        $"tickMs={Environment.TickCount64 - ticks}, status={delay.Status}, " +
                        $"workers={ThreadPool.ThreadCount}, available={available}, pending={ThreadPool.PendingWorkItemCount}");
                    if (completed || elapsed >= 20000) return;
                    nextReport += 5000;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Independent fault-delay observation failed");
            }
        }) { IsBackground = true };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join();
        _stop.Dispose();
    }
}
