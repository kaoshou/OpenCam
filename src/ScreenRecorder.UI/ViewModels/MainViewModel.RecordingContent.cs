// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorder.UI.Preflight;

namespace ScreenRecorder.UI.ViewModels;

public partial class MainViewModel
{
    private RecordingContentController? _content;
    private Func<bool>? _contentPermission;
    private Func<Task>? _ensureContentRecorder;
    private bool _openingContent;
    private Guid? _announcedExport;
    public bool UsesRecordingContent => _content is not null;
    public Func<string, Task<string?>>? RequestRecordingProjectName { get; set; }
    public Func<ScreenRecorder.Core.Models.RecordingConfiguration, bool, Task<ScreenRecorder.Core.Models.RecordingConfiguration?>>? RequestRecordingPreflight { get; set; }
    public Action<RecordingPreparationState>? RecordingPreparationChanged { get; set; }
    private bool _recordingPreparationActive;
    private RecordingPreparationState? _lastPreparation;

    private async Task<ScreenRecorder.Core.Models.RecordingConfiguration?> CheckBeforeRecordingAsync(
        ScreenRecorder.Core.Models.RecordingConfiguration configuration, bool resuming)
    {
        // Skip only the optional UI check. Permission, recorder ownership, output
        // validation and the actual start acknowledgement remain on the normal path.
        var shouldCheck = _recordingPreflightMode == ScreenRecorder.Core.Enums.RecordingPreflightMode.StartAndResume ||
            (_recordingPreflightMode == ScreenRecorder.Core.Enums.RecordingPreflightMode.StartOnly && !resuming);
        var accepted = !shouldCheck || configuration.AudioSource == ScreenRecorder.Core.Enums.AudioSourceType.None
            ? configuration : RequestRecordingPreflight is { } check ? await check(configuration, resuming) : null;
        if (accepted is not null)
        {
            _lastPreparation = null;
            ReportRecordingPreparation(RecordingPreparationState.Preparing);
        }
        return accepted;
    }
    private void ReportRecordingPreparation(RecordingPreparationState state)
    {
        _recordingPreparationActive = state is RecordingPreparationState.Preparing or RecordingPreparationState.Confirming or RecordingPreparationState.Unconfirmed;
        if (_lastPreparation == state) return;
        _lastPreparation = state;
        RecordingPreparationChanged?.Invoke(state);
    }
    private void ResolveContentPreparation()
    {
        if (!_recordingPreparationActive || _content is null) return;
        var vm = _content.Workspace;
        ReportRecordingPreparation(vm.StatusUnconfirmed ? RecordingPreparationState.Unconfirmed :
            vm.State.Mode == ProjectMode.Recording ? RecordingPreparationState.Started : RecordingPreparationState.Failed);
    }
    internal Task<ScreenRecorder.Infrastructure.IPC.IpcResponse> SendPreflightCommandAsync(string command,
        ScreenRecorder.Infrastructure.IPC.PreflightRequest request, CancellationToken ct) =>
        _ipcClient.SendCommandAsync(command, request, cancellationToken: ct);
    internal Task<ScreenRecorder.Infrastructure.IPC.ProjectReply> ReadPreflightFrameAsync(
        ScreenRecorder.Infrastructure.IPC.PreflightRequest request, CancellationToken ct) => _ipcClient.SendPreflightFrameAsync(request, ct);
    public Func<Task<UnsavedDecision>>? RequestUnsavedProjectDecision { get; set; }
    public Func<Task<SubtitleExportDecision>>? RequestSubtitleExportDecision { get; set; }
    public Func<Task<bool?>>? RequestProjectDraftRecovery { get; set; }
    public Func<string, Task<ExistingExportDecision>>? RequestExistingProjectExport { get; set; }
    public Action<string>? OpenExistingProjectExport { get; set; }
    private bool _newProjectAutoExport = true;
    public bool AutoExportRecordingOnStop => HasRecordingProject ? _content!.Workspace.State.AutoExportOnStop : _newProjectAutoExport;
    public bool CanChangeProjectExportPolicy => !IsProjectWorkspaceOpen && !IsPreparing && !IsRecovering &&
        (HasRecordingProject ? _content?.CanResume == true : _content?.CanStartNew == true);
    public bool CanExportRecordingContent => !IsProjectWorkspaceOpen && _content?.Workspace.CanExport == true;
    public async Task SetProjectAutoExportAsync(bool value)
    {
        if (!CanChangeProjectExportPolicy) return;
        if (HasRecordingProject) await _content!.Workspace.SetAutoExportOnStopAsync(value);
        else _newProjectAutoExport = value;
        OnPropertyChanged(nameof(AutoExportRecordingOnStop));
    }
    public Func<string, Task<string?>>? RequestMp4Destination { get; set; }
    public Func<ProjectWorkspaceViewModel, ProjectExportFormat, Task>? RequestMediaExport { get; set; }
    public Func<Task<string?>>? RequestWebPlayerDestination { get; set; }
    public Func<ProjectWorkspaceViewModel, Task>? RequestWebPlayerExport { get; set; }
    [RelayCommand] public async Task ExportWebPlayerAsync()
    {
        if (CanExportRecordingContent && RequestWebPlayerExport is { } export)
        { await export(_content!.Workspace); return; }
        if (!CanExportRecordingContent || RequestWebPlayerDestination is null) return;
        var path = await RequestWebPlayerDestination();
        if (path is not null && CanExportRecordingContent) await _content!.Workspace.ExportWebPlayerAsync(path);
    }
    [RelayCommand] public async Task ExportRecordingContentAsync()
    {
        if (CanExportRecordingContent && RequestMediaExport is { } export)
        { await export(_content!.Workspace, ProjectExportFormat.Mp4); return; }
        if (!CanExportRecordingContent || RequestMp4Destination is null) return;
        var path = await RequestMp4Destination(_content!.Workspace.State.Name);
        if (path is not null && CanExportRecordingContent) await _content.Workspace.ExportFileAsync(path);
    }
    [RelayCommand] public Task ExportGifAsync() => CanExportRecordingContent && RequestMediaExport is { } export
        ? export(_content!.Workspace, ProjectExportFormat.Gif) : Task.CompletedTask;
    public bool HasRecordingProject => _content?.Workspace.State.ProjectId is not null;
    public Task<bool> CloseRecordingProjectAsync() => _content?.Workspace.CloseAsync() ?? Task.FromResult(true);
    public bool CanManageRecordingProject => !IsProjectWorkspaceOpen && !IsPreparing && !IsRecovering && _content?.CanReplaceProject == true;
    public string CurrentRecordingProjectName => _content?.Workspace.State.ProjectId is null
        ? Strings["ProjectModeClosed"] : _content.Workspace.State.Name;
    public string CurrentRecordingProjectDetails => _content?.Workspace.State.ProjectId is null ? "" :
        $"{Strings["ProjectClips"]} {_content.Workspace.State.ClipCount}";
    public string? CurrentRecordingProjectDirectory => _content?.Workspace.State.Directory;
    public bool IsContentExporting => _content?.Workspace.IsExporting == true;
    public bool CanCancelContentExport => _content?.Workspace.CanCancelExport == true;
    internal bool ForceQuitAuthorized => _forceQuitAuthorized;

