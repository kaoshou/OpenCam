// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Localization;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.UI.Services;

internal static class CaptureStatusFormatter
{
    internal static string Format(CaptureSelection? selection, ILocalizationService strings)
    {
        var name = selection?.Backend switch
        {
            CaptureBackend.Gdi => "GDI",
            CaptureBackend.DesktopDuplication => "Desktop Duplication",
            CaptureBackend.WindowsGraphicsCapture => "Windows Graphics Capture",
            CaptureBackend.ScreenCaptureKit => "ScreenCaptureKit",
            CaptureBackend.X11 => "OBS X11",
            CaptureBackend.PipeWire => "OBS PipeWire / Portal",
            _ => null
        };
        if (name == null) return strings["ActualCapturePending"];
        var reason = selection!.FallbackReason switch
        {
            CaptureFallbackReason.FilterUnavailable => "CaptureFallbackFilter",
            CaptureFallbackReason.MappingUncertain => "CaptureFallbackMapping",
            CaptureFallbackReason.UnsupportedTopology => "CaptureFallbackTopology",
            CaptureFallbackReason.StartupFailed => "CaptureFallbackStartup",
            CaptureFallbackReason.AdapterIdentityUncertain => "CaptureFallbackAdapter",
            _ => null
        };
        return strings.GetFormatted("ActualCaptureFormat", name) +
            (reason == null ? "" : " · " + strings[reason]);
    }
}
