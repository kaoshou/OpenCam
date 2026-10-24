// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Windows.Capture;
using Serilog;

[assembly: InternalsVisibleTo("ScreenRecorder.Media.Tests")]

namespace ScreenRecorder.Platform.Windows;

internal sealed class WindowsNativeVideoCapture : INativeVideoCapture, INativePointerCapture
{
    private readonly Process _process;
    private readonly NativePointerProtocol _protocol;
    private readonly CancellationTokenSource _drainCancellation = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly object _stopLock = new();
    private readonly char[] _line = new char[256], _character = new char[1];
    private Task? _stderr, _stopTask;
    private volatile bool _stopping;
    private bool _begun;
    private int _metadataDisabled, _failed;
    private int _disposed;

    private WindowsNativeVideoCapture(Process process, CaptureSelection selection)
    {
        _process = process; Selection = selection;
        _protocol = new(selection.Bounds.Width, selection.Bounds.Height);
    }

    public Stream Output => _process.StandardOutput.BaseStream;
    public CaptureSelection Selection { get; }
    public event EventHandler<string>? CaptureFailed;
    public event EventHandler<NativePointerEvent>? PointerMetadata;
    public event EventHandler<string>? PointerIncomplete;

    public static async Task<INativeVideoCapture> PrepareAsync(RecordingConfiguration config,
        CaptureRegion bounds, CaptureSelection? pinned, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var helper = Path.Combine(AppContext.BaseDirectory, "OpenCam.ScreenCapture.exe");
        if (!File.Exists(helper)) throw new FileNotFoundException("Windows native video helper is missing; use compatible capture.", helper);
        var monitor = ResolvePhysicalMonitor(bounds)
            ?? throw new NotSupportedException("Windows native capture requires a region within one available display.");
        var launch = BuildStartInfo(helper, config, bounds, monitor, pinned);
        return await PrepareProcessAsync(launch.Start, launch.Selection, token).ConfigureAwait(false);
    }

    internal static GraphicsCaptureMonitor? ResolvePhysicalMonitor(CaptureRegion bounds,
        Func<CaptureRegion, GraphicsCaptureMonitor?>? resolve = null, Func<nint, nint>? setAwareness = null)
    {
        // The recorder process may have a different DPI default from the UI.
        // Keep this scope synchronous so the original thread context is restored.
        var set = setAwareness ?? SetThreadDpiAwarenessContext;
        var previous = set(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        if (previous == 0) throw new InvalidOperationException("Cannot resolve native capture in physical pixels.");
        try { return (resolve ?? WindowsGraphicsCapturePlan.Resolve)(bounds); }
        finally
        {
            if (set(previous) == 0) throw new InvalidOperationException("Cannot restore native capture DPI context.");
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    internal static (ProcessStartInfo Start, CaptureSelection Selection) BuildStartInfo(string helper,
        RecordingConfiguration config, CaptureRegion bounds, GraphicsCaptureMonitor monitor, CaptureSelection? pinned = null)
    {
        if (config.Fps is < 1 or > 120 || !WindowsCapturePlanner.Valid(bounds) ||
            bounds.Width < 2 || bounds.Height < 2 || bounds.Width > 16384 || bounds.Height > 16384 ||
            bounds.Width % 2 != 0 || bounds.Height % 2 != 0 || (long)bounds.Width * bounds.Height > 16_777_216 ||
            monitor.Handle == 0 || monitor.Handle > long.MaxValue || string.IsNullOrEmpty(monitor.DeviceName) ||
            monitor.Bounds.Width > 32768 || monitor.Bounds.Height > 32768 ||
            !WindowsCapturePlanner.Contains(monitor.Bounds, bounds))
            throw new ArgumentOutOfRangeException(nameof(bounds), "Invalid native capture dimensions, display, or frame rate.");
        var selection = new CaptureSelection(CaptureBackend.WindowsGraphicsCapture, CaptureFallbackReason.None,
            monitor.DeviceName, null, null, bounds) { OutputBounds = bounds };
        if (pinned is not null && pinned != selection)
            throw new InvalidOperationException("The original native capture display changed.");
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new long[] { (long)monitor.Handle, (long)bounds.X - monitor.Bounds.X,
            (long)bounds.Y - monitor.Bounds.Y, bounds.Width, bounds.Height,
            monitor.Bounds.Width, monitor.Bounds.Height, config.Fps })
            start.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
        return (start, selection);
    }

    internal static async Task<WindowsNativeVideoCapture> PrepareProcessAsync(ProcessStartInfo start,
        CaptureSelection selection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var process = Process.Start(start) ?? throw new IOException("Cannot start Windows native video helper.");
        var capture = new WindowsNativeVideoCapture(process, selection);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            var ready = await capture.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (ready.Overflow || ready.Text != "READY bgra-matroska-clock-v1")
                throw new IOException("Windows native video helper initialization failed.");
            capture._stderr = capture.DrainAsync();
            return capture;
        }
        catch
        {
            try { await capture.DisposeAsync().ConfigureAwait(false); }
            catch { Log.Error("Windows native video helper initialization cleanup failed."); }
            throw;
        }
    }

    public async Task BeginAsync(CancellationToken token)
    {
        await _commands.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_stopping || _begun) throw new InvalidOperationException("Native video capture cannot begin in its current state.");
            _begun = true;
            await _process.StandardInput.WriteLineAsync("start".AsMemory(), token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }
        finally { _commands.Release(); }
    }

