using System;
using System.Collections.Generic;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public class MacOsDisplayService : IDisplayService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        // 簡易實作：預設回傳一個螢幕
        return new List<MonitorInfo>
        {
            new MonitorInfo(1, "Mac Display", new CaptureRegion(0, 0, 1920, 1080), true, 1.0)
        };
    }

    public MonitorInfo? GetPrimaryMonitor()
    {
        var monitors = GetMonitors();
        return monitors.Count > 0 ? monitors[0] : null;
    }

    public CaptureRegion GetVirtualScreenBounds()
    {
        return new CaptureRegion(0, 0, 1920, 1080);
    }
}
