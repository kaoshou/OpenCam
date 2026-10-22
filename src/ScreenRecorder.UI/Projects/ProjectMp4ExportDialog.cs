// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Infrastructure.Settings;
using ScreenRecorder.UI.Localization;

namespace ScreenRecorder.UI.Projects;

public sealed class ProjectMp4ExportDialog : Window
{
    public ProjectMp4ExportDialog(ProjectExportTarget target, Mp4ExportOptions previous, bool webPlayer = false)
    {
        previous.Validate();
        var strings = LanguageManager.Instance;
        Title = strings["Mp4ExportSettings"];
        Width = 480; MinWidth = 360; MaxHeight = 680; SizeToContent = SizeToContent.Height;
        CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var quality = new ComboBox
        {
            Name = "Mp4Quality", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { strings["Mp4QualityOriginal"], strings["Mp4QualityStandard"], strings["Mp4QualityCompact"] },
            SelectedIndex = (int)previous.Quality
        };
        var resolution = new ComboBox
        {
            Name = "Mp4Resolution", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { strings["Mp4ResolutionOriginal"], strings["Mp4Resolution1080"], strings["Mp4Resolution720"] },
            SelectedIndex = (int)previous.Resolution
        };
        AutomationProperties.SetName(quality, strings["Mp4ExportQuality"]);
        AutomationProperties.SetName(resolution, strings["Mp4ExportResolution"]);
        var confirm = new Button { Name = "Mp4Confirm", Content = strings["Mp4ExportNext"], IsDefault = true };
        var cancel = new Button { Name = "Mp4Cancel", Content = strings["Cancel"], IsCancel = true };
        confirm.Click += (_, _) =>
        {
            if (quality.SelectedIndex is < 0 or > 2 || resolution.SelectedIndex is < 0 or > 2) return;
            Close(new Mp4ExportOptions((Mp4ExportQuality)quality.SelectedIndex, (Mp4ExportResolution)resolution.SelectedIndex));
        };
        cancel.Click += (_, _) => Close((Mp4ExportOptions?)null);
        Content = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = target.Name, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = strings["Mp4ExportQuality"] }, quality,
            new TextBlock { Text = strings["Mp4ExportResolution"] }, resolution,
            new TextBlock { Text = strings["Mp4ExportNotice"], FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Name = "Mp4WebNotice", IsVisible = webPlayer, Text = strings["Mp4WebNotice"], TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 12, Children = { cancel, confirm } }
        } } };
    }

    public static Task<Mp4ExportOptions?> ShowAsync(Window owner, ProjectExportTarget target, bool webPlayer = false) =>
        ShowAsync(owner, target, new Mp4ExportPreferencesStore(), webPlayer);

    public static async Task<Mp4ExportOptions?> ShowAsync(Window owner, ProjectExportTarget target,
        Mp4ExportPreferencesStore preferences, bool webPlayer = false)
    {
        var previous = await preferences.LoadAsync();
        var options = await new ProjectMp4ExportDialog(target, previous, webPlayer).ShowDialog<Mp4ExportOptions?>(owner);
        if (options is not null) await preferences.SaveAsync(options);
        return options;
    }
}