    // Keep even a malformed/non-newline helper record bounded. Consume the rest
    // of an oversized record so its tail cannot be mistaken for fresh metadata.
    private async Task<(string? Text, bool Overflow)> ReadLineAsync(CancellationToken token)
    {
        var count = 0; var overflow = false;
        while (true)
        {
            var read = await _process.StandardError.ReadAsync(_character.AsMemory(), token).ConfigureAwait(false);
            if (read == 0 && count == 0 && !overflow) return (null, false);
            if (read == 0 || _character[0] == '\n')
            {
                if (count > 0 && _line[count - 1] == '\r') --count;
                return (new string(_line, 0, count), overflow);
            }
            if (count < _line.Length) _line[count++] = _character[0];
            else overflow = true;
        }
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                var record = await ReadLineAsync(_drainCancellation.Token).ConfigureAwait(false);
                if (record.Text is not { } line) break;
                if (record.Overflow || line.StartsWith("POINTER_ERROR", StringComparison.Ordinal))
                {
                    Incomplete();
                    continue;
                }
                if (line.StartsWith("ERROR", StringComparison.Ordinal))
                {
                    Incomplete(); Failed();
                    continue;
                }
                if (Volatile.Read(ref _metadataDisabled) != 0) continue;
                try
                {
                    if (_protocol.Parse(line) is { } metadata) PointerMetadata?.Invoke(this, metadata);
                }
                catch { Incomplete(); }
                // Native metadata and arbitrary helper text never enter application logs.
            }
            if (!_stopping) { Incomplete(); Failed(); }
        }
        catch (Exception) when (_stopping) { }
        catch { Incomplete(); Failed(); }
    }

    private void Incomplete()
    {
        if (Interlocked.Exchange(ref _metadataDisabled, 1) != 0) return;
        try { PointerIncomplete?.Invoke(this, "Windows native pointer metadata is incomplete."); }
        catch { Log.Error("Windows native pointer failure notification failed."); }
    }

    private void Failed()
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0) return;
        try { CaptureFailed?.Invoke(this, "Windows native video producer ended unexpectedly."); }
        catch { Log.Error("Windows native capture failure notification failed."); }
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
            if (!_process.HasExited)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var commandLock = false;
                try
                {
                    await _commands.WaitAsync(deadline.Token).ConfigureAwait(false);
                    commandLock = true;
                    await _process.StandardInput.WriteLineAsync("stop".AsMemory(), deadline.Token).ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
                    await _process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
                {
                    if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                    using var join = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _process.WaitForExitAsync(join.Token).ConfigureAwait(false);
                }
                finally { if (commandLock) _commands.Release(); }
            }
            if (_stderr is not null)
            {
                try { await _stderr.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    _drainCancellation.Cancel();
                    await _stderr.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    throw new IOException("Windows native video diagnostics did not close within the stop deadline.");
                }
            }
            if (_process.ExitCode != 0)
                throw new IOException("Windows native video helper did not exit successfully; retain the working recording for recovery.");
        }
        finally
        {
            _drainCancellation.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _process.Dispose();
                _drainCancellation.Dispose();
            }
        }
    }
}
