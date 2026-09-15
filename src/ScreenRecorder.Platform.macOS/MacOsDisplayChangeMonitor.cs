using System;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

public class MacOsDisplayChangeMonitor : IDisplayChangeMonitor
{
#pragma warning disable CS0067
    public event EventHandler? DisplayChanged;
#pragma warning restore CS0067

    public void Start() { }
    public void Stop() { }
    
    public void Dispose() { }
}
