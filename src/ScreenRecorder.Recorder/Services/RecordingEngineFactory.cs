// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Recorder.Services;

public interface IRecordingEngineFactory
{
    IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null);
}

public sealed class RecordingEngineFactory(
    IFFmpegPlatformProvider provider, IEncoderSelectionService encoderSelectionService,
    ISystemAudioLoopbackCapture? systemAudioLoopbackCapture = null,
    IMicrophoneCapture? microphoneCapture = null,
    IMicrophoneLevelObserver? microphoneLevelObserver = null,
    ICapturePlanProvider? capturePlanProvider = null,
    IDisplayService? displayService = null,
    Func<MonitorInfo, string?>? nativeIdentity = null) : IRecordingEngineFactory
{
    public IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null)
    {
        var runtime = ObsRuntime.TryLoad(Path.Combine(AppContext.BaseDirectory, "obs-runtime"));
#if OPENCAM_OBS_ONLY_TEST
        if (runtime is null)
            throw new IOException("Native-only test: verified recording runtime is missing or damaged; recording did not start.");
        return new ObsRecordingEngineRouter(runtime,
            () => throw new IOException("Native-only test does not include a compatibility recording core."),
            (config, bounds, capture) => displayService is null ? null : ObsRecordingEngineRouter.ResolveMonitor(config,
                bounds, capture, displayService.GetMonitors(), nativeIdentity ?? ResolveIdentity),
            pinnedEncoder ?? HardwareEncoderType.SoftwareCpu, pinnedCapture, requireNative: true);
#else
        return new ObsRecordingEngineRouter(runtime, () => new FFmpegScreenRecorderEngine(
            provider, systemAudioLoopbackCapture, microphoneCapture: microphoneCapture,
            microphoneLevelObserver: microphoneLevelObserver,
            encoderSelectionService: encoderSelectionService, pinnedEncoder: pinnedEncoder,
            capturePlanProvider: capturePlanProvider,
            pinnedCapture: pinnedCapture is { RecordingCore: "ffmpeg" } ? pinnedCapture with { RecordingCore = null } : pinnedCapture),
            (config, bounds, capture) => displayService is null ? null : ObsRecordingEngineRouter.ResolveMonitor(config,
                bounds, capture, displayService.GetMonitors(), nativeIdentity ?? ResolveIdentity),
            pinnedEncoder ?? HardwareEncoderType.SoftwareCpu, pinnedCapture);
#endif
    }

    private string? ResolveIdentity(MonitorInfo monitor) => displayService is IRecordingDisplayIdentityService native
        ? native.GetRecordingDisplayIdentity(monitor)
        : null;
}
