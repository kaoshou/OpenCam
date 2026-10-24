// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Windows.Display;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed class WindowsCaptureHealthMonitor(IDxgiOutputCatalog catalog, Func<bool>? desktopAvailable = null,
    Func<CaptureRegion, GraphicsCaptureMonitor?>? nativeResolve = null) : ICaptureHealthMonitor
{
    public bool Check(CaptureSelection selection)
    {
        if (selection.Backend is not (CaptureBackend.DesktopDuplication or CaptureBackend.WindowsGraphicsCapture)) return true;
        try
        {
            if (!(desktopAvailable ?? IsInputDesktopAvailable)()) return false;
            if (selection.RecordingCore == "libobs-v1")
            {
                var monitor = (nativeResolve ?? ResolveNative)(selection.Bounds);
                return monitor is not null && string.Equals(monitor.DeviceName, selection.DeviceName, StringComparison.OrdinalIgnoreCase)
                    && monitor.Bounds == selection.OutputBounds && WindowsCapturePlanner.Contains(monitor.Bounds, selection.Bounds);
            }
            if (selection.Backend == CaptureBackend.WindowsGraphicsCapture)
            {
                var monitor = WindowsGraphicsCapturePlan.Resolve(selection.Bounds);
                return monitor is not null && monitor.DeviceName == selection.DeviceName &&
                    monitor.Bounds == selection.OutputBounds;
            }
            var matches = catalog.GetOutputs().Where(o => o.AdapterLuid == selection.AdapterLuid
                && o.OutputIndex == selection.OutputIndex && o.DeviceName == selection.DeviceName
                && o.Attached && o.IdentityRotation && o.IsDefaultAdapter
                && (selection.OutputBounds == null || o.Bounds == selection.OutputBounds)
                && WindowsCapturePlanner.Contains(o.Bounds, selection.Bounds)).ToArray();
            return matches.Length == 1;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Native capture output health check failed.");
            return false;
        }
    }

    private static GraphicsCaptureMonitor? ResolveNative(CaptureRegion bounds)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var displays = new WindowsDisplayService();
        var matches = displays.GetMonitors().Where(monitor => WindowsCapturePlanner.Contains(monitor.Bounds, bounds)).ToArray();
        if (matches.Length != 1) return null;
        var identity = displays.GetRecordingDisplayIdentity(matches[0]);
        return identity is null ? null : new GraphicsCaptureMonitor(0, identity, matches[0].Bounds);
    }

    private static bool IsInputDesktopAvailable()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var desktop = OpenInputDesktop(0, false, 0x0001); // DESKTOP_READOBJECTS
        if (desktop == 0) return false;
        try
        {
            var inputName = new StringBuilder(256);
            var threadName = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, inputName, 512, out _)
                && GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, threadName, 512, out _)
                && string.Equals(inputName.ToString(), threadName.ToString(), StringComparison.Ordinal);
        }
        finally { CloseDesktop(desktop); }
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("user32.dll")]
    private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(nint desktop);
}
