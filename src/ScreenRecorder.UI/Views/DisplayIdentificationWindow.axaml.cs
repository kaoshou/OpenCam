// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Views;

public partial class DisplayIdentificationWindow : Window
{
    public DisplayIdentificationWindow()
    {
        InitializeComponent();
    }

    internal DisplayIdentificationWindow(DisplayIdentificationBadge badge) : this()
    {
        NumberText.Text = badge.Number.ToString();
        SelectedText.Text = badge.IsSelected
            ? LanguageManager.Instance["DisplayIdentificationSelected"]
            : string.Empty;
        BadgeBorder.BorderBrush = badge.IsSelected
            ? SolidColorBrush.Parse("#FBBF24")
            : SolidColorBrush.Parse("#94A3B8");

        var bounds = badge.Screen.Bounds;
        var scale = badge.Screen.Scaling > 0 ? badge.Screen.Scaling : 1;
        Position = new PixelPoint(
            bounds.X + (int)Math.Round((bounds.Width - Width * scale) / 2),
            bounds.Y + (int)Math.Round((bounds.Height - Height * scale) / 2));
    }
}

internal sealed class AvaloniaDisplayBadgePresenter : IDisplayBadgePresenter
{
    public IDisposable Show(DisplayIdentificationBadge badge)
    {
        var window = new DisplayIdentificationWindow(badge);
        window.Show();
        return new BadgeHandle(window);
    }

    private sealed class BadgeHandle(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}

internal sealed class AvaloniaDisplayIdentificationTimer : IDisplayIdentificationTimer
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        EventHandler handler = (_, _) =>
        {
            timer.Stop();
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return new TimerHandle(timer, handler);
    }

    private sealed class TimerHandle(DispatcherTimer timer, EventHandler handler) : IDisposable
    {
        public void Dispose()
        {
            timer.Stop();
            timer.Tick -= handler;
        }
    }
}
