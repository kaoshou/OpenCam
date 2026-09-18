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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnReturnClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
