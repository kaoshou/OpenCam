// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using Serilog;

namespace ScreenRecorder.Platform.macOS;

internal sealed class MacOsNativeVideoCapture(Process process, CaptureSelection selection,
    bool editablePointer = false, ILogger? diagnosticLogger = null) : INativeVideoCapture, INativePointerCapture
{
    private readonly NativePointerProtocol? _pointerProtocol = editablePointer
        ? new(selection.Bounds.Width, selection.Bounds.Height) : null;
    private readonly ILogger _log = diagnosticLogger ?? Log.Logger;
    private bool _pointerIncomplete;
    private Task? _stderr;
    private volatile bool _stopping;
    private readonly object _stopLock = new();
    private Task? _stopTask;
    public event EventHandler<string>? CaptureFailed;
    public event EventHandler<NativePointerEvent>? PointerMetadata;
    public event EventHandler<string>? PointerIncomplete;
    public Stream Output => process.StandardOutput.BaseStream;
    public CaptureSelection Selection => selection;

    public static async Task<INativeVideoCapture> PrepareAsync(RecordingConfiguration config,
        CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var helper = Path.Combine(AppContext.BaseDirectory, "OpenCam.ScreenCapture");
        if (!File.Exists(helper)) throw new FileNotFoundException("ScreenCaptureKit helper is missing; use compatible capture.", helper);
        if (config.EncoderType is not (HardwareEncoderType.Auto or HardwareEncoderType.AppleVideoToolbox))
            throw new NotSupportedException("ScreenCaptureKit requires Auto or Apple VideoToolbox encoding.");
        var displays = new CoreGraphicsDisplayApi().GetActiveDisplays();
        var matches = displays.Where(d => d.Width > 0 && d.PixelWidth > 0 && d.PixelHeight > 0).Select(d =>
        {
            var scale = (double)d.PixelWidth / d.Width;
            var area = new CaptureRegion((int)Math.Round(d.X * scale), (int)Math.Round(d.Y * scale),
                (int)d.PixelWidth, (int)d.PixelHeight);
            return (Display: d, Bounds: area);
        }).Where(d => bounds.X >= d.Bounds.X && bounds.Y >= d.Bounds.Y &&
            (long)bounds.X + bounds.Width <= (long)d.Bounds.X + d.Bounds.Width &&
            (long)bounds.Y + bounds.Height <= (long)d.Bounds.Y + d.Bounds.Height).ToArray();
        if (matches.Length != 1 || bounds.Width <= 0 || bounds.Height <= 0 ||
            bounds.Width % 2 != 0 || bounds.Height % 2 != 0 || config.Fps is < 1 or > 120)
            throw new NotSupportedException("ScreenCaptureKit requires an even-sized region within one display.");
        var match = matches[0];
        var selection = new CaptureSelection(CaptureBackend.ScreenCaptureKit, CaptureFallbackReason.None,
            match.Display.Id.ToString(CultureInfo.InvariantCulture), null, null, bounds) { OutputBounds = match.Bounds };
        if (pinned is not null && pinned != selection)
            throw new InvalidOperationException("The original capture display changed.");
        var start = BuildStartInfo(helper, config, match.Display.Id, bounds, match.Bounds);
        var process = Process.Start(start) ?? throw new IOException("Cannot start ScreenCaptureKit helper.");
        return await PrepareProcessAsync(process, selection, config.EditablePointerEnabled, token);
    }

    internal static ProcessStartInfo BuildStartInfo(string helper, RecordingConfiguration config,
        long displayId, CaptureRegion bounds, CaptureRegion displayBounds)
    {
        // Validate metadata geometry before launching a child that would otherwise
        // be left waiting for its start command when the parser cannot support it.
        if (config.EditablePointerEnabled) _ = new NativePointerProtocol(bounds.Width, bounds.Height);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new long[] { displayId, bounds.X - displayBounds.X, bounds.Y - displayBounds.Y,
            bounds.Width, bounds.Height, displayBounds.Width, displayBounds.Height, config.Fps,
            config.VideoBitrateKbps, config.CursorEffect == CursorEffectMode.Hidden ? 0 : 1 })
            start.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
        if (config.EditablePointerEnabled) start.ArgumentList.Add("1");
        return start;
    }

    internal static async Task<MacOsNativeVideoCapture> PrepareProcessAsync(Process process,
        CaptureSelection selection, bool editablePointer, CancellationToken token)
    {
        var capture = new MacOsNativeVideoCapture(process, selection, editablePointer);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var ready = await process.StandardError.ReadLineAsync(deadline.Token);
            var expected = editablePointer ? "READY h264-matroska-clock-v1" : "READY h264-matroska-v1";
            if (ready != expected)
            {
                // Native preparation failures contain no pointer records. An
                // unexpected metadata line must never enter exception/log text.
                var detail = ready?.StartsWith("ERROR ", StringComparison.Ordinal) == true
                    ? ready[..Math.Min(ready.Length, 2048)] : "incompatible or unavailable native protocol";
                throw new IOException("ScreenCaptureKit initialization failed: " + detail);
            }
            capture._stderr = capture.DrainAsync();
            return capture;
        }
        catch
        {
            try { await capture.StopAsync(); }
            catch (Exception cleanup) { Log.Error(cleanup, "ScreenCaptureKit initialization cleanup failed"); }
            throw;
        }
    }

    public async Task BeginAsync(CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync("start".AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
    }

    private async Task DrainAsync()
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                HandleDiagnosticLine(line);
            if (!_stopping) CaptureFailed?.Invoke(this, "ScreenCaptureKit video producer ended unexpectedly.");
        }
        catch (Exception ex)
        {
            if (!_stopping) CaptureFailed?.Invoke(this, "ScreenCaptureKit diagnostics failed: " + ex.Message);
        }
    }

    internal void HandleDiagnosticLine(string line)
    {
        if (line.StartsWith("POINTER", StringComparison.Ordinal) || line.StartsWith("FRAME_CLOCK", StringComparison.Ordinal))
        {
            // Metadata is private media data, including malformed/error records.
            // Never forward raw records to Serilog or an exception message.
            if (_pointerProtocol is null || _pointerIncomplete) return;
            try
            {
                if (_pointerProtocol.Parse(line) is { } value) PointerMetadata?.Invoke(this, value);
            }
            catch (Exception)
            {
                _pointerIncomplete = true;
                try { PointerIncomplete?.Invoke(this, "Editable pointer metadata is invalid or incomplete."); }
                catch (Exception) { _log.Warning("Editable pointer failure subscriber could not accept the incomplete status."); }
            }
            return;
        }
        _log.Information("ScreenCaptureKit: {Diagnostic}", line.Length <= 2048 ? line : line[..2048]);
    }

    public Task StopAsync()
    {
        lock (_stopLock)
        {
            _stopping = true;
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    await process.StandardInput.WriteLineAsync("stop".AsMemory(), deadline.Token);
                    await process.StandardInput.FlushAsync(deadline.Token);
                    await process.WaitForExitAsync(deadline.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                    Log.Warning(ex, "ScreenCaptureKit graceful stop failed; terminating helper.");
                    if (!process.HasExited) process.Kill(true);
                    using var join = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await process.WaitForExitAsync(join.Token);
                }
            }
            if (_stderr is not null) await _stderr;
            if (process.ExitCode != 0)
                throw new IOException($"ScreenCaptureKit helper exited with code {process.ExitCode}; retain the working recording for recovery.");
        }
        finally { process.Dispose(); }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
