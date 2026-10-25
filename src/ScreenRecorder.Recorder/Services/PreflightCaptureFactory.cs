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
    public PreflightCaptureResources Create(HardwareEncoderType? pinnedEncoder = null, CaptureSelection? pinnedCapture = null,
        bool livePreview = false)
    {
#if OPENCAM_OBS_ONLY_TEST
        throw new NotSupportedException("Recording preparation uses the audio-only check; video preflight is not available in this build.");
#else
        ISystemAudioLoopbackCapture? system = null;
        IMicrophoneCapture? microphone = null;
        IMicrophoneLevelObserver? observer = null;
        if (OperatingSystem.IsWindows())
        {
            system = new WindowsWasapiLoopbackCapture { RequireConfirmedShutdown = true };
            observer = new WindowsMicrophoneLevelObserver(new WindowsAudioDeviceService()) { RequireConfirmedShutdown = true };
        }
        else if (OperatingSystem.IsMacOS())
        {
            system = new MacOsAudioLoopbackCapture { RequireConfirmedShutdown = true };
            microphone = new MacOsMicrophoneCapture { RequireConfirmedShutdown = true };
        }
        else throw new PlatformNotSupportedException("Preflight capture requires Windows or macOS.");
        var engine = new FFmpegScreenRecorderEngine(provider, system, microphoneCapture: microphone,
            microphoneLevelObserver: observer, encoderSelectionService: encoders, capturePlanProvider: capturePlan,
            pinnedEncoder: pinnedEncoder, pinnedCapture: pinnedCapture, livePreviewOutput: livePreview);
        return new(engine, [..new IAsyncDisposable?[] { system, microphone, observer }.OfType<IAsyncDisposable>()]);
#endif
    }
}

public sealed class PreflightCaptureResources(IScreenRecorderEngine engine, IReadOnlyList<IAsyncDisposable> sources) : IAsyncDisposable
{
    public IScreenRecorderEngine Engine { get; } = engine;
    public async Task StopSourcesAsync(CancellationToken ct)
    {
        foreach (var source in sources)
        {
            if (source is ISystemAudioLoopbackCapture system) await system.StopCaptureAsync(ct);
            else if (source is IMicrophoneCapture microphone) await microphone.StopCaptureAsync(ct);
            else if (source is IMicrophoneLevelObserver observer) await observer.StopAsync(ct);
        }
    }
    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StopSourcesAsync(stop.Token);
        await Engine.DisposeAsync();
        foreach (var source in sources) await source.DisposeAsync();
    }
}
