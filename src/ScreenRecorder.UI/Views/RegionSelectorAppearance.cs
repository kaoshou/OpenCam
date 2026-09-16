using Avalonia.Controls;
using Avalonia.Media;

namespace ScreenRecorder.UI.Views;

internal static class RegionSelectorAppearance
{
    internal static IReadOnlyList<WindowTransparencyLevel> TransparencyLevels(
        bool isMacOS) =>
        isMacOS
            ? new[] { WindowTransparencyLevel.Transparent }
            : new[]
            {
                WindowTransparencyLevel.Transparent,
                WindowTransparencyLevel.None
            };

    internal static Color FallbackColor(bool isMacOS)
    {
        _ = isMacOS;
        return Colors.Transparent;
    }
}
