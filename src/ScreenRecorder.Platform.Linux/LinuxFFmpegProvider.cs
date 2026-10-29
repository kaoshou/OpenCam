// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Preflight;
namespace ScreenRecorder.Platform.Linux;

/// <summary>Audio confirmation only; never a video-recording fallback.</summary>
public sealed class LinuxFFmpegProvider : IFFmpegPlatformProvider, IPreflightAudioPlatformProvider
{
    public IEnumerable<(string EncoderName, string ExtraArgs, HardwareEncoderType Type)> GetHardwareEncoderProbes() => [];
    public string BuildInputArguments(RecordingConfiguration c, int x, int y, int w, int h, bool synthetic, bool mic, string? sys = null, string? input = null) =>
        throw new NotSupportedException("Linux recording requires the bundled OBS core; no FFmpeg video fallback is available.");
    public string BuildOutputArguments(RecordingConfiguration c, HardwareEncoderType encoder, string output) =>
        throw new NotSupportedException("Linux recording requires the bundled OBS core.");
    public string BuildPreflightAudioInputArguments(RecordingConfiguration c, bool synthetic, string? system, string? microphone)
    {
        if (synthetic) throw new NotSupportedException("Synthetic audio cannot confirm a Linux device.");
        var audio = LinuxDesktopCatalog.ReadAudio();
        return PreflightAudioFormat.BuildInput(c, false,
            c.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone ? PulseInput(audio.ResolveSystem(c.SystemAudioDeviceId)) : null,
            c.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone ? PulseInput(audio.ResolveMicrophone(c.MicrophoneDeviceId)) : null, mac: false);
    }
    internal static string PulseInput(string id)
    {
        if (id.Length is < 1 or > 512 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || "._-:/@".Contains(c))))
            throw new IOException("Unsupported PulseAudio device identity.");
        return $"-thread_queue_size 1024 -f pulse -sample_rate 48000 -channels 2 -i \"{id}\"";
    }
}

// These adapters validate audio endpoints; FFmpeg owns the actual preflight
// streams. Formal recording always uses OBS sources, not these adapters.
public sealed class LinuxSystemAudioCapture : ISystemAudioLoopbackCapture
{
    public bool IsSupported => OperatingSystem.IsLinux(); public bool IsCapturing { get; private set; }
    public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
    public Task<SystemAudioCaptureInfo?> StartCaptureAsync(int monitorIndex, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested(); var id = LinuxDesktopCatalog.ReadAudio().ResolveSystem("default"); IsCapturing = true;
        return Task.FromResult<SystemAudioCaptureInfo?>(new("", 48000, 2, LinuxFFmpegProvider.PulseInput(id)));
    }
    public Task StopCaptureAsync(CancellationToken cancellationToken = default) { IsCapturing = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { IsCapturing = false; return ValueTask.CompletedTask; }
}
public sealed class LinuxMicrophoneCapture : IMicrophoneCapture
{
    public bool IsSupported => OperatingSystem.IsLinux(); public bool IsCapturing { get; private set; }
    public event EventHandler<string>? AudioErrorOccurred { add { } remove { } }
    public Task<MicrophoneCaptureInfo?> StartCaptureAsync(string? deviceId, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested(); var id = LinuxDesktopCatalog.ReadAudio().ResolveMicrophone(deviceId); IsCapturing = true;
        return Task.FromResult<MicrophoneCaptureInfo?>(new("", 48000, 2, LinuxFFmpegProvider.PulseInput(id)));
    }
    public Task StopCaptureAsync(CancellationToken cancellationToken = default) { IsCapturing = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() { IsCapturing = false; return ValueTask.CompletedTask; }
}
