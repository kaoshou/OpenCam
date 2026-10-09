// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Projects;

public enum UnsavedDecision { Cancel, Save, Discard }
public enum ExistingExportDecision { Cancel, OpenExisting, ExportAgain }

public sealed partial class ProjectWorkspaceViewModel
{
    public Func<Task<UnsavedDecision>>? RequestUnsavedDecision { get; set; }
    public Func<Task<bool?>>? RequestDraftRecovery { get; set; }
    public Func<string, Task<ExistingExportDecision>>? RequestExistingExport { get; set; }
    public Action<string>? OpenExistingExport { get; set; }
    public Task SetAutoExportOnStopAsync(bool enabled) => ExecuteAsync("SetProjectAutoExport", new() { AutoExportOnStop = enabled });
    private bool _resolvingSave;
    private CancellationTokenSource? _exportLookup;
    public bool CanCancelExport => IsExporting || _exportLookup is not null;

    private async Task<bool> ShouldExportAgainAsync(string directory)
    {
        var revision = State.Revision;
        var projectId = State.ProjectId;
        using var cancellation = new CancellationTokenSource();
        _exportLookup = cancellation;
        IsBusy = true;
        try
        {
            var reply = await client.SendAsync("FindProjectExport", new() { ProjectId = projectId, ExpectedRevision = revision, Path = directory }, cancellation.Token);
            if (cancellation.IsCancellationRequested) return false;
            ApplyReply(reply);
            if (!reply.Success || reply.Unconfirmed) return false;
            if (reply.ExistingOutputPath is not { } path) return true;
            var choice = RequestExistingExport is null ? ExistingExportDecision.Cancel : await RequestExistingExport(path);
            if (State.ProjectId != projectId || State.Revision != revision) return false;
            if (choice == ExistingExportDecision.OpenExisting) OpenExistingExport?.Invoke(path);
            return choice == ExistingExportDecision.ExportAgain;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally { _exportLookup = null; IsBusy = false; }
    }

    public async Task<bool> ResolveRecoveredDraftAsync()
    {
        if (!State.HasRecoverableDraft) return true;
        var choice = RequestDraftRecovery is null ? null : await RequestDraftRecovery();
        if (choice is null) return false;
        await ExecuteAsync("ResolveProjectDraft", new() { RestoreDraft = choice.Value });
        return !StatusUnconfirmed && !State.HasRecoverableDraft && Error is null;
    }

    public async Task<bool> ResolveUnsavedAsync(CancellationToken ct = default)
    {
        if (_resolvingSave || StatusUnconfirmed) return false;
        _resolvingSave = true;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!await ResolveRecoveredDraftAsync()) return false;
            if (!State.IsDirty && !HasPropertyDraft) { Error = null; return true; }
            var decision = RequestUnsavedDecision is null ? UnsavedDecision.Cancel : await RequestUnsavedDecision();
            ct.ThrowIfCancellationRequested();
            if (decision == UnsavedDecision.Cancel) return false;
            if (decision == UnsavedDecision.Save) await SaveAsync();
            else
            {
                await ExecuteAsync("DiscardProjectEdits");
                if (!StatusUnconfirmed && Error is null) ResetProperties();
            }
            return !StatusUnconfirmed && Error is null && !State.IsDirty && !HasPropertyDraft;
        }
        finally { _resolvingSave = false; }
    }
}
