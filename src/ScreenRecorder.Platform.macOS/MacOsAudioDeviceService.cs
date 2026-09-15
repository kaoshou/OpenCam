using System.Collections.Generic;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Platform.macOS;

public class MacOsAudioDeviceService : IAudioDeviceService
{
    public IReadOnlyList<AudioDeviceOption> GetRecordingDevices()
    {
        return new List<AudioDeviceOption>
        {
            new AudioDeviceOption("0", "MacBook Microphone (AVFoundation default)", true, true)
        };
    }

    public IReadOnlyList<AudioDeviceOption> GetPlaybackDevices()
    {
        return new List<AudioDeviceOption>();
    }
}
