// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Recorder.Services;

/// <summary>Never borrows the formal recorder's singleton audio capture services.</summary>
public sealed class PreflightCaptureFactory(IFFmpegPlatformProvider provider, IEncoderSelectionService encoders,
    ICapturePlanProvider? capturePlan = null)
{
    public PreflightCaptureResources Create()
    {
        ISystemAudioLoopbackCapture? system = null;
        IMicrophoneCapture? microphone = null;
        IMicrophoneLevelObserver? observer = null;
        if (OperatingSystem.IsWindows())
        {
            system = new WindowsWasapiLoopbackCapture();
            observer = new WindowsMicrophoneLevelObserver(new WindowsAudioDeviceService());
        }
        else if (OperatingSystem.IsMacOS())
        {
            system = new MacOsAudioLoopbackCapture();
            microphone = new MacOsMicrophoneCapture();
        }
        else throw new PlatformNotSupportedException("Preflight capture requires Windows or macOS.");
        var engine = new FFmpegScreenRecorderEngine(provider, system, microphoneCapture: microphone,
            microphoneLevelObserver: observer, encoderSelectionService: encoders, capturePlanProvider: capturePlan);
        return new(engine, [..new IAsyncDisposable?[] { system, microphone, observer }.OfType<IAsyncDisposable>()]);
    }
}

public sealed class PreflightCaptureResources(IScreenRecorderEngine engine, IReadOnlyList<IAsyncDisposable> sources) : IAsyncDisposable
{
    public IScreenRecorderEngine Engine { get; } = engine;
    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync();
        foreach (var source in sources) await source.DisposeAsync();
    }
}
