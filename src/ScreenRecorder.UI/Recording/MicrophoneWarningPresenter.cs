// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.UI.Recording;

internal interface ICaptureExcludedWarning : IDisposable
{
    void PrepareInvisible();
    bool TryExcludeFromCapture();
    void Reveal();
}

internal sealed class MicrophoneWarningPresenter(Func<ICaptureExcludedWarning?> create) : IDisposable
{
    private ICaptureExcludedWarning? _window;
    private bool _attempted;

    public bool Update(bool warning)
    {
        if (!warning) { Dispose(); return false; }
        if (_attempted) return _window is not null;
        _attempted = true;
        ICaptureExcludedWarning? candidate = null;
        try
        {
            candidate = create();
            if (candidate is null) return false;
            candidate.PrepareInvisible();
            if (!candidate.TryExcludeFromCapture()) return false;
            candidate.Reveal();
            _window = candidate;
            candidate = null;
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Microphone advisory overlay unavailable; recording unchanged");
            return false;
        }
        finally { Close(candidate); }
    }

    public void Dispose()
    {
        var window = _window;
        _window = null;
        _attempted = false;
        Close(window);
    }

    private static void Close(ICaptureExcludedWarning? window)
    {
        try { window?.Dispose(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Cannot close microphone advisory overlay"); }
    }
}
