using System;
using System.Threading;
using System.Threading.Tasks;
using ScreenRecorder.Core.Interfaces;

namespace ScreenRecorder.Platform.macOS;

public class MacOsAudioLoopbackCapture : ISystemAudioLoopbackCapture
{
    public bool IsSupported => false;
    public bool IsCapturing => false;

    public event EventHandler<string>? AudioErrorOccurred
    {
        add { }
        remove { }
    }

    public Task<SystemAudioCaptureInfo?> StartCaptureAsync(
        int monitorIndex,
        CancellationToken cancellationToken = default)
    {
        _ = monitorIndex;
        return Task.FromResult<SystemAudioCaptureInfo?>(null);
    }

    public Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
