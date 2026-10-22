// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectMp4ExportDialogTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.ZhTw)]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.EnUs)]
    [InlineData(ScreenRecorder.Core.Localization.AppLanguage.JaJp)]
    public void DialogUsesSelectedApplicationLanguage(ScreenRecorder.Core.Localization.AppLanguage language)
    {
        var strings = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var previous = strings.CurrentLanguage;
        var owner = new Window();
        ProjectMp4ExportDialog? dialog = null;
        try
        {
            strings.CurrentLanguage = language;
            owner.Show();
            dialog = new(new(Guid.NewGuid(), 1, null, "Movie", 1), new(), true);
            dialog.ShowDialog<Mp4ExportOptions?>(owner);
            dialog.UpdateLayout();
            Assert.Equal(strings["Mp4ExportSettings"], dialog.Title);
            var controls = dialog.GetVisualDescendants().ToArray();
            var quality = controls.OfType<ComboBox>().Single(c => c.Name == "Mp4Quality");
            Assert.Equal(new[] { strings["Mp4QualityOriginal"], strings["Mp4QualityStandard"], strings["Mp4QualityCompact"] },
                quality.Items.Cast<string>());
            Assert.Equal(strings["Mp4WebNotice"], controls.OfType<TextBlock>().Single(c => c.Name == "Mp4WebNotice").Text);
            Assert.True(quality.Bounds.Width > 300);
            var confirm = controls.OfType<Button>().Single(c => c.Name == "Mp4Confirm");
            Assert.Equal(strings["Mp4ExportNext"], confirm.Content);
            Assert.True(confirm.Bounds.Width > 0);
        }
        finally { dialog?.Close(); owner.Close(); strings.CurrentLanguage = previous; }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task PreviousChoiceIsPreselectedAndCancelReturnsNothing(bool webPlayer)
    {
        var type = typeof(ProjectGifExportDialog).Assembly.GetType("ScreenRecorder.UI.Projects.ProjectMp4ExportDialog");
        Assert.NotNull(type);
        var owner = new Window();
        var dialog = (Window)Activator.CreateInstance(type, new ProjectExportTarget(Guid.NewGuid(), 7, null, "Movie", 1),
            new Mp4ExportOptions(Mp4ExportQuality.Compact, Mp4ExportResolution.FullHd), webPlayer)!;
        try
        {
            owner.Show(); var result = dialog.ShowDialog<object?>(owner); dialog.UpdateLayout();
            var controls = dialog.GetVisualDescendants().ToArray();
            Assert.Equal(2, controls.OfType<ComboBox>().Single(c => c.Name == "Mp4Quality").SelectedIndex);
            Assert.Equal(1, controls.OfType<ComboBox>().Single(c => c.Name == "Mp4Resolution").SelectedIndex);
            Assert.Equal(webPlayer, controls.OfType<TextBlock>().Single(c => c.Name == "Mp4WebNotice").IsVisible);
            controls.OfType<Button>().Single(c => c.Name == "Mp4Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(await result);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(0, 0)] [InlineData(1, 2)] [InlineData(2, 1)]
    public async Task SelectedQualityAndResolutionAreReturned(int quality, int resolution)
    {
        var type = typeof(ProjectGifExportDialog).Assembly.GetType("ScreenRecorder.UI.Projects.ProjectMp4ExportDialog");
        Assert.NotNull(type);
        var optionsType = typeof(ProjectExportOptions).Assembly.GetType("ScreenRecorder.Core.Projects.Mp4ExportOptions")!;
        var parameters = optionsType.GetConstructors().Single().GetParameters();
        var previous = Activator.CreateInstance(optionsType, Enum.ToObject(parameters[0].ParameterType, 0), Enum.ToObject(parameters[1].ParameterType, 0));
        var owner = new Window();
        var dialog = (Window)Activator.CreateInstance(type, new ProjectExportTarget(Guid.NewGuid(), 7, null, "Movie", TimeSpan.FromSeconds(3).Ticks), previous!, false)!;
        try
        {
            owner.Show();
            var result = dialog.ShowDialog<object?>(owner);
            dialog.UpdateLayout();
            var controls = dialog.GetVisualDescendants().ToArray();
            controls.OfType<ComboBox>().Single(c => c.Name == "Mp4Quality").SelectedIndex = quality;
            controls.OfType<ComboBox>().Single(c => c.Name == "Mp4Resolution").SelectedIndex = resolution;
            controls.OfType<Button>().Single(c => c.Name == "Mp4Confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var options = await result;
            Assert.NotNull(options);
            Assert.Equal(quality, Convert.ToInt32(optionsType.GetProperty("Quality")!.GetValue(options)));
            Assert.Equal(resolution, Convert.ToInt32(optionsType.GetProperty("Resolution")!.GetValue(options)));
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
