// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    private CancellationTokenSource? _importCancellation;
    private Task? _importTask;
    private ProjectVideoImportStatus? _importStatus;
    public ProjectVideoImportStatus? ImportStatus => Volatile.Read(ref _importStatus);
    public bool IsImporting => ImportStatus?.State == ProjectVideoImportState.Running;

    public Task<ProjectCommandResult> StartVideoImportAsync(string path, long revision, Guid operationId,
        CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        CheckEditablePersistence(revision);
        if (_history is null || _handle is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused) || HasRecoverableDraft)
            throw new InvalidOperationException("VideoImportUnavailable");
        if (mediaProcess is null) throw new InvalidOperationException("VideoImportBackend");
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length > 200) name = name[..200];
        ProjectValidation.ValidateName(name);
        await FlushEditsAsync(ct);
        var project = Current!;
        var handle = _handle;
        _importCancellation?.Dispose();
        _importCancellation = new();
        var token = _importCancellation.Token;
        Volatile.Write(ref _importStatus, new(operationId, ProjectVideoImportState.Running, "Checking", name));
        _importTask = Task.Run(async () =>
        {
            try
            {
                Log.Information("Video import started: operation={OperationId}, project={ProjectId}", operationId, project.ProjectId);
                var source = await new ProjectVideoImporter(mediaProcess(), probe).ImportAsync(handle, path,
                    phase => Volatile.Write(ref _importStatus, new(operationId, ProjectVideoImportState.Running, phase, name)), token);
                await _gate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (_handle != handle || Current!.Revision != project.Revision)
                        throw new InvalidOperationException("VideoImportChanged");
                    var next = ProjectInsertion.Apply(project,
                        ProjectInsertion.Anchor(project, ProjectTimeline.Build(project).DurationTicks), source, operationId);
                    next = next with { Clips = next.Clips.SetItem(next.Clips.Length - 1, next.Clips[^1] with { Name = name }) };
                    // A verified asset is retained if publication succeeded but this manifest write fails.
                    await handle.SaveAsync(next, handle.Current.Revision, token);
                    _history!.AcceptRecordingInsertion(next);
                    LastError = null;
                    Volatile.Write(ref _importStatus, new(operationId, ProjectVideoImportState.Succeeded, "Done", name, ClipId: source.Id));
                    Log.Information("Video import committed: operation={OperationId}, source={SourceId}", operationId, source.Id);
                }
                finally { _gate.Release(); }
            }
            catch (OperationCanceledException)
            {
                Volatile.Write(ref _importStatus, new(operationId, ProjectVideoImportState.Canceled, "Done", name));
                Log.Information("Video import canceled: operation={OperationId}", operationId);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _importStatus, new(operationId, ProjectVideoImportState.Failed, "Done", name,
                    ex.Message.StartsWith("VideoImport", StringComparison.Ordinal) ? ex.Message : "VideoImportInvalid"));
                Log.Error(ex, "Video import failed: operation={OperationId}", operationId);
            }
        });
    }, operationId, "video-import:" + revision + ":" + path, ct);

    public async Task<ProjectCommandResult> CancelVideoImportAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            if (ImportStatus?.Id != id) throw new InvalidOperationException("VideoImportChanged");
            _importCancellation?.Cancel();
            return Result(true);
        }
        finally { _gate.Release(); }
    }
}
