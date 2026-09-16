using System.Runtime.InteropServices;

namespace ScreenRecorder.Platform.macOS;

internal readonly record struct MacOsDisplaySnapshot(
    uint Id,
    double X,
    double Y,
    double Width,
    double Height,
    nuint PixelWidth,
    nuint PixelHeight,
    bool IsPrimary);

internal interface IMacOsDisplayApi
{
    IReadOnlyList<MacOsDisplaySnapshot> GetActiveDisplays();
}

internal sealed class CoreGraphicsDisplayApi : IMacOsDisplayApi
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double Width;
        public readonly double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly CGPoint Origin;
        public readonly CGSize Size;
    }

    [DllImport(CoreGraphics)]
    private static extern int CGGetActiveDisplayList(
        uint maxDisplays,
        [Out] uint[]? displays,
        out uint displayCount);

    [DllImport(CoreGraphics)]
    private static extern CGRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayPixelsWide(uint display);

    [DllImport(CoreGraphics)]
    private static extern nuint CGDisplayPixelsHigh(uint display);

    [DllImport(CoreGraphics)]
    private static extern int CGDisplayIsMain(uint display);

    public IReadOnlyList<MacOsDisplaySnapshot> GetActiveDisplays()
    {
        if (CGGetActiveDisplayList(0, null, out var count) != 0 || count == 0)
        {
            return Array.Empty<MacOsDisplaySnapshot>();
        }

        var displayIds = new uint[count];
        if (CGGetActiveDisplayList(count, displayIds, out var populatedCount) != 0 ||
            populatedCount == 0)
        {
            return Array.Empty<MacOsDisplaySnapshot>();
        }

        var result = new List<MacOsDisplaySnapshot>((int)populatedCount);
        for (var index = 0; index < populatedCount; index++)
        {
            var displayId = displayIds[index];
            var bounds = CGDisplayBounds(displayId);
            result.Add(new MacOsDisplaySnapshot(
                displayId,
                bounds.Origin.X,
                bounds.Origin.Y,
                bounds.Size.Width,
                bounds.Size.Height,
                CGDisplayPixelsWide(displayId),
                CGDisplayPixelsHigh(displayId),
                CGDisplayIsMain(displayId) != 0));
        }

        return result;
    }
}
