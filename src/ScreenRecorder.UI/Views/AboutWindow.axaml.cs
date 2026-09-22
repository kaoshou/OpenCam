// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Views;

public partial class AboutWindow : Window
{
    public LanguageManager Strings => LanguageManager.Instance;

    public AboutWindow()
    {
        DataContext = this;
        InitializeComponent();

        Opened += (s, e) => Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnGitHubClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/kaoshou/OpenCam",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
