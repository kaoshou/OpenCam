// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Security.Cryptography;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Projects;
using ScreenRecorder.Media.Probe;

namespace ScreenRecorder.Recorder.Services;

/// <summary>The recorder process is the single project writer and recording owner.</summary>
public sealed class ProjectRecordingCoordinator(RecordingOrchestrator recorder, IProjectStore store,
    IProjectSourceProbe probe, IRecordingContentExporter? exporter = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ProjectSegmentCommitter _committer = new(probe);
    private readonly Dictionary<Guid, (string Fingerprint, ProjectCommandResult Result)> _operations = new();
    private IProjectHandle? _handle;
    private ProjectEditHistory? _history;
    private RecordingSession? _session;
    private RecordingContentExportJob? _export;
    private Func<Task>? _stopPreview;
    internal void AttachPreviewStop(Func<Task> stop)
    {
        if (_stopPreview is not null) throw new InvalidOperationException("Preview owner already registered.");
        _stopPreview = stop;
    }
    public RecordingExportStatus? ExportStatus => _export?.Status;
    private bool IsExporting => _export is not null && !_export.Completion.IsCompleted;
    public RecordingProject? Current => _history?.Current ?? _handle?.Current;
    public bool CanUndo => _history?.CanUndo == true;
    public bool CanRedo => _history?.CanRedo == true;
    public long SavedRevision => _handle?.Current.Revision ?? 0;
    public bool IsDirty => Current?.Revision != _handle?.Current.Revision;
    public bool NeedsRecoveryConfirmation => _handle?.NeedsRecoveryConfirmation == true;
    public string? ProjectDirectory => _handle?.ProjectDirectory;
    public string? OutputDirectory { get; private set; }
    public ProjectMode Mode { get; private set; } = ProjectMode.Closed;
    public string? LastError { get; private set; }

    internal async Task<ProjectMediaLease> OpenMediaSourceAsync(Guid projectId, long revision,
        Guid clipId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = Current;
            if (_handle is null || current?.ProjectId != projectId || current.Revision != revision ||
                Mode is not (ProjectMode.Ready or ProjectMode.Paused))
                throw new InvalidOperationException("Media snapshot is no longer available.");
            var clip = current.Clips.SingleOrDefault(c => c.Id == clipId)
                ?? throw new InvalidDataException("Clip is not part of the current project.");
            var source = current.Sources.Single(s => s.Id == clip.SourceId);
            return new(ProjectPathPolicy.OpenSource(_handle, source.RelativePath), source, clip);
        }
        finally { _gate.Release(); }
    }

    public async Task ReconcileRecorderStatusAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is null) return;
            if (recorder.CurrentState == RecordingState.Completed && recorder.CurrentSession is null)
            {
                var reason = _session.ErrorMessage ?? _session.StopReason ?? "Recording stopped.";
                try
                {
                    await CommitSessionAsync(Guid.NewGuid(), ct);
                    _session = null;
                    Mode = ProjectMode.Ready;
                }
                catch (Exception ex) { Mode = ProjectMode.SaveFailed; reason += " " + ex.Message; }
                LastError = reason;
            }
            else if (recorder.CurrentState == RecordingState.Failed)
            {
                Mode = ProjectMode.SaveFailed;
                LastError = _session.ErrorMessage ?? "Recording stopped unexpectedly; retry Finish to save the sources.";
            }
            else if (recorder.CurrentState is RecordingState.Stopping or RecordingState.Finalizing)
                Mode = ProjectMode.SavingSegment;
        }
        finally { _gate.Release(); }
    }
    private ProjectCommandResult Result(bool success, Guid? op = null) =>
        new(success, success ? null : LastError, Current?.ProjectId, Current?.Revision ?? 0, Mode, op);

    private async Task<ProjectCommandResult> Run(Func<Task> action, Guid? op, string fingerprint, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (op == Guid.Empty) throw new InvalidDataException("Operation ID is required.");
            if (op is Guid key && _operations.TryGetValue(key, out var old))
            {
                if (old.Fingerprint != fingerprint) throw new InvalidDataException("Operation ID reused for different command.");
                return old.Result;
            }
            // Persisting already accepted edits is safe even if audio shutdown is unconfirmed.
            if (_stopPreview is not null && fingerprint != "save") await _stopPreview();
            await action();
            LastError = null;
            var result = Result(true, op);
            if (op is Guid id) _operations.Add(id, (fingerprint, result));
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            var result = Result(false, op);
            if (op is Guid id && !_operations.ContainsKey(id)) _operations.Add(id, (fingerprint, result));
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task<ProjectCommandResult> CreateAsync(string parent, string name, CancellationToken ct = default) => Run(async () =>
    {
        RequireClosed();
        _handle = await store.CreateAsync(parent, name, ct);
        _history = new(_handle.Current);
        Mode = ProjectMode.Ready;
    }, null, "create", ct);

    public Task<ProjectCommandResult> OpenAsync(string path, CancellationToken ct = default) => Run(async () =>
    {
        RequireClosed();
        _handle = await store.OpenAsync(path, ct);
        Mode = ProjectMode.Interrupted;
        if (_handle.NeedsRecoveryConfirmation) throw new InvalidDataException("Project backup recovery requires confirmation.");
        await _committer.ReconcileAsync(_handle, ct);
        foreach (var session in await ProjectSessionCatalog.ReadAsync(_handle, ct))
            await _committer.CommitAsync(_handle, session, Guid.NewGuid(), ct);
        await ValidateSourcesAsync(ct);
        _history = new(_handle.Current);
        Mode = ProjectMode.Ready;
    }, null, "open", ct);

    public Task<ProjectCommandResult> RestoreBackupAsync(CancellationToken ct = default) => Run(async () =>
    {
        if (_handle is null || Mode != ProjectMode.Interrupted || !NeedsRecoveryConfirmation)
            throw new InvalidOperationException("No backup recovery is pending.");
        await _handle.RestoreBackupAsync(ct);
        await _committer.ReconcileAsync(_handle, ct);
        foreach (var session in await ProjectSessionCatalog.ReadAsync(_handle, ct))
            await _committer.CommitAsync(_handle, session, Guid.NewGuid(), ct);
        await ValidateSourcesAsync(ct);
        _history = new(_handle.Current);
        Mode = ProjectMode.Ready;
    }, null, "restore", ct);

    public Task<ProjectCommandResult> StartNewContentAsync(RecordingConfiguration config, Guid operationId,
        CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (!string.IsNullOrWhiteSpace(config.ProjectName))
            ProjectValidation.ValidateName(config.ProjectName.Trim());
        if (_session is not null || recorder.CurrentSession is not null ||
            Mode is not (ProjectMode.Closed or ProjectMode.Ready))
            throw new InvalidOperationException("Finish and save the current recording before starting new content.");
        if (_handle is not null)
        {
            await FlushEditsAsync(ct);
            await _handle.DisposeAsync();
            _handle = null;
            _history = null;
            Mode = ProjectMode.Closed;
        }
        OutputDirectory = null;
        _export?.Dispose();
        _export = null;
        _handle = await new RecordingContentFactory(store, TimeProvider.System).CreateAsync(config.OutputDirectory, ct, config.ProjectName);
        _history = new(_handle.Current);
        Mode = ProjectMode.Ready;
        await StartCoreAsync(config, ct);
    }, operationId, "new-content:" + JsonSerializer.Serialize(config), ct);

    public Task<ProjectCommandResult> StartAsync(RecordingConfiguration config, Guid operationId, CancellationToken ct = default) =>
        Run(() => StartCoreAsync(config, ct), operationId, "start:" + JsonSerializer.Serialize(config), ct);

    public Task<ProjectCommandResult> StartInsertionAsync(RecordingConfiguration config, long expectedRevision,
        long timelineTicks, Guid operationId, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (_handle is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused) ||
            Current!.Revision != expectedRevision || operationId == Guid.Empty)
            throw new InvalidOperationException("Insertion requires a saved, current editable timeline.");
        await FlushEditsAsync(ct);
        await ValidateSourcesAsync(ct);
        // Validate before ending a paused session; invalid positions must not change its state.
        _ = ProjectInsertion.Anchor(_handle.Current, timelineTicks);
        if (Mode == ProjectMode.Paused) await FinishCoreAsync(operationId, ct);
        var anchor = ProjectInsertion.Anchor(_handle.Current, timelineTicks);
        var sessionId = "insert_" + operationId.ToString("N");
        await new ProjectInsertionJournal().PrepareAsync(_handle, new(operationId, sessionId, anchor), ct);
        await StartCoreAsync(config, ct, sessionId);
    }, operationId, $"insert:{expectedRevision}:{timelineTicks}:" + JsonSerializer.Serialize(config), ct);

    private async Task StartCoreAsync(RecordingConfiguration config, CancellationToken ct, string? preparedSessionId = null)
    {
        RequireNoExport();
        if (_handle is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Project is not ready to record.");
        await FlushEditsAsync(ct);
        await ValidateSourcesAsync(ct);
        if (Mode == ProjectMode.Paused)
        {
            if (recorder.CurrentSession != _session || recorder.CurrentState != RecordingState.Paused)
                throw new InvalidOperationException("Recording session ownership changed.");
            var update = await recorder.UpdatePausedConfigurationAsync(config, ct);
            if (!update.Success) throw new IOException(update.ErrorMessage);
            var resume = await recorder.ResumeRecordingAsync(ct);
            if (!resume.Success) throw new IOException(resume.ErrorMessage);
        }
        else
        {
            OutputDirectory = config.OutputDirectory;
            var start = await recorder.StartProjectRecordingAsync(config, new(_handle, preparedSessionId), ct);
            if (recorder.CurrentSession?.ProjectId == _handle.Current.ProjectId)
                _session = recorder.CurrentSession;
            if (!start.Success)
            {
                if (_session is not null) Mode = ProjectMode.SaveFailed;
                throw new IOException(start.ErrorMessage);
            }
        }
        Mode = ProjectMode.Recording;
    }

    public Task<ProjectCommandResult> PauseAsync(Guid operationId, CancellationToken ct = default) => Run(async () =>
    {
        if (_handle is null || _session is null || Mode != ProjectMode.Recording)
            throw new InvalidOperationException("Project is not recording.");
        Mode = ProjectMode.SavingSegment;
        try
        {
            var pause = await recorder.PauseRecordingAsync(ct);
            if (!pause.Success) throw new IOException(pause.ErrorMessage);
            await CommitSessionAsync(operationId, ct);
            Mode = ProjectMode.Paused;
        }
        catch { Mode = ProjectMode.SaveFailed; throw; }
    }, operationId, "pause", ct);

    public Task<ProjectCommandResult> FinishAsync(Guid operationId, CancellationToken ct = default) =>
        Run(() => FinishCoreAsync(operationId, ct), operationId, "finish", ct);

    private async Task FinishCoreAsync(Guid operationId, CancellationToken ct)
    {
        if (_handle is null || _session is null || Mode is not (ProjectMode.Recording or ProjectMode.Paused or ProjectMode.SaveFailed))
            throw new InvalidOperationException("No project recording session to finish.");
        Mode = ProjectMode.SavingSegment;
        try
        {
            await FlushEditsAsync(ct);
            if (_session.State != RecordingState.Completed)
            {
                var stop = await recorder.FinishProjectSessionAsync(_session, new(_handle), ct);
                if (!stop.Success) throw new IOException(stop.ErrorMessage);
            }
            await CommitSessionAsync(operationId, ct);
            _session = null;
            Mode = ProjectMode.Ready;
        }
        catch { Mode = ProjectMode.SaveFailed; throw; }
    }

    public Task<ProjectCommandResult> FinishAndExportAsync(Guid operationId, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        await FinishCoreAsync(operationId, ct);
        BeginExport();
    }, operationId, "finish-export", ct);

    public Task<ProjectCommandResult> RetryExportAsync(Guid operationId, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (_handle is null || Mode != ProjectMode.Ready)
            throw new InvalidOperationException("Finish and save recording before exporting.");
        await FlushEditsAsync(ct);
        BeginExport();
    }, operationId, "retry-export", ct);

    private void BeginExport()
    {
        if (exporter is null) throw new InvalidOperationException("Verified content export is not available yet. Sources remain saved.");
        if (_handle is null || Current!.Clips.IsEmpty) throw new InvalidOperationException("There is no content to export.");
        if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new InvalidOperationException("Select an output directory before exporting.");
        _export?.Dispose();
        _export = new(exporter, _handle, Current!, OutputDirectory);
    }

    public Task<ProjectCommandResult> ExportAsync(string outputDirectory, long expectedRevision, Guid operationId,
        CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (_handle is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Pause or finish recording before exporting.");
        CheckRevision(expectedRevision);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Path.IsPathFullyQualified(outputDirectory))
            throw new InvalidDataException("Select an absolute output directory.");
        await FlushEditsAsync(ct);
        OutputDirectory = outputDirectory;
        BeginExport();
    }, operationId, "export:" + expectedRevision + ":" + outputDirectory, ct);

    public async Task<ProjectCommandResult> CancelExportAsync(Guid exportId, CancellationToken ct = default)
    {
        RecordingContentExportJob? job = null;
        var result = await Run(() =>
        {
            if (_export is null || _export.Status.ExportId != exportId)
                throw new InvalidOperationException("Export identity mismatch.");
            job = _export;
            if (!job.Completion.IsCompleted) job.Cancel();
            return Task.CompletedTask;
        }, null, "cancel-export", ct);
        if (result.Success) await job!.Completion.WaitAsync(ct);
        return result;
    }

    // IPC acknowledges the request immediately. Status remains Running until cleanup finishes.
    public Task<ProjectCommandResult> RequestExportCancellationAsync(Guid exportId, CancellationToken ct = default) => Run(() =>
    {
        if (_export is null || _export.Status.ExportId != exportId)
            throw new InvalidOperationException("Export identity mismatch.");
        if (!_export.Completion.IsCompleted) _export.Cancel();
        return Task.CompletedTask;
    }, null, "request-cancel-export", ct);

    private void RequireNoExport()
    {
        if (IsExporting) throw new InvalidOperationException("Wait for export to finish or cancel it before changing content.");
    }

    private async Task CommitSessionAsync(Guid operationId, CancellationToken ct)
    {
        try
        {
            await _committer.CommitAsync(_handle!, _session!, operationId, ct, (committed, inserted) =>
            {
                if (inserted) _history!.AcceptRecordingInsertion(committed);
                else _history!.AcceptRecordingAppend(committed);
            });
        }
        finally
        {
            // A later source/journal write may fail after earlier sources reached disk.
            // Never leave edit history behind the durable revision, including the retry path.
            _history!.AcceptRecordingAppend(_handle!.Current);
        }
    }

    public Task<ProjectCommandResult> RenameClipAsync(Guid clipId, string name, long expectedRevision, CancellationToken ct = default) =>
        EditAsync(expectedRevision, history => history.RenameClip(clipId, name), ct);

    public Task<ProjectCommandResult> RenameProjectAsync(string name, long expectedRevision, CancellationToken ct = default) =>
        EditAsync(expectedRevision, history => history.RenameProject(name.Trim()), ct);

    public Task<ProjectCommandResult> ApplyEditAsync(ProjectClipEdit edit, long expectedRevision, Guid operationId,
        CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (_history is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Finish or pause recording before editing.");
        CheckRevision(expectedRevision);
        _history.Apply(edit);
        await FlushEditsAsync(ct);
    }, operationId, "clip-edit:" + expectedRevision + ":" + JsonSerializer.Serialize<ProjectClipEdit>(edit), ct);

    public Task<ProjectCommandResult> UndoAsync(long expectedRevision, CancellationToken ct = default) =>
        EditAsync(expectedRevision, history => history.Undo(), ct);

    public Task<ProjectCommandResult> RedoAsync(long expectedRevision, CancellationToken ct = default) =>
        EditAsync(expectedRevision, history => history.Redo(), ct);

    private Task<ProjectCommandResult> EditAsync(long expectedRevision, Action<ProjectEditHistory> edit, CancellationToken ct) => Run(async () =>
    {
        RequireNoExport();
        if (_history is null || Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Finish or pause recording before editing.");
        CheckRevision(expectedRevision);
        edit(_history);
        await FlushEditsAsync(ct); // Auto-save starts immediately; errors retain both dirty state and edit history.
    }, null, "edit", ct);

    public Task<ProjectCommandResult> SaveAsync(long expectedRevision, CancellationToken ct = default) => Run(async () =>
    {
        CheckRevision(expectedRevision);
        await FlushEditsAsync(ct);
    }, null, "save", ct);

    private void CheckRevision(long revision)
    {
        if (_handle is null || Current!.Revision != revision) throw new InvalidOperationException("Stale project revision.");
    }

    private async Task FlushEditsAsync(CancellationToken ct)
    {
        if (_handle is null) throw new InvalidOperationException("No open project.");
        if (IsDirty) await _handle.SaveAsync(Current!, _handle.Current.Revision, ct);
    }

    public Task<ProjectCommandResult> CloseAsync(CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport();
        if (_session is not null || Mode is ProjectMode.Recording or ProjectMode.SavingSegment or ProjectMode.Paused or ProjectMode.SaveFailed)
            throw new InvalidOperationException("Finish and save recording before closing the project.");
        if (_handle is not null) { await FlushEditsAsync(ct); await _handle.DisposeAsync(); }
        _handle = null;
        _history = null;
        OutputDirectory = null;
        _export?.Dispose();
        _export = null;
        _operations.Clear();
        Mode = ProjectMode.Closed;
    }, null, "close", ct);

    private void RequireClosed()
    {
        if (_handle is not null || recorder.CurrentSession is not null)
            throw new InvalidOperationException("Close the current recording/project first.");
    }

    private async Task ValidateSourcesAsync(CancellationToken ct)
    {
        foreach (var item in _handle!.Current.Sources)
        {
            using var file = ProjectPathPolicy.OpenSource(_handle, item.RelativePath);
            if (file.Length != item.FileSize || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)),
                item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Project source changed: {item.RelativePath}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopPreview is not null) await _stopPreview();
        await _gate.WaitAsync();
        try
        {
            if (_export is not null)
            {
                _export.Cancel();
                await _export.Completion;
                _export.Dispose();
                _export = null;
            }
            if (_handle is not null) await _handle.DisposeAsync();
            _handle = null;
        }
        finally { _gate.Release(); }
    }
}
