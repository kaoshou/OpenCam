// SPDX-License-Identifier: AGPL-3.0-or-later
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
    string? GetDisplayUuid(uint displayId) => null;
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

    // Despite its CG prefix, this UUID conversion is exported by ColorSync.
    [DllImport("/System/Library/Frameworks/ColorSync.framework/ColorSync")]
    private static extern nint CGDisplayCreateUUIDFromDisplayID(uint display);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFUUIDCreateString(nint allocator, nint uuid);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(nint value, byte[] buffer, nint capacity, uint encoding);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);

    public string? GetDisplayUuid(uint displayId)
    {
        var uuid = CGDisplayCreateUUIDFromDisplayID(displayId);
        if (uuid == 0) return null;
        nint text = 0;
        try
        {
            text = CFUUIDCreateString(0, uuid);
            var buffer = new byte[128];
            if (text == 0 || !CFStringGetCString(text, buffer, buffer.Length, 0x08000100)) return null;
            var end = Array.IndexOf(buffer, (byte)0);
            return end > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, end) : null;
        }
        finally { if (text != 0) CFRelease(text); CFRelease(uuid); }
    }

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
