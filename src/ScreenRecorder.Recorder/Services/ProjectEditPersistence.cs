// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public sealed partial class ProjectRecordingCoordinator
{
    private CancellationTokenSource? _draftWrite;
    private Guid? _draftId;
    private long _draftSequence;
    private ProjectEditDraft? _recoveredDraft;
    public bool HasRecoverableDraft => _recoveredDraft is not null;
    public string? DraftError { get; private set; }

    private async Task LoadDraftAsync(CancellationToken ct)
    {
        try { _recoveredDraft = await _handle!.ReadDraftAsync(ct); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { DraftError = ex.Message; Log.Warning(ex, "Project edit draft could not be loaded"); }
    }

    private void CancelDraftWrite()
    {
        _draftWrite?.Cancel();
        _draftWrite?.Dispose();
        _draftWrite = null;
    }

    private void ScheduleDraft()
    {
        CancelDraftWrite();
        _draftWrite = new();
        var token = _draftWrite.Token;
        _ = PersistDraftAfterDelayAsync(token);
    }

    private async Task PersistDraftAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            await _gate.WaitAsync(ct);
            try
            {
                if (_handle is null || Current is null || HasRecoverableDraft) return;
                if (!IsDirty)
                {
                    if (_draftId is Guid old) await _handle.DiscardDraftAsync(old, ct);
                    _draftId = null;
                    return;
                }
                await PersistCurrentDraftAsync(ct);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { DraftError = ex.Message; Log.Warning(ex, "Project edit recovery draft save failed"); }
    }

    // Caller owns the coordinator gate. Policy changes must acknowledge the replacement
    // draft before returning, rather than cancelling the only recovery copy on disposal.
    private async Task PersistCurrentDraftAsync(CancellationToken ct)
    {
        var id = _draftId ?? Guid.NewGuid();
        var draft = new ProjectEditDraft(id, Current!.ProjectId, _handle!.Current.Revision,
            checked(++_draftSequence), Current.Name, Current.Clips);
        await _handle.SaveDraftAsync(draft, ct);
        _draftId = id;
        DraftError = null;
    }

    private async Task EnsureSavedForOperationAsync(CancellationToken ct)
    {
        if (IsDirty || HasRecoverableDraft) throw new InvalidOperationException("Save or discard pending edits before continuing.");
        // Undo may return to saved content with a newer IPC revision. Synchronize metadata only.
        await FlushEditsAsync(ct);
    }

    public Task<ProjectCommandResult> DiscardEditsAsync(long expectedRevision, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport(); CheckEditablePersistence(expectedRevision);
        CancelDraftWrite();
        if (_draftId is Guid id) await _handle!.DiscardDraftAsync(id, ct);
        _history!.ReplaceEdits(new ProjectEditSaveState(_handle!.Current).DiscardEdits(Current!), false);
        _draftId = null; DraftError = null;
    }, null, "discard-edits", ct);

    public Task<ProjectCommandResult> ResolveDraftAsync(bool restore, long expectedRevision, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport(); CheckEditablePersistence(expectedRevision);
        var draft = _recoveredDraft ?? throw new InvalidOperationException("No recovered draft is pending.");
        if (restore)
        {
            _history!.ReplaceEdits(Current! with { Name = draft.Name, Clips = draft.Clips,
                Revision = checked(Current!.Revision + 1) }, true);
            _draftId = draft.Id; _draftSequence = draft.Sequence;
        }
        else await _handle!.DiscardDraftAsync(draft.Id, ct);
        _recoveredDraft = null; DraftError = null;
    }, null, "resolve-draft:" + restore, ct);

    public Task<ProjectCommandResult> SetAutoExportOnStopAsync(bool enabled, long expectedRevision, CancellationToken ct = default) => Run(async () =>
    {
        RequireNoExport(); CheckEditablePersistence(expectedRevision);
        if (HasRecoverableDraft) throw new InvalidOperationException("Resolve the recovered draft first.");
        CancelDraftWrite();
        var priorDraft = await _handle!.ReadDraftAsync(ct);
        var saved = _handle!.Current with { AutoExportOnStop = enabled, Revision = checked(Current!.Revision + 1),
            RecoveryDraftBaseRevision = priorDraft?.BaseRevision };
        await _handle.SaveAsync(saved, _handle.Current.Revision, ct);
        _history!.AcceptSavedMetadata(Current with { AutoExportOnStop = enabled, Revision = saved.Revision,
            RecoveryDraftBaseRevision = saved.RecoveryDraftBaseRevision });
        if (IsDirty)
        {
            try { await PersistCurrentDraftAsync(ct); }
            catch (Exception ex)
            {
                DraftError = ex.Message;
                throw;
            }
        }
    }, null, "auto-export:" + enabled, ct);

    private void CheckEditablePersistence(long revision)
    {
        CheckRevision(revision);
        if (Mode is not (ProjectMode.Ready or ProjectMode.Paused))
            throw new InvalidOperationException("Project is not editable.");
    }

    public async Task<string?> FindExistingExportAsync(string directory, long revision, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireNoExport(); CheckEditablePersistence(revision);
            if (IsDirty || HasRecoverableDraft) throw new InvalidOperationException("Resolve unsaved edits first.");
            // Best-effort reuse must not monopolize the command channel for a large file.
            // Timeout means export anew, never trust a partially verified receipt.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                return (await ScreenRecorder.Infrastructure.Projects.ProjectExportReceiptStore.FindVerifiedAsync(
                    _handle!, Current!, directory, deadline.Token))?.Path;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }
        finally { _gate.Release(); }
    }
}
