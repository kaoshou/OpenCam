// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Audio;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Recorder.Services;

public static class RecordingAudioLevelResolver
{
    public static AudioLevelsSnapshot Resolve(AudioSourceType source, RecordingState state,
        AudioLevelSample? systemSample, AudioLevelSample? microphoneSample, DateTimeOffset now)
    {
        if (state is not (RecordingState.Recording or RecordingState.Pausing or RecordingState.Paused))
        {
            source = AudioSourceType.None;
        }

        var systemEnabled = source is AudioSourceType.SystemOnly or AudioSourceType.SystemAndMicrophone;
        var microphoneEnabled = source is AudioSourceType.MicrophoneOnly or AudioSourceType.SystemAndMicrophone;
        return new AudioLevelsSnapshot(
            AudioLevelState.Resolve(systemEnabled, state, systemSample, now),
            AudioLevelState.Resolve(microphoneEnabled, state, microphoneSample, now));
    }
}
