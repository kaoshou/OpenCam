// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using ScreenRecorder.Platform.Windows.Capture;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.UI.Recording;

internal sealed class MicrophoneWarningWindow : Window, ICaptureExcludedWarning
{
    public MicrophoneWarningWindow(MainViewModel vm, Avalonia.Platform.Screen screen)
    {
        DataContext = vm;
        Width = Math.Min(390, screen.WorkingArea.Width / screen.Scaling - 32);
        Height = 112;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0; // A native handle is needed before setting display affinity.
        IsHitTestVisible = false;

        var title = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold,
            Foreground = SolidColorBrush.Parse("#FEF3C7"), TextWrapping = TextWrapping.Wrap };
        title.Bind(TextBlock.TextProperty, new Binding("Strings[MicrophoneSilenceReminderTitle]"));
        var message = new TextBlock { FontSize = 12, Foreground = SolidColorBrush.Parse("#FDE68A"),
            TextWrapping = TextWrapping.Wrap };
        message.Bind(TextBlock.TextProperty, new Binding("Strings[MicrophoneSilenceReminderText]"));
        Content = new Border { Background = SolidColorBrush.Parse("#E6292419"), BorderBrush = SolidColorBrush.Parse("#C0D97706"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16,12),
            Child = new StackPanel { Spacing = 6, Children = { title, message } } };
        Position = new PixelPoint(screen.WorkingArea.Right - (int)Math.Ceiling((Width + 16) * screen.Scaling),
            screen.WorkingArea.Y + (int)Math.Ceiling(24 * screen.Scaling));
    }

    public void PrepareInvisible() => Show();
    public bool TryExcludeFromCapture() => WindowsWindowCaptureExclusion.TryApply(TryGetPlatformHandle()?.Handle ?? 0);
    public void Reveal() => Opacity = 1;
    public void Dispose() { Opacity = 0; Close(); }
}
