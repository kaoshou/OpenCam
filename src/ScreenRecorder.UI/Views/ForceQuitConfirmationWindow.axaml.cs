// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Views;

public partial class ForceQuitConfirmationWindow : Window
{
    public ForceQuitConfirmationWindow()
    {
        InitializeComponent();
        DataContext = LanguageManager.Instance;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
