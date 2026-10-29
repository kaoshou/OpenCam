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
    public static string GetRuntimeDirectory(string executableDirectory) =>
        RecordingBuildProfile.NativeOnlyTest && OperatingSystem.IsMacOS()
            ? Path.GetFullPath(Path.Combine(executableDirectory, "..", "Resources", "obs-runtime"))
            : Path.Combine(executableDirectory, "obs-runtime");

    public IScreenRecorderEngine Create(HardwareEncoderType? pinnedEncoder, CaptureSelection? pinnedCapture = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var runtime = ObsRuntime.TryLoad(GetRuntimeDirectory(AppContext.BaseDirectory));
        Serilog.Log.Information("Recording startup stage: Stage={Stage}, ElapsedMs={ElapsedMs}, Available={Available}",
            "runtime_factory_validation", clock.Elapsed.TotalMilliseconds, runtime is not null);
        if (OperatingSystem.IsLinux())
        {
            if (runtime is null || displayService is null)
                throw new IOException("Linux recording core is missing or damaged; use the complete Linux test package.");
            return new ScreenRecorder.Platform.Linux.LinuxRecordingEngine(runtime, displayService, pinnedCapture);
        }
#if OPENCAM_OBS_ONLY_TEST
        if (runtime is null)
            throw new IOException("The recording core is missing or damaged. Recording did not start; reinstall the complete application package.");
        if (OperatingSystem.IsWindows())
            WindowsRecordingPrerequisites.Validate(name => File.Exists(Path.Combine(runtime.Root, name)));
        return new ObsRecordingEngineRouter(runtime,
            () => throw new IOException("The legacy recording core is not available in this build."),
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