    [RelayCommand(CanExecute = nameof(CanCancelContentExport))]
    public Task CancelContentExportAsync() => _content?.Workspace.CancelExportAsync() ?? Task.CompletedTask;

    internal void NoteContentConnectionFailure()
    {
        _telemetryFailures++;
        OnPropertyChanged(nameof(CanForceQuitUnconfirmed));
    }
    public bool CanOpenContentEditor => !IsRecovering && !IsPreparing &&
        (_content is not null ? _content.Workspace.State.ProjectId is not null && _content.CanOpenEditor : (!IsRecording && !IsPaused));

    private async Task<string?> AskProjectNameAsync()
    {
        if (RequestRecordingProjectName is null) return null; // Never start capture without confirmation.
        var name = await RequestRecordingProjectName(Strings["ProjectDefaultName"] + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"));
        if (name is null) return null;
        name = name.Trim();
        ProjectValidation.ValidateName(name);
        return name;
    }

    [RelayCommand]
    public async Task NewRecordingProjectAsync()
    {
        if (!CanManageRecordingProject || _content is null) return;
        var autoExport = AutoExportRecordingOnStop;
        _openingContent = true; SyncRecordingContent();
        try
        {
            var parent = OutputDirectory;
            var name = await AskProjectNameAsync();
            if (name is null) return;
            await _ensureContentRecorder!();
            await _content.ReplaceProjectAsync(parent, name, autoExport);
        }
        catch (Exception ex) { _content.Workspace.Error = ex.Message; }
        finally { _openingContent = false; SyncRecordingContent(); }
    }

    public async Task OpenRecordingProjectAsync(string path)
    {
        if (!CanManageRecordingProject || _content is null) return;
        _openingContent = true; SyncRecordingContent();
        try
        {
            await _ensureContentRecorder!();
            await _content.ReplaceProjectAsync(path);
        }
        catch (Exception ex) { _content.Workspace.Error = ex.Message; }
        finally { _openingContent = false; SyncRecordingContent(); }
    }

    internal void ConfigureRecordingContent(IProjectClient client, Func<bool> permission, Func<Task> ensureRecorder)
    {
        if (_content is not null) throw new InvalidOperationException("Recording content already configured.");
        _contentPermission = permission;
        _ensureContentRecorder = ensureRecorder;
        _content = new(client, async ct =>
        {
            if (!await _content!.Workspace.StopPreviewAsync().WaitAsync(ct))
                throw new IOException(_content.Workspace.Error ?? "Preview shutdown is unconfirmed.");
        });
        _content.Workspace.RequestUnsavedDecision = () => RequestUnsavedProjectDecision?.Invoke() ?? Task.FromResult(UnsavedDecision.Cancel);
        _content.Workspace.LoadWebPlayerLanguages = async () => (await _settingsService.LoadSettingsAsync()).WebPlayerLanguages;
        _content.Workspace.RequestSubtitleExportDecision = () => RequestSubtitleExportDecision?.Invoke() ?? Task.FromResult(SubtitleExportDecision.Cancel);
        _content.Workspace.RequestDraftRecovery = () => RequestProjectDraftRecovery?.Invoke() ?? Task.FromResult<bool?>(null);
        _content.Workspace.RequestExistingExport = path => RequestExistingProjectExport?.Invoke(path) ?? Task.FromResult(ExistingExportDecision.Cancel);
        _content.Workspace.OpenExistingExport = path => OpenExistingProjectExport?.Invoke(path);
        _content.PropertyChanged += (_, _) => SyncRecordingContent();
        SyncRecordingContent();
    }

    public string TotalRecordingTimeText
    {
        get
        {
            var time = TimeSpan.FromTicks(Math.Max(0, _content?.Workspace.State.TotalRecordingTicks ?? 0));
            return $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }
    }

    private void SyncRecordingContent()
    {
        if (_content is null) return;
        var vm = _content.Workspace;
        if (!_openingContent && !vm.IsBusy && !_content.IsBusy) ResolveContentPreparation();
        var sessionTime = TimeSpan.FromTicks(Math.Max(0, vm.State.SessionRecordingTicks));
        ElapsedTimeText = $"{(long)sessionTime.TotalHours:00}:{sessionTime.Minutes:00}:{sessionTime.Seconds:00}";
        OnPropertyChanged(nameof(TotalRecordingTimeText));
        OnPropertyChanged(nameof(AutoExportRecordingOnStop));
        OnPropertyChanged(nameof(CanExportRecordingContent));
        OnPropertyChanged(nameof(CanAddBookmark));
        IsPreparing = _openingContent || _content.IsBusy || vm.IsBusy || vm.StatusUnconfirmed || vm.IsExporting || vm.State.Mode == ProjectMode.SavingSegment;
        IsRecording = vm.State.Mode == ProjectMode.Recording;
        IsPaused = vm.State.Mode is ProjectMode.Paused or ProjectMode.SaveFailed;
        OnPropertyChanged(nameof(PreparationBadgeText));
        if (!IsRecording && vm.State.RecordedSourceBytes is >= 0)
            FileSizeText = $"{vm.State.RecordedSourceBytes.Value / (1024.0 * 1024.0):F1} MB";
        OnPropertyChanged(nameof(CanChangeProjectExportPolicy));
        if (vm.Error is not null) StatusMessage = vm.Error;
        else if (vm.State.Export is not null) StatusMessage = vm.ExportStatusText;
        else if (vm.State.Mode == ProjectMode.Ready && vm.State.ClipCount > 0 && !vm.State.AutoExportOnStop) StatusMessage = Strings["ProjectSavedWithoutExport"];
        else if (IsPreparing) StatusMessage = PreparationBadgeText;
        else if (IsRecording) StatusMessage = Strings["StatusRecordingActive"];
        else if (IsPaused) StatusMessage = Strings["StatusPausedMsg"];
        else StatusMessage = Strings["StatusReady"];
        if (vm.State.Export is { State: RecordingExportState.Succeeded, FinalPath: not null } export && _announcedExport != export.ExportId)
        {
            _announcedExport = export.ExportId;
            LastOutputFilePath = export.FinalPath;
            RequestRestoreWindow?.Invoke(this, EventArgs.Empty);
            if (_openFolderOnFinished) OpenCompletedOutputFolder();
        }
        if (!_forScreenshot)
        {
            // Keep reconciling export / unknown status even when the editor is closed.
            if (vm.State.ProjectId is not null || vm.StatusUnconfirmed) _telemetryTimer.Start();
            if (IsRecording && !_audioMeterTimer.IsEnabled) StartAudioMeterPolling();
            else if (IsPaused) SetMeterStatesForPausedSelection();
            else if (!IsRecording && _audioMeterTimer.IsEnabled) StopAudioMeterPolling();
        }
        if (vm.State.ClipCount > 0 && !vm.State.IsDirty && vm.State.Directory is not null)
        {
            var path = Path.Combine(vm.State.Directory, "project.opencam");
            if (!RecentProjectPaths.Contains(path))
            {
                RecentProjectPaths.Insert(0, path);
                RecentProjectPaths = RecentProjectPaths.Take(20).ToList();
                PersistUserSettings();
            }
        }
        foreach (var name in new[] { nameof(CanOpenContentEditor), nameof(CanStartRecording), nameof(CanStopRecording), nameof(CanPauseOrResume) })
            OnPropertyChanged(name);
        OnPropertyChanged(nameof(IsContentExporting));
        OnPropertyChanged(nameof(CanCancelContentExport));
        foreach (var name in new[] { nameof(CanManageRecordingProject), nameof(CurrentRecordingProjectName),
            nameof(CurrentRecordingProjectDetails), nameof(CurrentRecordingProjectDirectory) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(CanForceQuitUnconfirmed));
        CancelContentExportCommand.NotifyCanExecuteChanged();
    }

    public string PreparationBadgeText => Strings[
        _content?.Workspace.IsExporting == true ? "ProjectExportRunning" :
        _content?.Operation is RecordingContentOperation.Pausing or RecordingContentOperation.Stopping ||
            _content?.Workspace.State.Mode == ProjectMode.SavingSegment ? "ProjectModeSavingSegment" :
        _openingContent || _content?.Operation == RecordingContentOperation.Starting ? "RecordingPreparing" : "ProjectSaving"];

    private async Task CaptureContentAsync(bool newContent, long? insertionTicks = null, long? revision = null)
    {
        if (_content is null || _openingContent || IsProjectWorkspaceOpen) return;
        var vm = _content.Workspace;
        if (!(newContent ? _content.CanStartNew : _content.CanResume)) return;
        var configuration = BuildProjectConfiguration(); // Freeze before any asynchronous work.
        var autoExport = AutoExportRecordingOnStop;
        _openingContent = true;
        SyncRecordingContent();
        try
        {
            if (newContent)
            {
                var name = await AskProjectNameAsync();
                if (name is null) return;
                configuration.ProjectName = name;
            }
            if (!_contentPermission!())
            {
                vm.Error = Strings["StatusScreenPermissionRequired"];
                return;
            }
            await _ensureContentRecorder!();
            if (!await vm.StopPreviewAsync() || !await vm.ResolveUnsavedAsync()) return;
            var checkedConfiguration = await CheckBeforeRecordingAsync(configuration,
                !newContent && insertionTicks is null && vm.State.Mode == ProjectMode.Paused);
            if (checkedConfiguration is null) return;
            configuration = checkedConfiguration;
            ApplyPreflightAudioSelection(configuration);
            _activeDiskWarningThresholdBytes = configuration.DiskWarningThresholdBytes;
            if (insertionTicks is { } ticks)
                await vm.StartInsertionAsync(configuration, ticks, revision!.Value);
            else if (newContent) await _content.StartNewAsync(configuration, autoExport);
            else await _content.ResumeAsync(configuration);
            if (vm.State.Mode == ProjectMode.Recording && MinimizeOnRecord)
                RequestMinimizeWindow?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { vm.Error = ex.Message; }
        finally { _openingContent = false; ResolveContentPreparation(); SyncRecordingContent(); }
    }

    public Task StartEditorRecordingAsync(long? insertionTicks = null, long? revision = null) =>
        CaptureContentAsync(false, insertionTicks, revision);

    private void ApplyPreflightAudioSelection(ScreenRecorder.Core.Models.RecordingConfiguration configuration)
    {
        RecordMicrophone = configuration.AudioSource is ScreenRecorder.Core.Enums.AudioSourceType.MicrophoneOnly or ScreenRecorder.Core.Enums.AudioSourceType.SystemAndMicrophone;
        RecordSystemAudio = configuration.AudioSource is ScreenRecorder.Core.Enums.AudioSourceType.SystemOnly or ScreenRecorder.Core.Enums.AudioSourceType.SystemAndMicrophone;
    }

    public async Task<bool> LeaveContentEditorAsync()
    {
        if (_content is null) return false;
        var vm = _content.Workspace;
        if (vm.IsBusy || vm.StatusUnconfirmed || vm.IsExporting || vm.State.Mode == ProjectMode.Recording) return false;
        if (!await vm.StopPreviewAsync()) return false;
        if (vm.State.Mode == ProjectMode.Interrupted) return true; // Preserve recovery bytes until explicitly confirmed.
        if (vm.State.ProjectId is null) return true;
        // Returning to home keeps the same workspace and its unsaved edits alive.
        return true;
    }
}
