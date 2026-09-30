// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows.Capture;

public static class WindowsCapturePlanner
{
    public static CaptureSelection Select(RecordingConfiguration config, CaptureRegion normalizedBounds,
        IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<DxgiOutputInfo> outputs, bool filterAvailable)
    {
        CaptureSelection Gdi(CaptureFallbackReason reason) =>
            new(CaptureBackend.Gdi, reason, null, null, null, normalizedBounds);
        if (config.WindowsCaptureMode != WindowsCaptureMode.ModernExperimental) return Gdi(CaptureFallbackReason.None);
        if (!filterAvailable) return Gdi(CaptureFallbackReason.FilterUnavailable);
        if (!Valid(normalizedBounds)) return Gdi(CaptureFallbackReason.MappingUncertain);
        var containing = monitors.Where(m => Contains(m.Bounds, normalizedBounds)).ToArray();
        if (containing.Length != 1) return Gdi(CaptureFallbackReason.UnsupportedTopology);
        var monitor = containing[0];
        if (config.CaptureSource == CaptureSourceType.Monitor && monitor.Index != config.MonitorIndex)
            return Gdi(CaptureFallbackReason.MappingUncertain);
        var matches = outputs.Where(o => o.DeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase)
            && o.Bounds == monitor.Bounds).ToArray();
        if (matches.Length != 1) return Gdi(CaptureFallbackReason.MappingUncertain);
        var output = matches[0];
        if (!output.Attached || !output.IdentityRotation || !output.IsDefaultAdapter || output.OutputIndex < 0)
            return Gdi(CaptureFallbackReason.UnsupportedTopology);
        return new(CaptureBackend.DesktopDuplication, CaptureFallbackReason.None, output.DeviceName,
            output.AdapterLuid, output.OutputIndex, normalizedBounds);
    }

    internal static bool Valid(CaptureRegion r) => r.IsValid
        && (long)r.X + r.Width <= int.MaxValue && (long)r.Y + r.Height <= int.MaxValue;

    internal static bool Contains(CaptureRegion outer, CaptureRegion inner) =>
        Valid(outer) && Valid(inner) && inner.X >= outer.X && inner.Y >= outer.Y
        && (long)inner.X + inner.Width <= (long)outer.X + outer.Width
        && (long)inner.Y + inner.Height <= (long)outer.Y + outer.Height;
}
