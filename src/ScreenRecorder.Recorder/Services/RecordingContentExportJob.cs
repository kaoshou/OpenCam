// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Recorder.Services;

/// <summary>The coordinator keeps the source owner alive until Completion, including cancellation.</summary>
internal sealed class RecordingContentExportJob : IRecordingExportProgress, IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private RecordingExportStatus _status;
    public RecordingExportStatus Status { get { lock (_sync) return _status; } }
    public Task Completion { get; }

    public RecordingContentExportJob(IRecordingContentExporter exporter, IProjectHandle owner,
        RecordingProject snapshot, string outputDirectory,
        Func<IProjectHandle, RecordingProject, string, CancellationToken, Task>? writeReceipt = null)
    {
        _status = new(Guid.NewGuid(), snapshot.Revision, RecordingExportState.Running, 0);
        Completion = Task.Run(async () =>
        {
            try
            {
                var result = await exporter.ExportAsync(owner, snapshot, outputDirectory, _status.ExportId, this, _cancellation.Token);
                if (result.Success && !string.IsNullOrWhiteSpace(result.FinalPath))
                {
                    using var receiptDeadline = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                    receiptDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        await (writeReceipt ?? ScreenRecorder.Infrastructure.Projects.ProjectExportReceiptStore.WriteAsync)(
                            owner, snapshot, result.FinalPath, receiptDeadline.Token);
                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Export succeeded but its reuse receipt could not be saved"); }
                }
                lock (_sync)
                    _status = result.Success && !string.IsNullOrWhiteSpace(result.FinalPath)
                        ? _status with { State = RecordingExportState.Succeeded, Progress = 1, FinalPath = result.FinalPath }
                        : _status with { State = RecordingExportState.Failed, Error = result.Error ?? "Export did not produce a verified output." };
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            { lock (_sync) _status = _status with { State = RecordingExportState.Canceled }; }
            catch (Exception ex)
            { lock (_sync) _status = _status with { State = RecordingExportState.Failed, Error = ex.Message }; }
        });
    }

    public void Report(double value)
    {
        if (!double.IsFinite(value)) return;
        lock (_sync)
            if (_status.State == RecordingExportState.Running)
                _status = _status with { Progress = Math.Max(_status.Progress, Math.Clamp(value, 0, 1)) };
    }

    public void Cancel() => _cancellation.Cancel();
    public void ReportPhase(RecordingExportPhase phase)
    {
        if (!Enum.IsDefined(phase)) return;
        lock (_sync)
            if (_status.State == RecordingExportState.Running)
                _status = _status with { Phase = phase };
    }
    public void Dispose() => _cancellation.Dispose();
}
