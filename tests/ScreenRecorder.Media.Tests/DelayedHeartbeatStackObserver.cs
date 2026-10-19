// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Text;

namespace ScreenRecorder.Media.Tests;

// CI-only evidence collector. Dedicated threads keep observation independent
// of the pool being investigated. No dump, pixels, arguments or credentials.
internal sealed class DelayedHeartbeatStackObserver : IDisposable
{
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread? _observer;

    public DelayedHeartbeatStackObserver(Func<bool> heartbeatHasNotAdvanced)
    {
        var tool = Environment.GetEnvironmentVariable("OPENCAM_TEST_STACK_TOOL");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(tool)) return;
        _observer = new Thread(() => Observe(tool, heartbeatHasNotAdvanced)) { IsBackground = true };
        _observer.Start();
    }

    private void Observe(string tool, Func<bool> heartbeatHasNotAdvanced)
    {
        if (_stop.Wait(5000) || !heartbeatHasNotAdvanced()) return;
        var collectionStarted = Stopwatch.GetTimestamp();
        try
        {
            ThreadPool.GetAvailableThreads(out var available, out _);
            Serilog.Log.Debug("Independent heartbeat observer: workers={Workers}, available={Available}, pending={Pending}",
                ThreadPool.ThreadCount, available, ThreadPool.PendingWorkItemCount);
            using var child = new Process { StartInfo = new(tool) {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true } };
            child.StartInfo.ArgumentList.Add("report");
            child.StartInfo.ArgumentList.Add("--process-id");
            child.StartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            Serilog.Log.Debug("Independent heartbeat stack tool starting at {Timestamp}", DateTimeOffset.UtcNow);
            child.Start();
            // A separate synchronous waiter bounds the tool even when neither
            // timer nor ThreadPool continuation can currently run in testhost.
            var deadline = new Thread(() => {
                try { if (!child.WaitForExit(10000)) child.Kill(true); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Stack tool deadline cleanup failed"); }
            }) { IsBackground = true };
            deadline.Start();
            try
            {
                var report = new StringBuilder();
                string? line;
                while ((line = child.StandardOutput.ReadLine()) != null)
                {
                    if (report.Length < 60000) report.AppendLine(line.Length > 2000 ? line[..2000] : line);
                }
                for (var offset = 0; offset < report.Length; offset += 10000)
                    Serilog.Log.Debug("Delayed heartbeat managed stacks chunk {Chunk}: {Report}",
                        offset / 10000, report.ToString(offset, Math.Min(10000, report.Length - offset)));
            }
            finally { deadline.Join(); }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Delayed heartbeat stack collection failed"); }
        finally
        {
            Serilog.Log.Debug("Independent heartbeat stack tool finished at {Timestamp}; elapsedMs={ElapsedMs}",
                DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(collectionStarted).TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _observer?.Join();
        _stop.Dispose();
    }
}
