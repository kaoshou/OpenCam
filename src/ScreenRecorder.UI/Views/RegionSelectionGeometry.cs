using Avalonia;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Views;

internal static class RegionSelectionGeometry
{
    public static CaptureRegion FromWindow(
        PixelPoint position,
        Size clientSize,
        double renderScaling)
    {
        var scale = renderScaling > 0 ? renderScaling : 1.0;
        return new CaptureRegion(
            position.X,
            position.Y,
            ToEven((int)Math.Round(clientSize.Width * scale)),
            ToEven((int)Math.Round(clientSize.Height * scale)));
    }

    public static CaptureRegion ClampToBounds(
        CaptureRegion region,
        CaptureRegion bounds)
    {
        var width = ToEven(Math.Min(region.Width, bounds.Width));
        var height = ToEven(Math.Min(region.Height, bounds.Height));
        var x = Math.Clamp(
            region.X,
            bounds.X,
            bounds.X + bounds.Width - width);
        var y = Math.Clamp(
            region.Y,
            bounds.Y,
            bounds.Y + bounds.Height - height);
        return new CaptureRegion(x, y, width, height);
    }

    private static int ToEven(int value) =>
        value % 2 == 0 ? value : value - 1;
}
