// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using Serilog;

namespace ScreenRecorder.Recorder.Services;

public partial class RecordingOrchestrator
{
    private async Task PrepareCursorDecorationAsync(RecordingConfiguration config, CancellationToken token)
    {
        StopCursorDecoration();
        config.CursorEffect = _requestedCursorEffect;
        if (_requestedCursorEffect is CursorEffectMode.Default or CursorEffectMode.Hidden)
        {
            Volatile.Write(ref _cursorEffectHealth, 0);
            return;
        }
        try
        {
            if (_requestedCursorEffect == CursorEffectMode.PresentationStick)
            {
                if (_cursorHighlightService is IPresentationPointerOverlay overlay &&
                    await overlay.StartPresentationAsync(config.PresentationPointer, token).ConfigureAwait(false) &&
                    _cursorHighlightService.IsRunning)
                {
                    Volatile.Write(ref _cursorEffectHealth, 1);
                    return;
                }
                config.CursorEffect = CursorEffectMode.Default;
                StopCursorDecoration();
            }
            else
            {
                _cursorHighlightService?.Start(_requestedCursorEffect);
                if (_cursorHighlightService?.IsRunning == true)
                {
                    Volatile.Write(ref _cursorEffectHealth, 1);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StopCursorDecoration();
            throw;
        }
        catch (Exception error)
        {
            config.CursorEffect = CursorEffectMode.Default;
            StopCursorDecoration();
            Log.Warning("Optional cursor effect startup failed: {ErrorType}", error.GetType().Name);
        }
        Volatile.Write(ref _cursorEffectHealth, 2);
        Log.Warning("Cursor effect unavailable; recording uses the normal cursor and unchanged selected audio.");
    }

    private void OnCursorOverlayFailed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _cursorHighlightService)) return;
        Volatile.Write(ref _cursorEffectHealth, 2);
        Log.Warning("Live cursor effect lost; video/audio recording continues without restarting capture.");
    }

    private bool? GetCursorEffectHealth()
    {
        if (_stateMachine.CurrentState == RecordingState.Recording &&
            Volatile.Read(ref _cursorEffectHealth) == 1 && _cursorHighlightService?.IsRunning != true)
            Volatile.Write(ref _cursorEffectHealth, 2);
        return Volatile.Read(ref _cursorEffectHealth) switch { 1 => true, 2 => false, _ => null };
    }

    private void StopCursorDecoration()
    {
        try { _cursorHighlightService?.Stop(); }
        catch (Exception error)
        {
            Volatile.Write(ref _cursorEffectHealth, 2);
            Log.Warning("Optional cursor effect cleanup unconfirmed: {ErrorType}", error.GetType().Name);
        }
    }
}
