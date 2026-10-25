// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenRecorder.Media.Tests;

// Test-only observation: never completes, cancels, or awaits the task being measured.
// In particular, observation must not need the worker/timer continuation under investigation.
internal sealed class IndependentDelayProbe : IDisposable
{
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;

    public IndependentDelayProbe(Task delay, Action<string> trace, string label = "fault-delay")
    {
        var started = IndependentClockSnapshot.Capture();
        _thread = new Thread(() => {
            try
            {
                var nextReport = 5000d;
                while (!_stop.Wait(50))
                {
                    var elapsed = Stopwatch.GetElapsedTime(started.Timestamp).TotalMilliseconds;
                    var completed = delay.IsCompleted;
                    if (!completed && elapsed < nextReport) continue;
                    ThreadPool.GetAvailableThreads(out var available, out _);
                    trace($"{label} independent observation: {started.ElapsedSummary()}, status={delay.Status}, " +
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

// Windows managed TimerQueue uses QueryUnbiasedInterruptTime, whereas Stopwatch
// uses QueryPerformanceCounter. Compare both with native GetTickCount64 instead
// of assuming a late await proves either timer expiry or scheduler starvation.
internal readonly record struct IndependentClockSnapshot(long Timestamp, long TickMilliseconds, ulong? Unbiased100ns)
{
    public static IndependentClockSnapshot Capture()
    {
        var timestamp = Stopwatch.GetTimestamp();
        if (!OperatingSystem.IsWindows()) return new(timestamp, Environment.TickCount64, null);
        var tick = checked((long)GetTickCount64());
        return new(timestamp, tick, QueryUnbiasedInterruptTime(out var unbiased) ? unbiased : null);
    }

    public string ElapsedSummary()
    {
        var now = Capture();
        var unbiased = now.Unbiased100ns is { } end && Unbiased100ns is { } start
            ? ((double)end - start) / 10000 : (double?)null;
        return $"elapsedMs={Stopwatch.GetElapsedTime(Timestamp, now.Timestamp).TotalMilliseconds:F1}, " +
            $"tickMs={now.TickMilliseconds - TickMilliseconds}, unbiasedMs={unbiased?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"}";
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
