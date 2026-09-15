using System;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

public class MacOsCursorHighlightService : ICursorHighlightService
{
    public bool IsRunning => false;

    public void Start(CursorEffectMode mode) { }
    public void Stop() { }
    
    public void Dispose() { }
}
