// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Media.Projects;

/// <summary>Shared straight-alpha RGBA compositor. The point is the tip, in output pixels.
/// Only the small pointer rectangle is visited; no per-frame image allocations.</summary>
public static class PointerRasterizer
{
    public static void Composite(Span<byte> rgba, int width, int height, PointerSample? point,
        ProjectPointerSettings settings, double scale = 1)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216 ||
            rgba.Length != (long)width * height * 4)
            throw new ArgumentException("Invalid RGBA frame dimensions.", nameof(rgba));
        ProjectPointers.Validate(settings);
        if (!double.IsFinite(scale) || scale <= 0 || scale > 32)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (point is not { } p) return;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
        if (!p.Visible || settings.Style == ProjectPointerStyle.Hidden) return;
        var unit = Math.Max(.1, height / 1080d * settings.SizePercent / 100d * scale);
        var extent = 60 * unit + 2;
        if (p.X + extent < 0 || p.Y + extent < 0 || p.X - extent >= width || p.Y - extent >= height) return;
        var left = (int)Math.Max(0, Math.Floor(p.X - extent));
        var top = (int)Math.Max(0, Math.Floor(p.Y - extent));
        var right = (int)Math.Min(width - 1, Math.Ceiling(p.X + extent));
        var bottom = (int)Math.Min(height - 1, Math.Ceiling(p.Y + extent));
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
        {
            // Four subpixel samples soften thin edges without blurring the hotspot.
            double red = 0, green = 0, blue = 0, coverage = 0;
            for (var sy = 0; sy < 2; sy++)
            for (var sx = 0; sx < 2; sx++)
            {
                var dx = (x + .25 + .5 * sx - p.X) / unit;
                var dy = (y + .25 + .5 * sy - p.Y) / unit;
                var color = ColorAt(settings.Style, dx, dy);
                if (color < 0) continue;
                red += (color >> 16) & 255;
                green += (color >> 8) & 255;
                blue += color & 255;
                coverage++;
            }
            if (coverage == 0) continue;
            var index = (y * width + x) * 4;
            var alpha = coverage / 4;
            var previousAlpha = rgba[index + 3] / 255d;
            var resultAlpha = alpha + previousAlpha * (1 - alpha);
            rgba[index] = Mix(red / coverage, rgba[index], alpha, previousAlpha, resultAlpha);
            rgba[index + 1] = Mix(green / coverage, rgba[index + 1], alpha, previousAlpha, resultAlpha);
            rgba[index + 2] = Mix(blue / coverage, rgba[index + 2], alpha, previousAlpha, resultAlpha);
            rgba[index + 3] = (byte)Math.Round(resultAlpha * 255);
        }
    }

    private static byte Mix(double source, byte destination, double alpha, double previousAlpha, double resultAlpha) =>
        (byte)Math.Clamp(Math.Round((source * alpha + destination * previousAlpha * (1 - alpha)) / resultAlpha), 0, 255);

    private static int ColorAt(ProjectPointerStyle style, double x, double y)
    {
        if (style == ProjectPointerStyle.Dot)
        {
            var distance = x * x + y * y;
            return distance <= 16 ? 0xE33B36 : distance <= 25 ? 0xFFFFFF : -1;
        }
        if (style == ProjectPointerStyle.Arrow)
        {
            // Tip at (0,0), slim arrow extending down/right, with a contrasting edge.
            if (x < 0 || y < 0 || y > 26 || x > y * .7) return -1;
            if (y > 17 && x < y * .7 - 5) return -1;
            return x < 1.2 || y * .7 - x < 1.2 || y > 24.8 ? 0xFFFFFF : 0x253244;
        }
        // A 56 px diagonal silver shaft and a short dark grip, at 1080p / 100%.
        var along = (x + y) / Math.Sqrt(2);
        var across = (y - x) / Math.Sqrt(2);
        if (along < 0 || along > 56) return -1;
        var halfWidth = along >= 43 ? 2.7 : 1.5;
        if (Math.Abs(across) > halfWidth) return -1;
        if (along < 3 || along >= 43) return across < -.6 ? 0x596170 : 0x252C38;
        if (Math.Abs(across) > 1) return 0x526078;
        return across < 0 ? 0xF2F7FF : 0xA9BDD4;
    }
}
