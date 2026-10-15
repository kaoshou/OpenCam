// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Capture;

public static class WindowsWindowCaptureExclusion
{
    // Earlier Windows versions interpret this as WDA_MONITOR (a black rectangle),
    // not exclusion. Never reveal the advisory on those versions.
    public static bool TryApply(nint window)
    {
        if (window == 0 || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        const uint excludeFromCapture = 0x11;
        if (DwmIsCompositionEnabled(out var composed) != 0 || !composed ||
            !SetWindowDisplayAffinity(window, excludeFromCapture) ||
            !GetWindowDisplayAffinity(window, out var actual) || actual != excludeFromCapture)
        {
            Log.Warning("Cannot confirm advisory capture exclusion; overlay remains invisible (Win32 {Error})",
                Marshal.GetLastWin32Error());
            return false;
        }
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}
