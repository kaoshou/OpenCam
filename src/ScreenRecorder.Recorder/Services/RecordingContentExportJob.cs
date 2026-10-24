// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Recorder.Services;

/// <summary>The coordinator keeps the source owner alive until Completion, including cancellation.</summary>
internal sealed class RecordingContentExportJob : IRecordingExportProgress, IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private RecordingExportStatus _status;
    public RecordingExportStatus Status
    {
        get
        {
            lock (_sync)
            {
                // A terminal result is actionable only after the worker has
                // released its resources, matching the coordinator's owner gate.
                return _status.State != RecordingExportState.Running && !Completion.IsCompleted
                    ? _status with { State = RecordingExportState.Running, FinalPath = null, Error = null }
                    : _status;
            }
        }
    }
    public Task Completion { get; }
    public ProjectExportOptions? Options { get; }

    public RecordingContentExportJob(IRecordingContentExporter exporter, IProjectHandle owner,
        RecordingProject snapshot, string outputDirectory,
        Func<IProjectHandle, RecordingProject, string, CancellationToken, Task>? writeReceipt = null,
        string? fileName = null, bool webPlayer = false, ProjectExportOptions? exportOptions = null,
        WebPlayerLanguageOptions? webPlayerLanguages = null)
    {
        Options = exportOptions;
        var languages = WebPlayerLanguageOptions.Normalize(webPlayerLanguages);
        _status = new(Guid.NewGuid(), snapshot.Revision, RecordingExportState.Running, 0);
        Completion = Task.Run(async () =>
        {
            try
            {
                using var destination = fileName is null ? null : ScreenRecorder.Infrastructure.Session.BoundDirectory.Open(outputDirectory);
                IRecordingContentExporter outputExporter = webPlayer ? new ProjectWebPlayerExporter(exporter, languages) : exporter;
                var result = await outputExporter.ExportAsync(owner, snapshot, outputDirectory, _status.ExportId, this, _cancellation.Token);
                if (destination is not null && result.Success && result.FinalPath is { } generated)
                {
                    using var generatedDirectory = ScreenRecorder.Infrastructure.Session.BoundDirectory.Open(Path.GetDirectoryName(generated)!);
                    if (!string.Equals(Path.GetFullPath(generatedDirectory.CurrentPath),
                        Path.GetFullPath(destination.CurrentPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidDataException("Export destination changed.");
                    // Atomic no-replace publication: a newly appeared file is never overwritten.
                    result = result with { FinalPath = destination.PublishVerified(Path.GetFileName(generated), fileName!) };
                }
                if (!webPlayer && result.Success && !string.IsNullOrWhiteSpace(result.FinalPath))
                {
                    using var receiptDeadline = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                    receiptDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        if (exportOptions is not null && writeReceipt is null)
                            await ScreenRecorder.Infrastructure.Projects.ProjectExportReceiptStore.WriteScopedAsync(
                                owner, snapshot, result.FinalPath, exportOptions, receiptDeadline.Token);
                        else
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
