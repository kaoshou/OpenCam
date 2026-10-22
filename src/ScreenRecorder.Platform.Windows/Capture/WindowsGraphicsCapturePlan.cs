// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed record GraphicsCaptureMonitor(ulong Handle, string DeviceName, CaptureRegion Bounds);

public sealed class WindowsGraphicsCapturePlan(ICapturePlanProvider compatible,
    Func<CancellationToken, Task<bool>> available,
    Func<CaptureRegion, GraphicsCaptureMonitor?>? resolve = null) : ICapturePlanProvider
{
    public async Task<CaptureLaunchPlan> PrepareAsync(RecordingConfiguration config, CaptureRegion bounds,
        CaptureSelection? pinned, CancellationToken token)
    {
        if (!config.NativeCaptureEnabled && pinned?.Backend != CaptureBackend.WindowsGraphicsCapture)
            return await compatible.PrepareAsync(config, bounds, pinned, token);
        if (pinned is not null && pinned.Backend != CaptureBackend.WindowsGraphicsCapture)
            throw new InvalidOperationException("Capture backend cannot change during a recording.");
        if (!await available(token))
            throw new NotSupportedException("This FFmpeg build does not support Windows Graphics Capture. Select compatible capture.");
        token.ThrowIfCancellationRequested();
        var monitor = (resolve ?? Resolve)(bounds)
            ?? throw new NotSupportedException("Windows Graphics Capture requires a region within one available display.");
        return Build(config, bounds, monitor, pinned);
    }

    public static CaptureLaunchPlan Build(RecordingConfiguration config, CaptureRegion bounds,
        GraphicsCaptureMonitor monitor, CaptureSelection? pinned = null)
    {
        if (config.Fps is < 1 or > 120 || !WindowsCapturePlanner.Valid(bounds) ||
            bounds.Width % 2 != 0 || bounds.Height % 2 != 0 || monitor.Handle == 0 ||
            !WindowsCapturePlanner.Contains(monitor.Bounds, bounds))
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var selection = new CaptureSelection(CaptureBackend.WindowsGraphicsCapture, CaptureFallbackReason.None,
            monitor.DeviceName, null, null, bounds) { OutputBounds = monitor.Bounds };
        if (pinned is not null && pinned != selection)
            throw new InvalidOperationException("The original capture display is no longer available.");
        int left = bounds.X - monitor.Bounds.X, top = bounds.Y - monitor.Bounds.Y;
        int right = monitor.Bounds.Width - left - bounds.Width, bottom = monitor.Bounds.Height - top - bounds.Height;
        // gfxcapture preserves source PTS. The compatibility encoder interface currently
        // accepts software surfaces; retain explicit hwdownload rather than claim zero-copy.
        var input = FormattableString.Invariant($"-f lavfi -i \"gfxcapture=hmonitor={monitor.Handle}:max_framerate={config.Fps}:capture_cursor={(config.CursorEffect == CursorEffectMode.Hidden ? 0 : 1)}:crop_left={left}:crop_top={top}:crop_right={right}:crop_bottom={bottom}:width={bounds.Width}:height={bounds.Height}:output_fmt=bgra,hwdownload,format=bgra\" ");
        return new(selection, input);
    }

    public string BuildInputArguments(CaptureLaunchPlan plan, RecordingConfiguration config, bool microphone,
        string? system, string? mic) => compatible.BuildInputArguments(plan, config, microphone, system, mic);

    public static async Task<bool> ProbeAsync(string ffmpeg, CancellationToken token, IEncoderProbeRunner? runner = null)
    {
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-h", "filter=gfxcapture" }) start.ArgumentList.Add(arg);
        var result = await (runner ?? new EncoderProbeRunner()).RunAsync(start, TimeSpan.FromSeconds(3), token);
        if (!result.CleanupCompleted) throw new InvalidOperationException("WGC probe did not terminate.");
        token.ThrowIfCancellationRequested();
        if (result.Status == EncoderProbeStatus.Cancelled) throw new OperationCanceledException(token);
        var help = result.StdoutTail + result.StderrTail;
        return result.Status == EncoderProbeStatus.Available && result.ExitCode == 0 &&
            new[] { "Filter gfxcapture", "hmonitor", "max_framerate", "capture_cursor", "crop_left",
                "crop_top", "crop_right", "crop_bottom", "width", "height", "output_fmt" }.All(help.Contains);
    }

    public static GraphicsCaptureMonitor? Resolve(CaptureRegion bounds)
    {
        if (!OperatingSystem.IsWindows() || !WindowsCapturePlanner.Valid(bounds)) return null;
        var rect = new Rect { Left = bounds.X, Top = bounds.Y,
            Right = checked(bounds.X + bounds.Width), Bottom = checked(bounds.Y + bounds.Height) };
        var handle = MonitorFromRect(ref rect, 0);
        if (handle == 0) return null;
        var info = new Info { Size = Marshal.SizeOf<Info>(), Device = "" };
        if (!GetMonitorInfo(handle, ref info)) return null;
        var screen = new CaptureRegion(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        return WindowsCapturePlanner.Contains(screen, bounds)
            ? new(unchecked((ulong)handle.ToInt64()), info.Device, screen) : null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Info
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref Rect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref Info info);
}
