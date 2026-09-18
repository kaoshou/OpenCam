using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Views;

public partial class MainWindow : Window
{
    private RecordingCloseWarningWindow? _closeWarningWindow;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is MainViewModel vm)
            {
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
        {
            vm.Cleanup();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (DataContext is MainViewModel { IsApplicationCloseBlocked: true })
        {
            e.Cancel = true;
            _ = ShowRecordingCloseWarningAsync();
        }
    }

    public async Task ShowRecordingCloseWarningAsync()
    {
        if (_closeWarningWindow is not null)
        {
            WindowState = WindowState.Normal;
            Activate();
            _closeWarningWindow.Activate();
            return;
        }

        var warningWindow = new RecordingCloseWarningWindow();
        _closeWarningWindow = warningWindow;
        try
        {
            WindowState = WindowState.Normal;
            if (!IsVisible)
            {
                Show();
            }
            Activate();

            await warningWindow.ShowDialog(this);
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
            if (vm.MatchesStartStopHotkey(e.Key, e.KeyModifiers))
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
            else if (vm.MatchesPauseResumeHotkey(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                if (vm.CanPauseOrResume)
                {
                    await vm.TogglePauseResumeCommand.ExecuteAsync(null);
                }
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
            await settingsVm.LoadSettingsAsync();
            settingsVm.SelectedTabIndex = initialTabIndex;

            var settingsWindow = new SettingsWindow(settingsVm);
            settingsVm.SettingsSaved += (updatedSettings) =>
            {
                vm.ApplyNewSettings(updatedSettings);
            };
            await settingsWindow.ShowDialog(this);
        }
    }
}
