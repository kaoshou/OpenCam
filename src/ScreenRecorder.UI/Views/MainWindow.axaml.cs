// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow : Window
{
    private ScreenRecorder.UI.Projects.ProjectWorkspaceView? _projectWindow;
    private RecordingCloseWarningWindow? _closeWarningWindow;
    private readonly DisplayIdentificationController _displayIdentification =
        new(new AvaloniaDisplayBadgePresenter(), new AvaloniaDisplayIdentificationTimer());
    private MainViewModel? _identificationViewModel;
    private bool _projectCloseAuthorized, _closingProject;
    private bool _settingsCloseAuthorized, _closingSettings;
    private bool _initialSizeApplied;
    private ScreenRecorder.Infrastructure.IPC.PreflightRequest? _lastPreflight;
    private ScreenRecorder.UI.Preflight.RecordingPreflightWindow? _preparationWindow;
    private readonly ScreenRecorder.UI.Recording.MicrophoneWarningPresenter _microphoneWarning;

    public MainWindow()
    {
        _microphoneWarning = new(CreateMicrophoneWarning);
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            CloseAnnotations();
            if (_identificationViewModel is not null)
            {
                _identificationViewModel.PropertyChanged -= OnIdentificationViewModelPropertyChanged;
                _identificationViewModel.RequestScreenAnnotations = null;
                _identificationViewModel.RequestAnnotationCommand = null;
                _identificationViewModel.MicrophoneWarningOverlayVisible = false;
                _identificationViewModel.RequestRecordingProjectName = null;
                _identificationViewModel.RequestRecordingPreflight = null;
                _identificationViewModel.RecordingPreparationChanged = null;
                _identificationViewModel.RequestUnsavedProjectDecision = null;
                _identificationViewModel.RequestProjectDraftRecovery = null;
                _identificationViewModel.RequestMp4Destination = null;
                _identificationViewModel.RequestMediaExport = null;
                _identificationViewModel.RequestWebPlayerDestination = null;
                _identificationViewModel.RequestWebPlayerExport = null;
            }
            _displayIdentification.Close();
            _microphoneWarning.Update(false);
            _identificationViewModel = DataContext as MainViewModel;
            if (_identificationViewModel is not null)
            {
                _identificationViewModel.PropertyChanged += OnIdentificationViewModelPropertyChanged;
            }

            if (DataContext is MainViewModel vm)
            {
                vm.RequestScreenAnnotations = OpenScreenAnnotations;
                vm.RequestAnnotationCommand = RunAnnotationCommand;
                vm.RequestRecordingProjectName = AskRecordingProjectNameAsync;
                vm.RecordingPreparationChanged = state =>
                {
                    if (state == ScreenRecorder.UI.Preflight.RecordingPreparationState.Preparing &&
                        _preparationWindow?.PreparationState == ScreenRecorder.UI.Preflight.RecordingPreparationState.Failed)
                        _preparationWindow.Close();
                    if (_preparationWindow is null && state is not (ScreenRecorder.UI.Preflight.RecordingPreparationState.Started or
                        ScreenRecorder.UI.Preflight.RecordingPreparationState.Failed))
                    {
                        ShowPreparationWindow(new(new(), vm.SendPreflightCommandAsync, preparationOnly: true));
                    }
                    _preparationWindow?.SetPreparationState(state);
                };
                vm.RequestRecordingPreflight = async (configuration, allowPrevious) =>
                {
                    if (allowPrevious && _lastPreflight is { } previous)
                    {
                        var response = await vm.SendPreflightCommandAsync("CheckPreflightConfirmation", previous with { Configuration = configuration }, default);
                        if (response.Success && System.Text.Json.JsonSerializer.Deserialize<ScreenRecorder.Infrastructure.IPC.PreflightReply>(response.DataJson!)?.ConfigurationConfirmed == true)
                            return configuration;
                    }
                    if (_preparationWindow?.PreparationState == ScreenRecorder.UI.Preflight.RecordingPreparationState.Failed)
                        _preparationWindow.Close();
                    var dialog = new ScreenRecorder.UI.Preflight.RecordingPreflightWindow(configuration, vm.SendPreflightCommandAsync);
                    ShowPreparationWindow(dialog);
                    var accepted = await dialog.Confirmation;
                    _lastPreflight = accepted is null ? null : dialog.Request;
                    return accepted;
                };
                vm.RequestMp4Destination = name => ScreenRecorder.UI.Projects.ProjectExportFileDialog.ChooseAsync(this, name);
                vm.RequestMediaExport = (workspace, format) => ScreenRecorder.UI.Projects.ProjectExportFileDialog.ExportAsync(this, workspace, format);
                vm.RequestWebPlayerDestination = () => ScreenRecorder.UI.Projects.ProjectExportFileDialog.ChooseWebParentAsync(this);
                vm.RequestWebPlayerExport = workspace => ScreenRecorder.UI.Projects.ProjectExportFileDialog.ExportWebAsync(this, workspace);
                vm.RequestUnsavedProjectDecision = () => ScreenRecorder.UI.Projects.ProjectSaveDialogs.AskUnsavedAsync(
                    _projectWindow?.IsVisible == true ? _projectWindow : this);
                vm.RequestSubtitleExportDecision = () => ScreenRecorder.UI.Projects.ProjectSaveDialogs.AskSubtitleExportAsync(
                    _projectWindow?.IsVisible == true ? _projectWindow : this);
                vm.RequestProjectDraftRecovery = () => ScreenRecorder.UI.Projects.ProjectSaveDialogs.AskRecoveryAsync(
                    _projectWindow?.IsVisible == true ? _projectWindow : this);
                vm.RequestExistingProjectExport = _ => ScreenRecorder.UI.Projects.ProjectSaveDialogs.AskExistingExportAsync(
                    _projectWindow?.IsVisible == true ? _projectWindow : this);
                vm.OpenExistingProjectExport = path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                vm.RequestMinimizeWindow += (sender, args) =>
                {
                    WindowState = WindowState.Minimized;
                };

                vm.RequestRestoreWindow += (sender, args) =>
                {
                    WindowState = WindowState.Normal;
                    Activate();
                };
            }
        };
    }

    private void ShowPreparationWindow(ScreenRecorder.UI.Preflight.RecordingPreflightWindow dialog)
    {
        if (_preparationWindow is not null) throw new InvalidOperationException("Recording preparation is already active.");
        _preparationWindow = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(_preparationWindow, dialog)) _preparationWindow = null; };
        // Owned, not modal: the ViewModel locks editing/start while existing Stop
        // and force-quit safety controls remain reachable if startup is unknown.
        dialog.Show(_projectWindow?.IsVisible == true ? _projectWindow : this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_initialSizeApplied && Screens.ScreenFromWindow(this) is { } screen)
        {
            _initialSizeApplied = true;
            var decoration = Math.Max(0, (FrameSize?.Height ?? ClientSize.Height) - ClientSize.Height);
            var height = HomeWindowSizing.InitialHeight(screen.WorkingArea.Height, screen.Scaling, decoration);
            MinHeight = Math.Min(MinHeight, height);
            Height = height;
            // Initial centering used the unbounded size; keep the frame inside the work area.
            var top = screen.WorkingArea.Y + Math.Max(0,
                (screen.WorkingArea.Height - (int)Math.Ceiling((height + decoration) * screen.Scaling)) / 2);
            Position = new Avalonia.PixelPoint(Position.X, top);
        }
        Screens.Changed += OnScreensChanged;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        CloseAnnotations();
        _displayIdentification.Close();
        _microphoneWarning.Update(false);
        if (DataContext is MainViewModel vm) vm.MicrophoneWarningOverlayVisible = false;
    }

    private ScreenRecorder.UI.Recording.ICaptureExcludedWarning? CreateMicrophoneWarning()
    {
        // AVFoundation does not provide a verified window-exclusion mechanism.
        // Do not create any floating pixels on unsupported platforms.
        if (!OperatingSystem.IsWindows() || DataContext is not MainViewModel vm) return null;
        var bounds = vm.IsCustomRegion
            ? new CaptureRegion(vm.RegionX, vm.RegionY, vm.RegionWidth, vm.RegionHeight)
            : vm.GetCurrentMonitors().FirstOrDefault(m => m.Index == vm.SelectedMonitor?.Index)?.Bounds;
        if (bounds is null) return null;
        var screen = Screens.ScreenFromPoint(new Avalonia.PixelPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        return screen is null ? null : new ScreenRecorder.UI.Recording.MicrophoneWarningWindow(vm, screen);
    }

    private void OnIdentificationViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(MainViewModel.IsRecording) or nameof(MainViewModel.IsPaused) or nameof(MainViewModel.IsPreparing)
            or nameof(MainViewModel.IsRecovering) or nameof(MainViewModel.ScreenAnnotationsEnabled)
            or nameof(MainViewModel.SelectedMonitor) or nameof(MainViewModel.IsCustomRegion)
            or nameof(MainViewModel.RegionX) or nameof(MainViewModel.RegionY) or nameof(MainViewModel.RegionWidth) or nameof(MainViewModel.RegionHeight))
            QueueAnnotationsUpdate();
        if (sender is MainViewModel reminderVm && e.PropertyName is nameof(MainViewModel.IsMicrophoneSilenceWarningActive)
                or nameof(MainViewModel.IsRecording) or nameof(MainViewModel.IsPaused))
            reminderVm.MicrophoneWarningOverlayVisible = _microphoneWarning.Update(
                reminderVm.IsMicrophoneSilenceWarningActive && reminderVm.IsRecording && !reminderVm.IsPaused);
        if (e.PropertyName == nameof(MainViewModel.IsRecording) &&
            sender is MainViewModel { IsRecording: true } && _projectWindow?.IsVisible == true)
        {
            // Resume may come from a hotkey while the paused editor is visible.
            // Keep its project, but never leave the editing window over an active capture.
            _projectWindow.Hide();
            Show(); Activate();
        }
        if (e.PropertyName is nameof(MainViewModel.IsPreparing) or nameof(MainViewModel.IsRecording)
            or nameof(MainViewModel.IsMonitorSelected) or nameof(MainViewModel.SelectedMonitor)
            or nameof(MainViewModel.IsRecovering))
        {
            _displayIdentification.Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CloseAnnotations();
        Screens.Changed -= OnScreensChanged;
        _displayIdentification.Dispose();
        _microphoneWarning.Dispose();
        if (DataContext is MainViewModel reminderVm) reminderVm.MicrophoneWarningOverlayVisible = false;
        if (_identificationViewModel is not null)
        {
            _identificationViewModel.PropertyChanged -= OnIdentificationViewModelPropertyChanged;
            _identificationViewModel.RequestScreenAnnotations = null;
            _identificationViewModel.RequestAnnotationCommand = null;
            _identificationViewModel = null;
        }
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
        {
            vm.Cleanup();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var settingsCloseAuthorized = _settingsCloseAuthorized;
        _settingsCloseAuthorized = false;
        _displayIdentification.Close();
        base.OnClosing(e);

        if (DataContext is MainViewModel { ForceQuitAuthorized: true }) return;

        if (DataContext is MainViewModel { IsApplicationCloseBlocked: true })
        {
            e.Cancel = true;
            _ = ShowRecordingCloseWarningAsync();
            return;
        }

        if (!_projectCloseAuthorized && (_projectWindow is not null || DataContext is MainViewModel { HasRecordingProject: true }))
        {
            e.Cancel = true;
            _ = CloseProjectAndMainAsync();
            return;
        }

        if (!settingsCloseAuthorized && DataContext is MainViewModel { HasPendingUserSettings: true } preferences)
        {
            e.Cancel = true;
            _ = SavePreferencesAndCloseAsync(preferences);
        }

    }

    private async Task SavePreferencesAndCloseAsync(MainViewModel vm)
    {
        if (_closingSettings) return;
        _closingSettings = true;
        try
        {
            await vm.FlushPendingUserSettingsAsync();
            _settingsCloseAuthorized = true;
            Close(); // Recheck recording/project guards; saving preferences never authorizes a recording shutdown.
        }
        finally { _closingSettings = false; }
    }

    private async Task CloseProjectAndMainAsync()
    {
        if (_closingProject) return;
        _closingProject = true;
        try
        {
            if (_projectWindow is not null && !await _projectWindow.RequestCloseAsync()) return;
            if (DataContext is MainViewModel vm && !await vm.CloseRecordingProjectAsync()) return;
            _projectCloseAuthorized = true;
            Close();
        }
        finally { _closingProject = false; }
    }

    private async void OnProjectWorkspace(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanOpenContentEditor) return;
        if (_projectWindow is not null) { _projectWindow.ShowForRecordingWindow(this); return; }
        try
        {
            var model = await vm.PrepareProjectWorkspaceAsync();
            _projectWindow = new(model, vm);
            _projectWindow.Closed += (_, _) => { _projectWindow = null; vm.IsProjectWorkspaceOpen = false; };
            _projectWindow.ShowForRecordingWindow(this);
        }
        catch (Exception ex) { vm.IsProjectWorkspaceOpen = _projectWindow?.IsVisible == true; vm.StatusMessage = ex.Message; }
    }

    private async void OnProjectAutoExportChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CanChangeProjectExportPolicy && sender is CheckBox box)
            await vm.SetProjectAutoExportAsync(box.IsChecked == true);
    }

    private async Task<string?> AskRecordingProjectNameAsync(string defaultName)
    {
        if (DataContext is not MainViewModel vm) return null;
        var dialog = new ScreenRecorder.UI.Projects.ProjectNameDialog(defaultName, rename: false);
        return await dialog.ShowDialog<string?>(this);
    }

    private async void OnOpenRecordingProject(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanManageRecordingProject) return;
        IStorageFolder? startLocation = null;
        try
        {
            if (Directory.Exists(vm.OutputDirectory))
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(vm.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Serilog.Log.Warning(ex, "Unable to use the recording output directory as the project picker start location");
        }
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = vm.Strings["HomeProjectOpen"], AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter = [new("OpenCam") { Patterns = ["*.opencam"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await vm.OpenRecordingProjectAsync(path);
    }

    private void OnIdentifyDisplaysClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanSelectMonitor)
        {
            return;
        }

        try
        {
            var captures = vm.GetCurrentMonitors()
                .Select(monitor => new CaptureDisplayDescriptor(
                    monitor.Index,
                    monitor.Bounds,
                    monitor.DpiScaling,
                    monitor.IsPrimary,
                    WindowsDisplayIdentity(monitor.DeviceName)))
                .ToArray();
            var uiScreens = Screens.All
                .Select(screen => new UiScreenDescriptor(
                    new CaptureRegion(screen.Bounds.X, screen.Bounds.Y,
                        screen.Bounds.Width, screen.Bounds.Height),
                    screen.Scaling,
                    screen.IsPrimary,
                    WindowsDisplayIdentity(screen.DisplayName),
                    OperatingSystem.IsMacOS()))
                .ToArray();
            var unresolved = _displayIdentification.Show(captures, uiScreens, vm.SelectedMonitor?.Index);
            if (unresolved > 0)
            {
                vm.StatusMessage = vm.Strings.GetFormatted("DisplayIdentificationIncomplete", unresolved);
            }
        }
        catch
        {
            _displayIdentification.Close();
            vm.StatusMessage = vm.Strings.GetFormatted("DisplayIdentificationIncomplete",
                vm.AvailableMonitors.Count);
        }
    }

    private static string? WindowsDisplayIdentity(string? name) =>
        OperatingSystem.IsWindows() &&
        name?.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase) == true
            ? name
            : null;

    public async Task ShowRecordingCloseWarningAsync()
    {
        if (_closeWarningWindow is not null)
        {
            WindowState = WindowState.Normal;
            Activate();
            _closeWarningWindow.Activate();
            return;
        }

        var warningWindow = new RecordingCloseWarningWindow(
            (DataContext as MainViewModel)?.CanForceQuitUnconfirmed == true);
        _closeWarningWindow = warningWindow;
        try
        {
            WindowState = WindowState.Normal;
            if (!IsVisible)
            {
                Show();
            }
            Activate();

            var forceRequested = await warningWindow.ShowDialog<bool>(this);
            if (forceRequested && DataContext is MainViewModel vm && vm.CanForceQuitUnconfirmed)
            {
                var confirmation = new ForceQuitConfirmationWindow();
                if (await confirmation.ShowDialog<bool>(this) && vm.TryAuthorizeForceQuit())
                {
                    Close();
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_closeWarningWindow, warningWindow))
            {
                _closeWarningWindow = null;
            }
        }
    }

    private async void OnSelectRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CanEditRecordingSettings)
        {
            // 防呆保證：明確鎖定為自訂區域
            vm.IsCustomRegion = true;
            vm.IsMonitorSelected = false;

            var selectWindow = new RegionSelectWindow(vm.RegionX, vm.RegionY, vm.RegionWidth, vm.RegionHeight);
            var result = await selectWindow.ShowDialog<bool>(this);
            if (result)
            {
                vm.UpdateCustomRegion(selectWindow.SelectedX, selectWindow.SelectedY, selectWindow.SelectedWidth, selectWindow.SelectedHeight);
            }
        }
    }

    private async void OnChangeOutputFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CanEditRecordingSettings)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "選擇錄影檔案存放資料夾",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    var selectedFolder = folders[0];
                    var localPath = selectedFolder.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(localPath) && System.IO.Directory.Exists(localPath))
                    {
                        vm.SetOutputDirectory(localPath);
                    }
                }
            }
            catch { }
        }
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is MainViewModel vm)
        {
            if (!vm.IsRecordingHotkeyRegistered && vm.MatchesStartStopHotkey(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                if (vm.CanStartRecording)
                {
                    await vm.StartRecordingCommand.ExecuteAsync(null);
                }
                else if ((vm.IsRecording || vm.IsPaused) && !vm.IsPreparing)
                {
                    await vm.StopRecordingCommand.ExecuteAsync(null);
                }
            }
            else if (!vm.IsRecordingHotkeyRegistered && vm.MatchesPauseResumeHotkey(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                if (vm.CanPauseOrResume)
                {
                    await vm.TogglePauseResumeCommand.ExecuteAsync(null);
                }
            }
            else if (vm.MatchesBookmarkHotkey(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                if (!vm.IsBookmarkHotkeyRegistered) await vm.AddBookmarkAsync();
            }
            else if (e.Key == Key.F1)
            {
                e.Handled = true;
                OnAboutClicked(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.F2)
            {
                e.Handled = true;
                OnSettingsClicked(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.O && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                e.Handled = true;
                vm.OpenOutputFolder();
            }
        }
    }

    private async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        await OpenSettingsWindowAsync(initialTabIndex: 0);
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e)
    {
        // 需求 3：右下角的關於保留，打開後直接進入設定頁裡「關於本程式」分頁
        await OpenSettingsWindowAsync(initialTabIndex: 1);
    }

    private async Task OpenSettingsWindowAsync(int initialTabIndex)
    {
        if (DataContext is MainViewModel vm && vm.CanEditRecordingSettings)
        {
            var settingsVm = new SettingsViewModel(vm.SettingsService);
            settingsVm.TryApplyHotkeys = vm.TryApplyHotkeys;
            settingsVm.CanEditCaptureMode = vm.CanEditRecordingSettings;
            System.ComponentModel.PropertyChangedEventHandler captureGuard = (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CanEditRecordingSettings))
                    settingsVm.CanEditCaptureMode = vm.CanEditRecordingSettings;
            };
            await settingsVm.LoadSettingsAsync();
            settingsVm.SelectedTabIndex = initialTabIndex;

            var settingsWindow = new SettingsWindow(settingsVm);
            settingsVm.SettingsSaved += (updatedSettings) =>
            {
                vm.ApplyNewSettings(updatedSettings);
            };
            vm.PropertyChanged += captureGuard;
            try { await settingsWindow.ShowDialog(this); }
            finally { vm.PropertyChanged -= captureGuard; }
        }
    }
}
