using Avalonia.Controls;
using Avalonia.Media;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public class RegionSelectorAppearanceTests
{
    [Fact]
    public void MacOs_UsesTransparentCompositionWithoutBlur()
    {
        var levels = RegionSelectorAppearance.TransparencyLevels(isMacOS: true);

        Assert.Equal(new[] { WindowTransparencyLevel.Transparent }, levels);
        Assert.Equal(
            Colors.Transparent,
            RegionSelectorAppearance.FallbackColor(isMacOS: true));
        Assert.DoesNotContain(WindowTransparencyLevel.Blur, levels);
    }
}
