using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public class MacOsDisplayService : IDisplayService
{
    private readonly IMacOsDisplayApi _displayApi;

    public MacOsDisplayService()
        : this(new CoreGraphicsDisplayApi())
    {
    }

    internal MacOsDisplayService(IMacOsDisplayApi displayApi)
    {
        _displayApi = displayApi;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var displays = _displayApi.GetActiveDisplays();
        if (displays.Count == 0)
        {
            return new[]
            {
                new MonitorInfo(
                    0,
                    "Mac Display 1",
                    new CaptureRegion(0, 0, 1920, 1080),
                    true,
                    1.0)
            };
        }

        return displays.Select((display, index) =>
        {
            var scale = display.Width > 0
                ? (double)display.PixelWidth / display.Width
                : 1.0;
            var bounds = new CaptureRegion(
                (int)Math.Round(display.X * scale),
                (int)Math.Round(display.Y * scale),
                (int)display.PixelWidth,
                (int)display.PixelHeight);

            return new MonitorInfo(
                index,
                "Mac Display " + (index + 1),
                bounds,
                display.IsPrimary,
                scale);
        }).ToArray();
    }

    public MonitorInfo? GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(monitor => monitor.IsPrimary) ??
               monitors.FirstOrDefault();
    }

    internal uint? GetNativeDisplayId(int monitorIndex)
    {
        var displays = _displayApi.GetActiveDisplays();
        if (monitorIndex >= 0 && monitorIndex < displays.Count)
        {
            return displays[monitorIndex].Id;
        }

        var primary = displays.FirstOrDefault(display => display.IsPrimary);
        if (primary.Id != 0)
        {
            return primary.Id;
        }

        return displays.Count > 0 ? displays[0].Id : null;
    }

    public CaptureRegion GetVirtualScreenBounds()
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return new CaptureRegion(0, 0, 1920, 1080);
        }

        var left = monitors.Min(monitor => monitor.Bounds.X);
        var top = monitors.Min(monitor => monitor.Bounds.Y);
        var right = monitors.Max(monitor => monitor.Bounds.X + monitor.Bounds.Width);
        var bottom = monitors.Max(monitor => monitor.Bounds.Y + monitor.Bounds.Height);
        return new CaptureRegion(left, top, right - left, bottom - top);
    }
}
