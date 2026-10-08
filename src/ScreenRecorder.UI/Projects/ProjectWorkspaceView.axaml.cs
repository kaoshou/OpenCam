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
    private MainViewModel? _main;
    private bool _closed, _closing;
    private EditorLayout? _layout;
    public ProjectWorkspaceView()
    {
        InitializeComponent();
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
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
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
        RecentProjects.ItemsSource = main.RecentProjectPaths;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) => await vm.PollRecordingAsync();
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }
    private ProjectWorkspaceViewModel Model => (ProjectWorkspaceViewModel)DataContext!;

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        if (Model.IsBusy || !await Model.CloseAsync()) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = Model.Strings["ProjectChooseFolder"], AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        await Model.CreateAsync(path, Model.Strings["ProjectDefaultName"] + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm"));
        RecentProjects.ItemsSource = _main?.RecentProjectPaths;
    }
    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (Model.IsBusy || !await Model.CloseAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Model.Strings["ProjectOpen"], AllowMultiple = false,
            FileTypeFilter = [new("OpenCam") { Patterns = ["*.opencam"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await Model.OpenAsync(path);
        RecentProjects.ItemsSource = _main?.RecentProjectPaths;
    }
    private async void OnOpenRecent(object? sender, RoutedEventArgs e)
    {
        if (RecentProjects.SelectedItem is string path && !Model.IsBusy && await Model.CloseAsync()) await Model.OpenAsync(path);
    }
    private async void OnRecord(object? sender, RoutedEventArgs e)
    {
        if (Model.CanRecord && _main?.CheckProjectScreenPermission() == true) await Model.StartAsync(_main.BuildProjectConfiguration());
    }
    private void OnCaptureSettings(object? sender, RoutedEventArgs e) => Owner?.Activate();
    private void OnFitTimeline(object? sender, RoutedEventArgs e) => TimelineControl.Fit();
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
            if (!await Model.CloseAsync()) return false;
            _closed = true;
            Close();
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
