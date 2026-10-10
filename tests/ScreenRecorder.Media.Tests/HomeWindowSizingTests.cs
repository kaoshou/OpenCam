using Avalonia.Headless.XUnit;
using ScreenRecorder.UI.Views;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ScreenRecorder.UI.ViewModels;
using ScreenRecorder.Core.Localization;

namespace ScreenRecorder.Media.Tests;

public class HomeWindowSizingTests
{
    [Theory]
    [InlineData(1080, 1, 30, 820)]
    [InlineData(768, 1, 30, 714)]
    [InlineData(1080, 1.5, 30, 666)]
    [InlineData(720, 2, 30, 306)]
    public void InitialHeightFitsLogicalWorkArea(double pixels, double scale, double frame, double expected)
        => Assert.Equal(expected, HomeWindowSizing.InitialHeight(pixels, scale, frame));

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)]
    public void InvalidScalingIsRejected(double scale)
        => Assert.Throws<ArgumentOutOfRangeException>(() => HomeWindowSizing.InitialHeight(1080, scale, 30));

    [AvaloniaFact]
    public void NewWindowLeavesRoomForProjectHeaderWithoutWidening()
    {
        var window = new MainWindow();
        Assert.Equal(840, window.Width);
        Assert.Equal(820, window.Height);
    }

    [AvaloniaTheory]
    [InlineData(AppLanguage.ZhTw)]
    [InlineData(AppLanguage.EnUs)]
    public void DefaultHeightShowsBottomRecordingPreference(AppLanguage language)
    {
        var languages = ScreenRecorder.UI.Localization.LanguageManager.Instance;
        var previous = languages.CurrentLanguage;
        languages.CurrentLanguage = language;
        var vm = new MainViewModel(forScreenshot: true);
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            window.UpdateLayout();
            var bottom = window.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => Equals(c.Content, vm.Strings["MinimizeOnRecord"]));
            var scroll = bottom.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1,
                $"Settings height {scroll.Extent.Height}, visible {scroll.Viewport.Height}");
        }
        finally { window.Close(); vm.Cleanup(); languages.CurrentLanguage = previous; }
    }
}
