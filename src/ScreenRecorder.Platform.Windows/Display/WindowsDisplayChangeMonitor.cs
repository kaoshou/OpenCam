// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.Versioning;
using Microsoft.Win32;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.Windows.Display;

[SupportedOSPlatform("windows")]
public class WindowsDisplayChangeMonitor : IDisplayChangeMonitor
{
    private bool _isMonitoring;
    private bool _disposed;
    private readonly object _lock = new();

    public event EventHandler? DisplayChanged;

    public void Start()
    {
        lock (_lock)
        {
            if (_isMonitoring || _disposed) return;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _isMonitoring = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isMonitoring) return;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _isMonitoring = false;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
        GC.SuppressFinalize(this);
    }
}
