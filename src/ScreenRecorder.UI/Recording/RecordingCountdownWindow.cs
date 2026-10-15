// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Recording;

public sealed class RecordingCountdownWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private bool _done;
    public void CancelCountdown() { if (_done) return; _done = true; _cancel.Cancel(); Close(false); }
    public RecordingCountdownWindow(int seconds)
    {
        var strings = LanguageManager.Instance;
        Title = strings["CountdownTitle"]; Width = 300; Height = 260; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var number = new TextBlock { FontSize = 76, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#2563EB")) };
        var cancel = new Button { Content = strings["Cancel"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        cancel.Click += (_, _) => CancelCountdown();
        Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children = {
            new TextBlock { Text = strings["CountdownTitle"], HorizontalAlignment = HorizontalAlignment.Center }, number, cancel } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; CancelCountdown(); } };
        Closed += (_, _) => { _done = true; _cancel.Cancel(); };
        Opened += async (_, _) =>
        {
            try { await RecordingCountdown.RunAsync(seconds, n => number.Text = n.ToString(), _cancel.Token); _done = true; Close(true); }
            catch (OperationCanceledException) { }
            finally { _cancel.Dispose(); }
        };
    }
}
