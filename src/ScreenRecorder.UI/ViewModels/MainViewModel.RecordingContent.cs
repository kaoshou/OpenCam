// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects;
using CommunityToolkit.Mvvm.Input;

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
    public Func<Task<UnsavedDecision>>? RequestUnsavedProjectDecision { get; set; }
    public Func<Task<bool?>>? RequestProjectDraftRecovery { get; set; }
    public Func<string, Task<ExistingExportDecision>>? RequestExistingProjectExport { get; set; }
    public Action<string>? OpenExistingProjectExport { get; set; }
    public bool AutoExportRecordingOnStop => _content?.Workspace.State.AutoExportOnStop ?? true;
    public bool CanChangeProjectExportPolicy => CanManageRecordingProject && HasRecordingProject;
    public bool CanExportRecordingContent => _content?.Workspace.CanExport == true;
    public Task SetProjectAutoExportAsync(bool value) => _content?.Workspace.SetAutoExportOnStopAsync(value) ?? Task.CompletedTask;
    [RelayCommand] public Task ExportRecordingContentAsync() => _content?.Workspace.ExportAsync(OutputDirectory) ?? Task.CompletedTask;
    public bool HasRecordingProject => _content?.Workspace.State.ProjectId is not null;
    public Task<bool> CloseRecordingProjectAsync() => _content?.Workspace.CloseAsync() ?? Task.FromResult(true);
    public bool CanManageRecordingProject => !IsPreparing && !IsRecovering && _content?.CanReplaceProject == true;
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
        _openingContent = true; SyncRecordingContent();
        try
        {
            var parent = OutputDirectory;
            var name = await AskProjectNameAsync();
            if (name is null) return;
            await _ensureContentRecorder!();
            await _content.ReplaceProjectAsync(parent, name);
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
        _content.Workspace.RequestDraftRecovery = () => RequestProjectDraftRecovery?.Invoke() ?? Task.FromResult<bool?>(null);
        _content.Workspace.RequestExistingExport = path => RequestExistingProjectExport?.Invoke(path) ?? Task.FromResult(ExistingExportDecision.Cancel);
        _content.Workspace.OpenExistingExport = path => OpenExistingProjectExport?.Invoke(path);
        _content.PropertyChanged += (_, _) => SyncRecordingContent();
        SyncRecordingContent();
    }

    private void SyncRecordingContent()
    {
        if (_content is null) return;
        var vm = _content.Workspace;
        OnPropertyChanged(nameof(AutoExportRecordingOnStop));
        OnPropertyChanged(nameof(CanChangeProjectExportPolicy));
        OnPropertyChanged(nameof(CanExportRecordingContent));
        IsPreparing = _openingContent || _content.IsBusy || vm.IsBusy || vm.StatusUnconfirmed || vm.IsExporting || vm.State.Mode == ProjectMode.SavingSegment;
        IsRecording = vm.State.Mode == ProjectMode.Recording;
        IsPaused = vm.State.Mode is ProjectMode.Paused or ProjectMode.SaveFailed;
        if (vm.Error is not null) StatusMessage = vm.Error;
        else if (vm.State.Export is not null) StatusMessage = vm.ExportStatusText;
        else if (vm.State.Mode == ProjectMode.Ready && vm.State.ClipCount > 0 && !vm.State.AutoExportOnStop) StatusMessage = Strings["ProjectSavedWithoutExport"];
        else if (IsPreparing) StatusMessage = Strings["StatusInitializing"];
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

    private async Task CaptureContentAsync(bool newContent, long? insertionTicks = null, long? revision = null)
    {
        if (_content is null || _openingContent) return;
        var vm = _content.Workspace;
        if (!(newContent ? _content.CanStartNew : _content.CanResume)) return;
        var configuration = BuildProjectConfiguration(); // Freeze before any asynchronous work.
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
            _activeDiskWarningThresholdBytes = configuration.DiskWarningThresholdBytes;
            if (insertionTicks is { } ticks)
                await vm.StartInsertionAsync(configuration, ticks, revision!.Value);
            else if (newContent) await _content.StartNewAsync(configuration);
            else await _content.ResumeAsync(configuration);
            if (vm.State.Mode == ProjectMode.Recording && MinimizeOnRecord)
                RequestMinimizeWindow?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { vm.Error = ex.Message; }
        finally { _openingContent = false; SyncRecordingContent(); }
    }

    public Task StartEditorRecordingAsync(long? insertionTicks = null, long? revision = null) =>
        CaptureContentAsync(false, insertionTicks, revision);

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
