using System.Diagnostics;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

public sealed class MacOsCursorHighlightService : ICursorHighlightService
{
    private readonly string _helperPath;
    private Process? _helperProcess;
    private bool _isDisposed;

    public MacOsCursorHighlightService()
        : this(MacOsCursorSupport.HelperPath(AppContext.BaseDirectory))
    {
    }

    internal MacOsCursorHighlightService(string helperPath)
    {
        _helperPath = helperPath;
    }

    public bool IsRunning =>
        _helperProcess is { HasExited: false };

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

        var startInfo = new ProcessStartInfo
        {
            FileName = _helperPath,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(
            mode == CursorEffectMode.HaloWithClickRipple
                ? "ripple"
                : "halo");

        _helperProcess = Process.Start(startInfo);
    }

    public void Stop()
    {
        if (_helperProcess is null)
        {
            return;
        }

        try
        {
            if (!_helperProcess.HasExited)
            {
                _helperProcess.Kill(entireProcessTree: true);
                _helperProcess.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _helperProcess.Dispose();
            _helperProcess = null;
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
