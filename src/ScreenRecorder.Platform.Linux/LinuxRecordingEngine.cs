// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
namespace ScreenRecorder.Platform.Linux;

/// <summary>Linux endpoint normalization around the shared, owned-output OBS engine.</summary>
public sealed class LinuxRecordingEngine : IScreenRecorderEngine, IBoundRecordingOutput, IRecordingAudioLevelSource
{
    private readonly ObsScreenRecorderEngine inner;
    public LinuxRecordingEngine(ObsRuntime runtime, IDisplayService displays, CaptureSelection? pinned)
    {
        inner = new(runtime.HelperPath, runtime.Root, (config, bounds, capture) => ObsRecordingEngineRouter.ResolveMonitor(
            config, bounds, capture, displays.GetMonitors(), m => (displays as IRecordingDisplayIdentityService)?.GetRecordingDisplayIdentity(m)),
            HardwareEncoderType.SoftwareCpu, pinned, TimeSpan.FromSeconds(70), allowCompatibilityFallback: false);
    }
    public Stream? OutputStream { get => inner.OutputStream; set => inner.OutputStream = value; }
    public bool UseSyntheticCaptureSource { get => inner.UseSyntheticCaptureSource; set => inner.UseSyntheticCaptureSource = value; }
    public bool IsRunning => inner.IsRunning;
    public TimeSpan CurrentRecordedTime => inner.CurrentRecordedTime;
    public long CurrentFramesRecorded => inner.CurrentFramesRecorded;
    public HardwareEncoderType ActiveEncoder => inner.ActiveEncoder;
    public CaptureSelection? ActiveCapture => inner.ActiveCapture;
    public event EventHandler<string>? EngineErrorOccurred { add => inner.EngineErrorOccurred += value; remove => inner.EngineErrorOccurred -= value; }
    public event EventHandler<string>? EngineWarningOccurred { add => inner.EngineWarningOccurred += value; remove => inner.EngineWarningOccurred -= value; }
    public event EventHandler? AudioDeviceLost { add => inner.AudioDeviceLost += value; remove => inner.AudioDeviceLost -= value; }
    public Task StartRecordingAsync(string path, RecordingConfiguration config, CaptureRegion bounds, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || config.EditablePointerEnabled || config.CursorEffect is not (CursorEffectMode.Default or CursorEffectMode.Hidden))
            throw new NotSupportedException("This Linux test build supports the original or hidden cursor; teaching-pointer overlays are not yet validated.");
        var c = JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(config))!;
        if (c.AudioSource != AudioSourceType.None && !c.IsRecoverySilenceMode) {
            var audio = LinuxDesktopCatalog.ReadAudio();
            if (c.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone) c.SystemAudioDeviceId = audio.ResolveSystem(c.SystemAudioDeviceId);
            if (c.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone) c.MicrophoneDeviceId = audio.ResolveMicrophone(c.MicrophoneDeviceId);
        }
        return inner.StartRecordingAsync(path, c, bounds, cancellationToken);
    }
    public Task StopRecordingAsync(CancellationToken cancellationToken = default) => inner.StopRecordingAsync(cancellationToken);
    public (AudioLevelSample? SystemAudio, AudioLevelSample? Microphone) ReadInputLevels(DateTimeOffset now) => inner.ReadInputLevels(now);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
