// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

public sealed class ProjectGifExportDialog : Window
{
    public ProjectGifExportDialog(ProjectExportTarget target)
    {
        var strings = LanguageManager.Instance;
        Title = strings["ProjectExportGif"];
        Width = 480; MinWidth = 360; MaxHeight = 680; SizeToContent = SizeToContent.Height;
        CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var width = new ComboBox { Name = "GifWidth", ItemsSource = new[] { "480", "640", "960", strings["GifOriginal"] }, SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var fps = new ComboBox { Name = "GifFps", ItemsSource = new[] { "10", "15", "20" }, SelectedIndex = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Avalonia.Automation.AutomationProperties.SetName(width, strings["GifWidth"]);
        Avalonia.Automation.AutomationProperties.SetName(fps, strings["GifFps"]);
        var time = TimeSpan.FromTicks(target.DurationTicks);
        var duration = $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
        var warning = new TextBlock { Name = "GifWarning", IsVisible = false, TextWrapping = TextWrapping.Wrap,
            Text = string.Format(strings["GifLongWarning"], duration) };
        var confirm = new Button { Name = "GifConfirm", Content = strings["Confirm"], IsDefault = true };
        var cancel = new Button { Content = strings["Cancel"], IsCancel = true };
        confirm.Click += (_, _) =>
        {
            if (target.DurationTicks >= TimeSpan.FromMinutes(5).Ticks && !warning.IsVisible)
            {
                warning.IsVisible = true; confirm.Content = strings["GifContinue"]; return;
            }
            if (width.SelectedIndex is < 0 or > 3 || fps.SelectedIndex is < 0 or > 2) return;
            Close(new GifExportOptions(new int?[] {480,640,960,null}[width.SelectedIndex], new[] {10,15,20}[fps.SelectedIndex]));
        };
        cancel.Click += (_, _) => Close((GifExportOptions?)null);
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = target.Name, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = duration },
            new TextBlock { Text = strings["GifNoAudio"], TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = strings["GifWidth"] }, width,
            new TextBlock { Text = strings["GifFps"] }, fps,
            new TextBlock { Text = strings["GifQualityNotice"], FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap },
            warning,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12,
                Children = { cancel, confirm } }
        } } };
    }
    public static Task<GifExportOptions?> ShowAsync(Window owner, ProjectExportTarget target) =>
        new ProjectGifExportDialog(target).ShowDialog<GifExportOptions?>(owner);
}
