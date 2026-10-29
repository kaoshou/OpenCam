// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Media.Preflight;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Recorder.Services;

public sealed class PreflightAudioCaptureFactory(IPreflightAudioPlatformProvider provider)
{
    public IPreflightAudioCapture Create()
    {
        if (OperatingSystem.IsWindows())
            return new FFmpegPreflightAudioCapture(provider,
                new WindowsWasapiLoopbackCapture { RequireConfirmedShutdown = true },
                observer: new WindowsMicrophoneLevelObserver(new WindowsAudioDeviceService()) { RequireConfirmedShutdown = true });
        if (OperatingSystem.IsMacOS())
            return new FFmpegPreflightAudioCapture(provider,
                new MacOsAudioLoopbackCapture { RequireConfirmedShutdown = true },
                new MacOsMicrophoneCapture { RequireConfirmedShutdown = true });
        if (OperatingSystem.IsLinux())
            return new FFmpegPreflightAudioCapture(provider,
                new ScreenRecorder.Platform.Linux.LinuxSystemAudioCapture(),
                new ScreenRecorder.Platform.Linux.LinuxMicrophoneCapture());
        throw new PlatformNotSupportedException("Audio confirmation requires Windows or macOS.");
    }
}
