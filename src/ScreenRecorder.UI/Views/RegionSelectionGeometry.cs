using Avalonia;
using Avalonia.Controls;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Views;

internal static class RegionSelectionGeometry
{
    public static Size ToLogicalSize(
        int pixelWidth,
        int pixelHeight,
        double renderScaling)
    {
        var scale = renderScaling > 0 ? renderScaling : 1.0;
        return new Size(pixelWidth / scale, pixelHeight / scale);
    }

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

    public static PixelRect ResizeWindow(
        PixelRect initialBounds,
        PixelPoint pointerStart,
        PixelPoint pointerCurrent,
        WindowEdge edge,
        int minimumWidth,
        int minimumHeight)
    {
        var deltaX = pointerCurrent.X - pointerStart.X;
        var deltaY = pointerCurrent.Y - pointerStart.Y;

        var left = initialBounds.X;
        var top = initialBounds.Y;
        var right = initialBounds.X + initialBounds.Width;
        var bottom = initialBounds.Y + initialBounds.Height;

        if (edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest)
        {
            left = Math.Min(right - minimumWidth, left + deltaX);
        }
        else if (edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast)
        {
            right = Math.Max(left + minimumWidth, right + deltaX);
        }

        if (edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast)
        {
            top = Math.Min(bottom - minimumHeight, top + deltaY);
        }
        else if (edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast)
        {
            bottom = Math.Max(top + minimumHeight, bottom + deltaY);
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static int ToEven(int value) =>
        value % 2 == 0 ? value : value - 1;
}
