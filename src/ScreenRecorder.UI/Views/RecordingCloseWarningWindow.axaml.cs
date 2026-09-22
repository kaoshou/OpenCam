// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Views;

public partial class RecordingCloseWarningWindow : Window
{
    public RecordingCloseWarningWindow()
    {
        InitializeComponent();
        DataContext = LanguageManager.Instance;
    }

    internal RecordingCloseWarningWindow(bool allowForceQuit) : this()
    {
        ForceQuitButton.IsVisible = allowForceQuit;
        if (allowForceQuit)
        {
            WarningMessageText.Text = LanguageManager.Instance["RecordingCloseUnconfirmedMessage"];
            WarningDetailText.Text = LanguageManager.Instance["RecordingCloseUnconfirmedDetail"];
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close(false);
        }
    }

    private void OnReturnClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnForceQuitClicked(object? sender, RoutedEventArgs e) => Close(true);
}
