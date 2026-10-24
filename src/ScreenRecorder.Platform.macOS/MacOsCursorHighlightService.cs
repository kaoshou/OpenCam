// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using Serilog;

namespace ScreenRecorder.Platform.macOS;

public sealed class MacOsCursorHighlightService : ICursorHighlightService, IPresentationPointerOverlay
{
    private readonly string _helperPath;
    private Process? _helperProcess;
    private bool _isDisposed;
    private volatile bool _stopping;
    private CursorEffectMode _mode;
    private TeachingPointerOptions _presentationPointer = TeachingPointerOptions.Default;
    public event EventHandler? OverlayFailed;

    public MacOsCursorHighlightService()
        : this(MacOsCursorSupport.HelperPath(AppContext.BaseDirectory))
    {
    }

    internal MacOsCursorHighlightService(string helperPath)
    {
        _helperPath = helperPath;
    }

    public bool IsRunning
    {
        get { try { return _helperProcess is { HasExited: false }; } catch (InvalidOperationException) { return false; } }
    }

    public async Task<bool> StartPresentationAsync(TeachingPointerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _presentationPointer = TeachingPointerOptions.Normalize(options);
        Start(CursorEffectMode.PresentationStick);
        var process = _helperProcess;
        if (process is null || _mode != CursorEffectMode.PresentationStick) return false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            if (ready == $"READY stick-v2 {_presentationPointer.Style} {_presentationPointer.Size}" && IsRunning) return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Stop();
            throw;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        { Log.Warning("Presentation overlay readiness could not be confirmed: {ErrorType}", error.GetType().Name); }
        Stop();
        return false;
    }

    public void Start(CursorEffectMode mode)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (mode is CursorEffectMode.Default or CursorEffectMode.Hidden ||
            !OperatingSystem.IsMacOS() ||
            !File.Exists(_helperPath))
        {
            return;
        }

        Stop();
        if (IsRunning) return; // An unconfirmed shutdown never permits a second overlay.
        _mode = mode;
        _stopping = false;

        var startInfo = new ProcessStartInfo
        {
            FileName = _helperPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(
            mode switch
            {
                CursorEffectMode.HaloWithClickRipple => "ripple",
                CursorEffectMode.PresentationStick => "stick",
                _ => "halo"
            });

        if (mode == CursorEffectMode.PresentationStick)
        {
            startInfo.ArgumentList.Add("--stick-style");
            startInfo.ArgumentList.Add(_presentationPointer.Style.ToString());
            startInfo.ArgumentList.Add("--stick-size");
            startInfo.ArgumentList.Add(_presentationPointer.Size.ToString());
        }
        try
        {
            _helperProcess = Process.Start(startInfo);
            if (_helperProcess is { } process)
            {
                process.Exited += OnExited;
                process.EnableRaisingEvents = true;
            }
        }
        catch (Exception error)
        {
            // Cursor decoration is optional. A damaged or blocked helper must
            // never abort a screen recording that is already running.
            Log.Warning("Optional cursor overlay could not start: {ErrorType}", error.GetType().Name);
            Stop();
        }
    }

    private void OnExited(object? sender, EventArgs args)
    {
        if (_stopping || !ReferenceEquals(sender, _helperProcess)) return;
        Log.Warning("Optional cursor overlay exited; video/audio recording is unchanged.");
        try { OverlayFailed?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { Log.Warning("Cursor overlay warning subscriber failed: {ErrorType}", error.GetType().Name); }
    }

    public void Stop()
    {
        var process = _helperProcess;
        if (process is null) return;
        _stopping = true;
        var exited = false;
        try
        {
            if (!process.HasExited)
            {
                try { process.StandardInput.WriteLine("stop"); process.StandardInput.Close(); }
                catch (Exception error) when (error is IOException or InvalidOperationException) { }
                if (!process.WaitForExit(1000))
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(2000))
                    {
                        Log.Warning("Cursor overlay shutdown unconfirmed; another overlay will not start.");
                        return;
                    }
                }
            }
            exited = true;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Log.Warning("Cursor overlay cleanup could not be confirmed: {ErrorType}", error.GetType().Name); }
        finally
        {
            if (exited)
            {
                if (ReferenceEquals(_helperProcess, process)) _helperProcess = null;
                process.Exited -= OnExited;
                process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
    }
}
