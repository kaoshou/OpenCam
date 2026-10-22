// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.UI.Projects;

public partial class ProjectWorkspaceView : Window
{
    private async void OnBookmarksOpening(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control panel } && DataContext is ProjectWorkspaceViewModel vm)
        {
            panel.DataContext = vm;
            await vm.RefreshBookmarksAsync();
        }
    }
    private async void OnManageSubtitles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectWorkspaceViewModel vm || !vm.CanEditTimeline) return;
        if ((vm.IsPlayingPreview || vm.IsChangingPreviewQuality) && !await vm.StopPreviewAsync()) return;
        await new ProjectSubtitlesDialog(vm).ShowDialog<bool>(this);
    }
    private void OnResetSubtitleAppearance(object? sender, RoutedEventArgs e) => Model?.ResetSubtitleAppearance();
    private MainViewModel? _main;
    private Window? _recordingWindow;
    private bool _homeWasEnabled;
    public void ShowForRecordingWindow(Window home)
    {
        if (_recordingWindow is null) _homeWasEnabled = home.IsEnabled;
        _recordingWindow = home;
        if (!IsVisible) Show(home);
        home.IsEnabled = false;
        Activate();
    }
    private bool _closed, _closing;
    private EditorLayout? _layout;
    private Avalonia.Media.Imaging.WriteableBitmap? _previewBitmap;
    public ProjectWorkspaceView()
    {
        InitializeComponent();
        InitializePreviewViewport();
        InitializeClipListDrag();
        TimelineControl.ExportRequested += RequestExportAsync;
        LayoutUpdated += (_, _) => { if (DataContext is ProjectWorkspaceViewModel vm) vm.SetSubtitleViewportWidth(PreviewPane.Bounds.Width); };
        Closed += (_, _) => { if (_recordingWindow is not null) _recordingWindow.IsEnabled = _homeWasEnabled; };
        Opened += (_, _) => UpdateEditorLayout();
        SizeChanged += (_, _) => UpdateEditorLayout();
        if (PlatformSettings is not null)
        {
            ApplyTheme(PlatformSettings.GetColorValues().ThemeVariant);
            PlatformSettings.ColorValuesChanged += OnColorsChanged;
            Closed += (_, _) => PlatformSettings.ColorValuesChanged -= OnColorsChanged;
        }
    }
    private void UpdateEditorLayout()
    {
        if (IsPreviewFullscreen || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        var next = EditorLayout.ForSize(ClientSize.Width, ClientSize.Height);
        if (_layout == next) return;
        _layout = next;
        ClipPane.IsVisible = next.LeftVisible;
        InspectorPane.IsVisible = next.RightVisible;
        ApplyPanelWidths();
    }
    private void ApplyPanelWidths()
    {
        WorkspaceGrid.ColumnDefinitions[0].Width = new(ClipPane.IsVisible ? 220 : 0);
        WorkspaceGrid.ColumnDefinitions[2].Width = new(InspectorPane.IsVisible ? 260 : 0);
    }
    private void OnToggleClips(object? sender, RoutedEventArgs e)
    {
        ClipPane.IsVisible = !ClipPane.IsVisible;
        if (ClipPane.IsVisible && ClientSize.Width < 1200) InspectorPane.IsVisible = false;
        ApplyPanelWidths();
    }
    private void OnToggleProperties(object? sender, RoutedEventArgs e)
    {
        InspectorPane.IsVisible = !InspectorPane.IsVisible;
        if (InspectorPane.IsVisible && ClientSize.Width < 1200) ClipPane.IsVisible = false;
        ApplyPanelWidths();
    }
    private void ApplyTheme(Avalonia.Platform.PlatformThemeVariant theme) => RequestedThemeVariant =
        theme == Avalonia.Platform.PlatformThemeVariant.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    private void OnColorsChanged(object? sender, Avalonia.Platform.PlatformColorValues values) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme(values.ThemeVariant));
    public ProjectWorkspaceView(ProjectWorkspaceViewModel vm, MainViewModel main) : this()
    {
        DataContext = vm;
        _main = main;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => { await vm.PollRecordingAsync(); await vm.PollWaveformAsync();
            if (IsVisible && ClipPane.IsVisible && vm.PreviewFrame is not null)
            {
                vm.SetVisibleThumbnailClips(ClipList.GetRealizedContainers()
                    .Select(c => c.DataContext).OfType<ScreenRecorder.Core.Projects.ProjectClip>().Select(c => c.Id));
                await vm.PollThumbnailAsync();
            } };
        Opened += async (_, _) => { timer.Start(); await vm.RefreshBookmarksAsync(); };
        Closed += (_, _) => timer.Stop();
        var previewTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        previewTimer.Tick += async (_, _) => { if (IsVisible) await vm.PollPreviewAsync(); };
        vm.PropertyChanged += OnPreviewChanged;
        Opened += (_, _) => previewTimer.Start();
        Closed += (_, _) => {
            previewTimer.Stop(); vm.PropertyChanged -= OnPreviewChanged;
            PreviewImage.Source = null; _previewBitmap?.Dispose(); _previewBitmap = null;
        };
    }
    private void OnPreviewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectWorkspaceViewModel.PreviewFrame)) return;
        if (Model.PreviewFrame is not { HasValidPixels: true } frame)
        { PreviewImage.Source = null; _previewBitmap?.Dispose(); _previewBitmap = null; return; }
        var bytes = frame.Rgba!;
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new(frame.PixelWidth, frame.PixelHeight), new(96, 96),
            Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var locked = bitmap.Lock())
            for (var y = 0; y < frame.PixelHeight; y++)
                System.Runtime.InteropServices.Marshal.Copy(bytes, y * frame.PixelWidth * 4, locked.Address + y * locked.RowBytes, frame.PixelWidth * 4);
        var previous = _previewBitmap;
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
        previous?.Dispose();
        UpdatePreviewViewport();
    }
    private ProjectWorkspaceViewModel Model => (ProjectWorkspaceViewModel)DataContext!;

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        await RequestExportAsync(ScreenRecorder.Core.Projects.ProjectExportFormat.Mp4, null);
    }
    private async void OnExportGif(object? sender, RoutedEventArgs e) =>
        await RequestExportAsync(ScreenRecorder.Core.Projects.ProjectExportFormat.Gif, null);
    private Task RequestExportAsync(ScreenRecorder.Core.Projects.ProjectExportFormat format, Guid? clipId) =>
        ProjectExportFileDialog.ExportAsync(this, Model, format, clipId);
    private async void OnExportWebPlayer(object? sender, RoutedEventArgs e)
    {
        if (!Model.CanExport) return;
        Model.RequestSubtitleExportDecision ??= () => ProjectSaveDialogs.AskSubtitleExportAsync(this);
        await ProjectExportFileDialog.ExportWebAsync(this, Model);
    }

    private async void OnCaptureSettings(object? sender, RoutedEventArgs e)
    {
        await ReturnToRecordingAsync();
    }
    public async Task ReturnToRecordingAsync()
    {
        if (!Model.CanReturnToRecording) return;
        var home = _recordingWindow ?? Owner;
        if (!await RequestCloseAsync()) return;
        if (home is Window window)
        {
            window.Show(); window.WindowState = WindowState.Normal; window.Activate();
        }
    }
    private async void OnRenameProject(object? sender, RoutedEventArgs e)
    {
        if (!Model.CanRecord) return;
        var dialog = new ProjectNameDialog(Model.State.Name, rename: true);
        if (await dialog.ShowDialog<string?>(this) is { } name)
        {
            await Model.RenameProjectAsync(name);
        }
    }
    private void OnFitTimeline(object? sender, RoutedEventArgs e) => TimelineControl.Fit();
    private void OnToggleClipDensity(object? sender, RoutedEventArgs e) => Model.IsClipListCompact = !Model.IsClipListCompact;
    private async void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (!Model.State.NeedsRecoveryConfirmation || Model.IsBusy) return;
        var dialog = new Window { Width = 440, Height = 230, CanResize = false,
            Title = Model.Strings["ProjectRestoreBackup"], WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var confirm = new Button { Content = Model.Strings["Confirm"] };
        var cancel = new Button { Content = Model.Strings["Cancel"] };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Content = new StackPanel { Margin = new(24), Spacing = 20, Children = {
            new TextBlock { Text = Model.Strings["ProjectRestoreWarning"], TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { confirm, cancel } }
        }};
        if (await dialog.ShowDialog<bool>(this)) await Model.RestoreBackupAsync();
    }
    private async void OnClose(object? sender, RoutedEventArgs e) => await RequestCloseAsync();
    public async Task<bool> RequestCloseAsync()
    {
        if (_closing) return false;
        _closing = true;
        try
        {
            if (_main?.UsesRecordingContent == true)
            {
                if (!await Model.ResolveUnsavedAsync()) return false;
                if (!await _main.LeaveContentEditorAsync()) return false;
            }
            else if (!await Model.CloseAsync()) return false;
            _closed = true;
            Close();
            if (_main is not null) _main.IsProjectWorkspaceOpen = false;
            return true;
        }
        finally { _closing = false; }
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closed) return;
        e.Cancel = true;
        _ = RequestCloseAsync();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dragClip is not null) { CancelClipDrag(); e.Handled = true; return; }
        ShowPreviewControls();
        if (e.Key == Key.Escape && IsPreviewFullscreen) { SetPreviewFullscreen(false); e.Handled = true; return; }
        var action = ResolveShortcut(e.Key, e.KeyModifiers, FocusManager?.GetFocusedElement() is TextBox, OperatingSystem.IsMacOS());
        if (action == "save")
        { e.Handled = true; if (!Model.IsBusy) _ = Model.SaveAsync(); return; }
        // Text inputs own their own text undo. Project undo only applies outside them.
        if (action is "undo" or "redo")
        {
            if (action == "redo" && Model.CanRedo) _ = Model.RedoAsync();
            else if (action == "undo" && Model.CanUndo) _ = Model.UndoAsync();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    internal static string? ResolveShortcut(Key key, KeyModifiers modifiers, bool textInput, bool mac)
    {
        var command = mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if ((modifiers & command) == 0) return null;
        if (key == Key.S) return "save";
        if (textInput) return null;
        if (key == Key.Y && !mac || key == Key.Z && (modifiers & KeyModifiers.Shift) != 0) return "redo";
        return key == Key.Z ? "undo" : null;
    }
}
