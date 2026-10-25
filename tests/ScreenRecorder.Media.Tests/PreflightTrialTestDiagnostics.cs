// SPDX-License-Identifier: AGPL-3.0-or-later
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScreenRecorder.Media.Tests;

internal sealed class PreflightTrialTestDiagnostics : IDisposable
{
    private readonly ILogger _previous = Log.Logger;
    private readonly Logger _logger;
    private readonly Sink _sink = new();
    public PreflightTrialTestDiagnostics() => Log.Logger = _logger = new LoggerConfiguration()
        .MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();
    public string Snapshot() => _sink.Snapshot();
    public void Dispose() { Log.Logger = _previous; _logger.Dispose(); }
    private sealed class Sink : ILogEventSink
    {
        private readonly Queue<string> _events = new();
        public void Emit(LogEvent value)
        {
            var template = value.MessageTemplate.Text;
            if (value.Level < LogEventLevel.Warning && !template.StartsWith("[FFmpeg Stderr]", StringComparison.Ordinal) &&
                !template.StartsWith("Independent heartbeat observer", StringComparison.Ordinal) &&
                !template.StartsWith("Independent heartbeat stack tool", StringComparison.Ordinal) &&
                !template.StartsWith("Independent task observation", StringComparison.Ordinal) &&
                !template.StartsWith("Independent timer", StringComparison.Ordinal) &&
                !template.StartsWith("Preflight phase observation", StringComparison.Ordinal) &&
                !template.StartsWith("Preflight test setup", StringComparison.Ordinal) &&
                !template.StartsWith("Direct preview startup observation", StringComparison.Ordinal) &&
                !template.StartsWith("Delayed heartbeat", StringComparison.Ordinal)) return;
            var text = value.RenderMessage();
            lock (_events)
            {
                _events.Enqueue(text.Length > 10000 ? text[..10000] : text);
                while (_events.Count > 32) _events.Dequeue();
            }
        }
        public string Snapshot()
        {
            lock (_events)
            {
                var text = string.Join("\n", _events);
                return text.Length > 120000 ? text[^120000..] : text;
            }
        }
    }
}

public sealed class PreflightTrialTestDiagnosticsTests
{
    [Fact]
    public void ReportRetainsBoundedRecentFailuresAndStacksWithoutLaunchArguments()
    {
        using var capture = new PreflightTrialTestDiagnostics();
        for (var i = 0; i < 100; i++) Log.Warning("trial fault {Index}: {Detail}", i, new string('x', 5000));
        Log.Information("launch arguments {Arguments}", "must-not-be-copied");
        Log.Debug("Independent heartbeat observer: {Workers}", 8);
        Log.Debug("Delayed heartbeat managed stacks chunk {Chunk}: {Report}", 0, "worker-stack-evidence");
        Log.Debug("Direct preview startup observation: task={Task}", "WaitingForActivation");
        Log.Debug("Independent heartbeat stack tool starting at {Timestamp}", "bounded-start-time");
        Log.Debug("Independent task observation: {Observation}", "preflight-clock-evidence");
        Log.Debug("Preflight test setup engine started: elapsedMs={ElapsedMs}", 100);
        Log.Debug("Independent timer canary: {Observation}", "clock-canary-evidence");
        Log.Debug("Preflight phase observation: stage={Stage}", "phase-boundary-evidence");
        var report = capture.Snapshot();
        Assert.Contains("trial fault 99:", report);
        Assert.DoesNotContain("trial fault 0:", report);
        Assert.Contains("worker-stack-evidence", report);
        Assert.Contains("Direct preview startup observation", report);
        Assert.Contains("bounded-start-time", report);
        Assert.Contains("preflight-clock-evidence", report);
        Assert.Contains("Preflight test setup engine started", report);
        Assert.Contains("clock-canary-evidence", report);
        Assert.Contains("phase-boundary-evidence", report);
        Assert.DoesNotContain("must-not-be-copied", report);
        Assert.InRange(report.Length, 1, 120000);
    }
}
