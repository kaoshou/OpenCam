// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Media.Capture;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed class WindowsCaptureHealthMonitor(IDxgiOutputCatalog catalog, Func<bool>? desktopAvailable = null) : ICaptureHealthMonitor
{
    public bool Check(CaptureSelection selection)
    {
        if (selection.Backend != CaptureBackend.DesktopDuplication) return true;
        try
        {
            if (!(desktopAvailable ?? IsInputDesktopAvailable)()) return false;
            var matches = catalog.GetOutputs().Where(o => o.AdapterLuid == selection.AdapterLuid
                && o.OutputIndex == selection.OutputIndex && o.DeviceName == selection.DeviceName
                && o.Attached && o.IdentityRotation && o.IsDefaultAdapter
                && (selection.OutputBounds == null || o.Bounds == selection.OutputBounds)
                && WindowsCapturePlanner.Contains(o.Bounds, selection.Bounds)).ToArray();
            return matches.Length == 1;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Desktop Duplication output health check failed.");
            return false;
        }
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
