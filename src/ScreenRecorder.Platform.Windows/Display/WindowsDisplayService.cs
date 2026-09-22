// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.Windows.Display;

using ScreenRecorder.Core.Interfaces;
public class WindowsDisplayService : IDisplayService
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public POINTL dmPosition;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    private const int ENUM_CURRENT_SETTINGS = -1;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        int index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                int x = mi.rcMonitor.Left;
                int y = mi.rcMonitor.Top;
                int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                // 透過 EnumDisplaySettings 獲取不受 Windows DPI 縮放虛擬化影響的物理真實解析度與座標
                if (!string.IsNullOrEmpty(mi.szDevice))
                {
                    var dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    if (EnumDisplaySettings(mi.szDevice, ENUM_CURRENT_SETTINGS, ref dm))
                    {
                        x = dm.dmPosition.x;
                        y = dm.dmPosition.y;
                        width = dm.dmPelsWidth;
                        height = dm.dmPelsHeight;
                    }
                }

                var bounds = new CaptureRegion(x, y, width, height);

                list.Add(new MonitorInfo(index, mi.szDevice ?? $"Monitor {index + 1}", bounds, isPrimary, 1.0));
                index++;
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            // Fallback
            int w = GetSystemMetrics(0); // SM_CXSCREEN
            int h = GetSystemMetrics(1); // SM_CYSCREEN
            list.Add(new MonitorInfo(0, "Default Display", new CaptureRegion(0, 0, w > 0 ? w : 1920, h > 0 ? h : 1080), true, 1.0));
        }

        return list;
    }

    public MonitorInfo? GetPrimaryMonitor()
    {
        return GetMonitors().FirstOrDefault(m => m.IsPrimary) ?? GetMonitors().FirstOrDefault();
    }

    public CaptureRegion GetVirtualScreenBounds()
    {
        var monitors = GetMonitors();
        if (monitors.Count > 0)
        {
            int minX = monitors.Min(m => m.Bounds.X);
            int minY = monitors.Min(m => m.Bounds.Y);
            int maxX = monitors.Max(m => m.Bounds.X + m.Bounds.Width);
            int maxY = monitors.Max(m => m.Bounds.Y + m.Bounds.Height);
            return new CaptureRegion(minX, minY, maxX - minX, maxY - minY);
        }

        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (w <= 0 || h <= 0)
        {
            var primary = GetPrimaryMonitor();
            return primary?.Bounds ?? new CaptureRegion(0, 0, 1920, 1080);
        }

        return new CaptureRegion(x, y, w, h);
    }
}
