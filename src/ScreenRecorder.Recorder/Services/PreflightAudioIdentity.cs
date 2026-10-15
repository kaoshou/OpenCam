// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Recorder.Services;

public static class PreflightAudioIdentity
{
    public static string Read(RecordingConfiguration configuration)
    {
        var microphone = configuration.AudioSource is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        var system = configuration.AudioSource is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        if (!microphone && !system) return "disabled";
        return OperatingSystem.IsWindows() ? WindowsAudioEndpointIdentity.Read(microphone, system)
            : OperatingSystem.IsMacOS() ? MacOsAudioEndpointIdentity.Read(microphone, system)
            : throw new PlatformNotSupportedException();
    }
}
