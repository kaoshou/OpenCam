using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args != null)
            {
                for (int i = 0; i < desktop.Args.Length - 1; i++)
                {
                    if (desktop.Args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase))
                    {
                        var langCode = desktop.Args[i + 1];
                        if (langCode.Equals("en", StringComparison.OrdinalIgnoreCase))
                        {
                            ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = ScreenRecorder.Core.Localization.AppLanguage.EnUs;
                        }
                        else if (langCode.Equals("zh", StringComparison.OrdinalIgnoreCase))
                        {
                            ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = ScreenRecorder.Core.Localization.AppLanguage.ZhTw;
                        }
                        break;
                    }
                }
            }

            var viewModel = new MainViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            _mainWindow = mainWindow;
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, e) =>
            {
                if (viewModel.IsApplicationCloseBlocked)
                {
                    e.Cancel = true;
                    _ = mainWindow.ShowRecordingCloseWarningAsync();
                }
            };

            if (desktop.Args != null && desktop.Args.Length >= 2 && desktop.Args[0] == "--screenshot")
            {
                var outputPath = desktop.Args[1];
                ScreenRecorder.Core.Localization.AppLanguage? overrideLang = null;
                string stateMode = "idle";
                for (int i = 0; i < desktop.Args.Length - 1; i++)
                {
                    if (desktop.Args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase))
                    {
                        if (desktop.Args[i + 1].Equals("en", StringComparison.OrdinalIgnoreCase))
                            overrideLang = ScreenRecorder.Core.Localization.AppLanguage.EnUs;
                        else if (desktop.Args[i + 1].Equals("zh", StringComparison.OrdinalIgnoreCase))
                            overrideLang = ScreenRecorder.Core.Localization.AppLanguage.ZhTw;
                    }
                    else if (desktop.Args[i].Equals("--state", StringComparison.OrdinalIgnoreCase))
                    {
                        stateMode = desktop.Args[i + 1].ToLowerInvariant();
                    }
                }

                mainWindow.Opened += async (s, e) =>
                {
                    if (overrideLang.HasValue)
                    {
                        ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = overrideLang.Value;
                    }
                    await Task.Delay(400);

                    if (stateMode == "recording")
                    {
                        viewModel.IsRecording = true;
                        viewModel.IsPaused = false;
                        viewModel.ElapsedTimeText = "00:02:18";
                        viewModel.FileSizeText = "16.4 MB";
                        viewModel.StatusMessage = ScreenRecorder.UI.Localization.LanguageManager.Instance["StatusRecordingActive"];
                    }
                    else if (stateMode == "paused")
                    {
                        viewModel.IsRecording = false;
                        viewModel.IsPaused = true;
                        viewModel.ElapsedTimeText = "00:04:35";
                        viewModel.FileSizeText = "32.8 MB";
                        viewModel.StatusMessage = ScreenRecorder.UI.Localization.LanguageManager.Instance["StatusPausedMsg"];
                    }

                    if (overrideLang.HasValue)
                    {
                        ScreenRecorder.UI.Localization.LanguageManager.Instance.CurrentLanguage = overrideLang.Value;
                    }
                    await Task.Delay(400);

                    if (stateMode == "settings" || stateMode == "settings-about")
                    {
                        var settingsWin = new SettingsWindow();
                        var settingsVm = new SettingsViewModel(viewModel.SettingsService);
                        await settingsVm.LoadSettingsAsync();
                        if (overrideLang.HasValue)
                        {
                            settingsVm.SelectedLanguage = System.Linq.Enumerable.FirstOrDefault(settingsVm.AvailableLanguages, l => l.Language == overrideLang.Value);
                        }
                        if (stateMode == "settings-about")
                        {
                            settingsVm.SelectedTabIndex = 1;
                        }
                        settingsWin.DataContext = settingsVm;
                        settingsWin.Show();
                        await Task.Delay(500);

                        try
                        {
                            var sw = (int)settingsWin.Width;
                            var sh = (int)settingsWin.Height;
                            if (sw <= 0) sw = 650;
                            if (sh <= 0) sh = 820;
                            using var rtb = new RenderTargetBitmap(new PixelSize(sw, sh), new Vector(96, 96));
                            rtb.Render(settingsWin);
                            rtb.Save(outputPath);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Settings screenshot failed: " + ex);
                        }
                        finally
                        {
                            settingsWin.Close();
                            desktop.Shutdown();
                        }
                        return;
                    }

                    try
                    {
                        var w = (int)mainWindow.Width;
                        var h = (int)mainWindow.Height;
                        if (w <= 0) w = 840;
                        if (h <= 0) h = 740;
                        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                        rtb.Render(mainWindow);
                        rtb.Save(outputPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Screenshot failed: " + ex);
                    }
                    finally
                    {
                        desktop.Shutdown();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindow? _mainWindow;

    public void OnTrayIconClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    public void OnShowMainWindowClicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    public void OnOpenFolderClicked(object? sender, EventArgs e)
    {
        if (_mainWindow?.DataContext is MainViewModel vm)
        {
            vm.OpenOutputFolder();
        }
    }

    public void OnExitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown();
        }
    }
}
