// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

public sealed partial class ProjectWorkspaceViewModel(IProjectClient client) : ObservableObject
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid _pendingOperation;
    private (string Command, ProjectRequest Request)? _pendingRequest;
    public LanguageManager Strings => LanguageManager.Instance;
    [ObservableProperty] private ProjectSnapshot _state = ProjectSnapshot.Closed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _statusUnconfirmed;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private ProjectClip? _selectedClip;
    [ObservableProperty] private string _clipName = "";
    public ObservableCollection<ProjectClip> Clips { get; } = [];
    public bool IsExporting => State.Export?.State == RecordingExportState.Running;
    public bool CanExport => CanEdit && State.ClipCount > 0;
    public double ExportProgress => (State.Export?.Progress ?? 0) * 100;
    public bool HasEditorError => !string.IsNullOrEmpty(Error);
    public string ExportStatusText => State.Export is not { } export ? "" : export.State switch
    {
        RecordingExportState.Running => Strings["ProjectExportRunning"] + $" {ExportProgress:0}%",
        RecordingExportState.Succeeded => Strings["ProjectExportSucceeded"] + " " + export.FinalPath,
        RecordingExportState.Canceled => Strings["ProjectExportCanceled"],
        _ => Strings["ProjectExportFailed"] + " " + export.Error
    };
    public bool CanEdit => !IsBusy && !IsChangingPreviewQuality && !StatusUnconfirmed && !IsExporting && !State.HasRecoverableDraft && State.Mode is ProjectMode.Ready or ProjectMode.Paused;
    public bool CanEditTimeline => CanEdit && !HasPropertyDraft;
    public bool CanReplaceProject => !IsBusy && !HasPropertyDraft;
    public bool CanReturnToRecording => !IsBusy && !StatusUnconfirmed && State.Mode is not (ProjectMode.Recording or ProjectMode.SavingSegment);
    public bool CanRecord => CanEditTimeline && State.ProjectId is not null;
    public bool CanPause => !IsBusy && !StatusUnconfirmed && State.Mode == ProjectMode.Recording;
    public bool CanFinish => !HasPropertyDraft && !IsBusy && !StatusUnconfirmed && State.Mode is ProjectMode.Recording or ProjectMode.Paused or ProjectMode.SaveFailed;
    public bool CanClose => !IsBusy && !StatusUnconfirmed && !IsExporting && State.Mode is ProjectMode.Ready or ProjectMode.Interrupted or ProjectMode.Closed;
    public bool CanUndo => CanEditTimeline && State.CanUndo;
    public bool CanRedo => CanEditTimeline && State.CanRedo;
    public bool CanEditSelection => CanEdit && SelectedClip is not null && Clips.Contains(SelectedClip);
    public bool CanMoveEarlier => CanEditTimeline && CanEditSelection && GroupStart(Clips.IndexOf(SelectedClip!)) > 0;
    public bool CanMoveLater => CanEditTimeline && CanEditSelection && GroupEnd(Clips.IndexOf(SelectedClip!)) < Clips.Count - 1;
    public string SaveStatus => State.ProjectId is null ? "" : Strings[StatusUnconfirmed ? "ProjectUnconfirmed" : IsBusy ? "ProjectSaving" :
        Error is not null ? "ProjectSaveFailed" : State.DraftError is not null ? "ProjectDraftSaveFailed" : State.IsDirty || HasPropertyDraft ? "ProjectUnsaved" : "ProjectSaved"];
    public string ModeText => Strings["ProjectMode" + State.Mode];
    public string StartText => Strings[State.Mode == ProjectMode.Paused ? "ProjectContinue" : "ProjectRecord"];
    public string ClipCountText => $"{Clips.Count} / {State.ClipCount}";
    public event EventHandler? StateChanged;

    partial void OnSelectedClipChanged(ProjectClip? value) { ResetProperties(); NotifyState(); }
    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnErrorChanged(string? value) => NotifyState();
    partial void OnStatusUnconfirmedChanged(bool value) => NotifyState();
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(CanEdit), nameof(CanRecord), nameof(CanPause), nameof(CanFinish), nameof(CanClose),
            nameof(CanUndo), nameof(CanRedo), nameof(SaveStatus), nameof(ModeText), nameof(StartText), nameof(ClipCountText), nameof(IsExporting),
            nameof(CanExport), nameof(CanCancelExport), nameof(ExportProgress), nameof(ExportStatusText), nameof(HasEditorError), nameof(CanReturnToRecording),
            nameof(CanEditSelection), nameof(CanMoveEarlier), nameof(CanMoveLater), nameof(CanEditTimeline), nameof(CanReplaceProject),
            nameof(CanChangePreviewOptions), nameof(CanUseOriginalPreview) })
            OnPropertyChanged(property);
        StateChanged?.Invoke(this, EventArgs.Empty);
        NotifyPlayback();
        NotifyTimeline();
    }

    public void ApplyReply(ProjectReply reply)
    {
        if (reply.Unconfirmed) { StatusUnconfirmed = true; Error = reply.Error; return; }
        if (reply.State.ProjectId == State.ProjectId && reply.State.Revision < State.Revision) return;
        if (reply.State.ProjectId != State.ProjectId || reply.State.Revision != State.Revision ||
            reply.State.ServerInstanceId != State.ServerInstanceId) { ClearWaveforms(); ClearPreview(); ClearThumbnails(); }
        if (reply.State.Mode != State.Mode) ClearPreview();
        if (reply.State.ProjectId != State.ProjectId)
        {
            Clips.Clear();
            SelectedClip = null;
            PublishTimeline([]);
        }
        State = reply.State;
        StatusUnconfirmed = false;
        Error = reply.Success ? reply.State.LastError : reply.Error;
        NotifyState();
    }

    public Task CreateAsync(string parent, string name) => ExecuteAsync("CreateProject", new() { Path = parent, Name = name });
    public async Task OpenAsync(string path)
    {
        await ExecuteAsync("OpenProject", new() { Path = path });
        await ResolveRecoveredDraftAsync();
    }
    public Task StartAsync(RecordingConfiguration configuration) => ExecuteAsync("StartProjectRecording", new() { Configuration = configuration });
    public Task StartNewContentAsync(RecordingConfiguration configuration) => ExecuteAsync("StartNewRecordingContent", new() { Configuration = configuration });
    public Task FinishAndExportAsync() => ExecuteAsync("FinishRecordingContent");
    public async Task StartInsertionAsync(RecordingConfiguration configuration, long confirmedTicks, long confirmedRevision)
    {
        if (!CanRecord || State.Revision != confirmedRevision || confirmedTicks < 0 || confirmedTicks > DurationTicks)
        { Error = Strings["ProjectInsertionChanged"]; return; }
        if (!await ResolveUnsavedAsync()) return;
        if (!CanRecord || State.Revision != confirmedRevision || State.IsDirty || Error is not null) return;
        await ExecuteAsync("StartProjectInsertion", new() { Configuration = configuration, TimelineTicks = confirmedTicks });
    }
    [RelayCommand] public Task PauseAsync() => ExecuteAsync("PauseProjectRecording");
    [RelayCommand] public Task FinishAsync() => ExecuteAsync("FinishProjectRecording");
    public async Task ExportAsync(string directory)
    {
        if (!CanExport) return;
        if (!await ResolveUnsavedAsync()) return;
        if (!CanExport || HasPropertyDraft || State.IsDirty || Error is not null) return;
        if (!await ShouldExportAgainAsync(directory)) return;
        await ExecuteAsync("ExportRecordingContent", new() { Path = directory });
    }
    [RelayCommand] public Task CancelExportAsync()
    {
        if (_exportLookup is not null) { _exportLookup.Cancel(); return Task.CompletedTask; }
        return IsExporting ? ExecuteAsync("CancelRecordingContentExport", new() { ExportId = State.Export!.ExportId }) : Task.CompletedTask;
    }
    [RelayCommand] public async Task SaveAsync()
    {
        if (HasPropertyDraft)
        {
            await ApplyPropertiesAsync();
            if (HasPropertyDraft || StatusUnconfirmed) return;
        }
        await ExecuteAsync("SaveProject");
    }
    [RelayCommand] public Task UndoAsync() => ExecuteAsync("UndoProject");
    [RelayCommand] public Task RedoAsync() => ExecuteAsync("RedoProject");
    public Task RestoreBackupAsync() => ExecuteAsync("RestoreProjectBackup");
    [RelayCommand] public Task RenameSelectedAsync() => SelectedClip is null ? Task.CompletedTask : RenameAsync(SelectedClip.Id, ClipName);
    public Task RenameAsync(Guid clipId, string name) => ExecuteAsync("RenameProjectClip", new() { ClipId = clipId, Name = name });
    public Task RenameProjectAsync(string name) => ExecuteAsync("RenameProject", new() { Name = name });
    [RelayCommand] public Task DeleteSelectedAsync() => !CanEditTimeline || !CanEditSelection ? Task.CompletedTask :
        ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Remove(SelectedClip!.Id) });
    [RelayCommand] public Task MoveSelectedEarlierAsync()
    {
        if (!CanMoveEarlier) return Task.CompletedTask;
        var previous = GroupStart(GroupStart(Clips.IndexOf(SelectedClip!)) - 1);
        return ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Move(SelectedClip!.Id, Clips[previous].Id) });
    }
    [RelayCommand] public Task MoveSelectedLaterAsync()
    {
        if (!CanMoveLater) return Task.CompletedTask;
        var afterNext = GroupEnd(GroupEnd(Clips.IndexOf(SelectedClip!)) + 1) + 1;
        return ExecuteAsync("ApplyProjectEdit", new() { Edit = new ProjectClipEdit.Move(SelectedClip!.Id,
            afterNext < Clips.Count ? Clips[afterNext].Id : null) });
    }
    private int GroupStart(int index)
    {
        var group = Clips[index].GroupId;
        while (group is not null && index > 0 && Clips[index - 1].GroupId == group) index--;
        return index;
    }
    private int GroupEnd(int index)
    {
        var group = Clips[index].GroupId;
        while (group is not null && index + 1 < Clips.Count && Clips[index + 1].GroupId == group) index++;
        return index;
    }
    [RelayCommand] public Task RefreshAsync() => ExecuteAsync("GetProjectStatus", new() { OperationId = _pendingOperation });
    public async Task PollRecordingAsync()
    {
        if (StatusUnconfirmed || (!State.IsDirty && !IsExporting && State.Mode is not (ProjectMode.Recording or ProjectMode.SavingSegment)) || !await _gate.WaitAsync(0)) return;
        try
        {
            // Background status reads are not user operations. Keep the gate for
            // serialization, but do not pulse IsBusy (and the home startup banner)
            // on every telemetry tick. Real transitions and lost status still
            // update the UI through ApplyReply / StatusUnconfirmed below.
            var previous = State;
            var reply = await client.SendAsync("GetProjectStatus", new());
            // A safety stop or newly saved segment needs a consistent clip list
            // before editing can be enabled. Lock before publishing that state.
            if (!reply.Unconfirmed && (reply.State.Mode != previous.Mode || reply.State.Revision != previous.Revision))
                IsBusy = true;
            ApplyReply(reply);
            if (!StatusUnconfirmed && (State.Mode != previous.Mode || State.Revision != previous.Revision))
                await LoadClipsAsync();
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; }
        finally { IsBusy = false; _gate.Release(); }
    }

    public async Task<bool> CloseAsync()
    {
        if ((IsPlayingPreview || IsChangingPreviewQuality) && !await StopPreviewAsync()) return false;
        if (CanClose && !await ResolveUnsavedAsync()) return false;
        await _gate.WaitAsync();
        try
        {
            if (StatusUnconfirmed || IsExporting || State.Mode is ProjectMode.Recording or ProjectMode.Paused or ProjectMode.SavingSegment or ProjectMode.SaveFailed)
            { Error = Strings["ProjectFinishBeforeClose"]; return false; }
            if (Error is not null && State.IsDirty) return false;
            if (State.ProjectId is null) return true;
            IsBusy = true;
            var reply = await SendAsync("CloseProject", new());
            ApplyReply(reply);
            return reply.Success && !reply.Unconfirmed;
        }
        finally { IsBusy = false; _gate.Release(); }
    }

    private async Task ExecuteAsync(string command, ProjectRequest? request = null)
    {
        if (command != "GetProjectStatus" && (IsPlayingPreview || IsChangingPreviewQuality) && !await StopPreviewAsync()) return;
        await _gate.WaitAsync();
        try
        {
            if (StatusUnconfirmed && command != "GetProjectStatus") return;
            IsBusy = true;
            var reply = await SendAsync(command, request ?? new());
            if (command == "GetProjectStatus" && _pendingOperation != Guid.Empty && !reply.OperationKnown)
            {
                // The server serializes status behind in-flight commands. A reachable
                // unknown operation may have been lost before dispatch; retry its exact ID/payload.
                if (!reply.Unconfirmed && _pendingRequest is { } pending)
                    reply = await client.SendAsync(pending.Command, pending.Request);
                else reply = reply with { Unconfirmed = true, Success = false };
            }
            ApplyReply(reply);
            if (!StatusUnconfirmed)
            {
                _pendingOperation = Guid.Empty;
                _pendingRequest = null;
                await LoadClipsAsync();
            }
        }
        catch (Exception ex) { Error = ex.Message; StatusUnconfirmed = true; }
        finally { IsBusy = false; _gate.Release(); }
    }

    private async Task LoadClipsAsync()
    {
        if (State.ProjectId is null) return;
        var snapshot = State;
        if (snapshot.ClipCount is < 0 or > 10000) throw new InvalidDataException(Strings["ProjectClipLoadFailed"]);
        var loaded = new List<ProjectClip>(snapshot.ClipCount);
        var spans = new List<ProjectTimelineClip>(snapshot.ClipCount);
        var missingTiming = false;
        var ids = new HashSet<Guid>();
        for (var offset = 0; offset < snapshot.ClipCount; offset += 100)
        {
            var page = await client.SendAsync("GetProjectClips", new() {
                ProjectId = snapshot.ProjectId, Offset = offset, Limit = 100 });
            if (!page.Success || page.Unconfirmed || page.Clips is null ||
                page.State.ProjectId != snapshot.ProjectId || page.State.Revision != snapshot.Revision ||
                page.State.ClipCount != snapshot.ClipCount || page.State.ServerInstanceId != snapshot.ServerInstanceId ||
                page.Clips.Length != Math.Min(100, snapshot.ClipCount - offset) ||
                page.Clips.Any(c => c.Id == Guid.Empty || !ids.Add(c.Id)))
                throw new InvalidDataException(Strings["ProjectClipLoadFailed"]);
            loaded.AddRange(page.Clips);
            if (page.TimelineClips is null) missingTiming = true;
            else
            {
                if (page.TimelineClips.Length != page.Clips.Length)
                    throw new InvalidDataException(Strings["ProjectClipLoadFailed"]);
                for (var i = 0; i < page.TimelineClips.Length; i++)
                {
                    var span = page.TimelineClips[i];
                    if (span.ClipId != page.Clips[i].Id || span.StartTicks != (spans.LastOrDefault()?.EndTicks ?? 0) ||
                        span.EndTicks <= span.StartTicks)
                        throw new InvalidDataException(Strings["ProjectClipLoadFailed"]);
                    spans.Add(span);
                }
            }
        }
        // Publish only a complete, same-revision snapshot. Failed refresh leaves the previous list intact.
        var selection = SelectedClip?.Id;
        Clips.Clear();
        foreach (var clip in loaded) Clips.Add(clip);
        SelectedClip = Clips.FirstOrDefault(c => c.Id == selection) ?? Clips.FirstOrDefault();
        PublishTimeline(missingTiming ? [] : spans.AsReadOnly());
        OnPropertyChanged(nameof(ClipCountText));
    }

    private async Task<ProjectReply> SendAsync(string command, ProjectRequest request)
    {
        var query = command == "GetProjectStatus";
        var actual = request with { ProjectId = State.ProjectId, ExpectedRevision = State.Revision,
            OperationId = query ? request.OperationId : Guid.NewGuid() };
        if (!query)
        {
            actual = System.Text.Json.JsonSerializer.Deserialize<ProjectRequest>(System.Text.Json.JsonSerializer.Serialize(actual))!;
            _pendingOperation = actual.OperationId;
            _pendingRequest = (command, actual);
        }
        return await client.SendAsync(command, actual);
    }
}
