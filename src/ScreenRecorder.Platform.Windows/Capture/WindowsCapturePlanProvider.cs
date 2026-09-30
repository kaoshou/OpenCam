// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using Serilog;

namespace ScreenRecorder.Platform.Windows.Capture;

public sealed class WindowsCapturePlanProvider(WindowsFFmpegProvider provider, IDxgiOutputCatalog catalog,
    Func<IReadOnlyList<MonitorInfo>> monitors, Func<CancellationToken, Task<bool>> filterAvailable) : ICapturePlanProvider
{
    public async Task<CaptureLaunchPlan> PrepareAsync(RecordingConfiguration config, CaptureRegion bounds,
        CaptureSelection? pinned, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (config.Fps is < 1 or > 120 || !WindowsCapturePlanner.Valid(bounds) || bounds.Width % 2 != 0 || bounds.Height % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid capture dimensions or frame rate.");
        if (pinned != null && pinned.Bounds != bounds) throw new InvalidOperationException("Capture bounds changed during this session.");
        if (pinned?.Backend == CaptureBackend.Gdi ||
            (pinned == null && config.WindowsCaptureMode == WindowsCaptureMode.CompatibleGdi))
            return Gdi(pinned ?? new(CaptureBackend.Gdi, CaptureFallbackReason.None, null, null, null, bounds), config);

        var available = await filterAvailable(token).ConfigureAwait(false);
        IReadOnlyList<DxgiOutputInfo> outputs = [];
        IReadOnlyList<MonitorInfo> currentMonitors = [];
        if (available)
        {
            try { outputs = catalog.GetOutputs(); currentMonitors = monitors(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { Log.Warning(ex, "Cannot map Desktop Duplication output; compatible capture required."); }
        }
        // A pinned device may have a different UI enumeration index on resume.
        var selectionConfig = config;
        if (pinned?.Backend == CaptureBackend.DesktopDuplication)
        {
            selectionConfig = new() { WindowsCaptureMode = WindowsCaptureMode.ModernExperimental,
                CaptureSource = CaptureSourceType.CustomRegion };
        }
        var selection = WindowsCapturePlanner.Select(selectionConfig, bounds, currentMonitors, outputs, available);
        if (pinned != null && (selection.Backend != pinned.Backend || selection.DeviceName != pinned.DeviceName
                || selection.AdapterLuid != pinned.AdapterLuid || selection.OutputBounds != pinned.OutputBounds))
            throw new InvalidOperationException("The original capture output is no longer available.");
        if (selection.Backend == CaptureBackend.Gdi) return Gdi(selection, config);
        var output = outputs.Single(o => o.AdapterLuid == selection.AdapterLuid && o.OutputIndex == selection.OutputIndex);
        var x = checked(bounds.X - output.Bounds.X);
        var y = checked(bounds.Y - output.Bounds.Y);
        var mouse = config.CursorEffect == CursorEffectMode.Hidden ? 0 : 1;
        var input = FormattableString.Invariant(
            $"-f lavfi -i \"ddagrab=output_idx={selection.OutputIndex}:framerate={config.Fps}:draw_mouse={mouse}:video_size={bounds.Width}x{bounds.Height}:offset_x={x}:offset_y={y}:output_fmt=bgra:dup_frames=1,hwdownload,format=bgra\" ");
        return new(selection, input);
    }

    private static CaptureLaunchPlan Gdi(CaptureSelection selection, RecordingConfiguration config)
    {
        var b = selection.Bounds;
        var mouse = config.CursorEffect == CursorEffectMode.Hidden ? 0 : 1;
        return new(selection, FormattableString.Invariant(
            $"-rtbufsize 100M -f gdigrab -draw_mouse {mouse} -framerate {config.Fps} -offset_x {b.X} -offset_y {b.Y} -video_size {b.Width}x{b.Height} -i desktop "));
    }

    public string BuildInputArguments(CaptureLaunchPlan plan, RecordingConfiguration config, bool hasDirectShowMic,
        string? systemAudioPipeArg, string? microphoneAudioPipeArg) =>
        provider.BuildAudioInputArguments(plan.VideoInputArguments, config, hasDirectShowMic, systemAudioPipeArg);
}
