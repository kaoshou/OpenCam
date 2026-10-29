// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Measure export phases without changing progress or output validation.</summary>
internal sealed class ProjectExportTimingProgress(IProgress<double> target, Guid exportId) : IRecordingExportProgress, IDisposable
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Stopwatch _phaseTime = new();
    private RecordingExportPhase? _phase;
    private bool _completed;

    public void Report(double value)
    {
        target.Report(value);
        if (value >= 1) _completed = true;
    }

    public void ReportPhase(RecordingExportPhase phase)
    {
        if (_phase != phase)
        {
            LogPhase();
            _phase = phase;
            _phaseTime.Restart();
        }
        (target as IRecordingExportProgress)?.ReportPhase(phase);
    }

    private void LogPhase()
    {
        if (_phase is { } phase)
            Log.Information("Project export phase {Phase}; exportId={ExportId}; elapsedMs={ElapsedMs}",
                phase, exportId, _phaseTime.Elapsed.TotalMilliseconds);
    }

    public void Dispose()
    {
        LogPhase();
        Log.Information("Project export timing; exportId={ExportId}; elapsedMs={ElapsedMs}; completed={Completed}",
            exportId, _total.Elapsed.TotalMilliseconds, _completed);
    }
}

internal sealed class ProjectExportStepTimer(Guid exportId, string step) : IDisposable
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    public void Dispose() => Log.Information("Project export step {Step}; exportId={ExportId}; elapsedMs={ElapsedMs}",
        step, exportId, _watch.Elapsed.TotalMilliseconds);
}
