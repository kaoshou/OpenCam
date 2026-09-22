// SPDX-License-Identifier: AGPL-3.0-or-later
namespace ScreenRecorder.Core.Models;

public enum AudioMeterState
{
    Off,
    Live,
    Silent,
    Paused,
    Unavailable
}

public readonly record struct AudioLevelSample(double Rms, double Peak, DateTimeOffset CapturedAt);

public readonly record struct AudioSourceLevel(AudioMeterState State, double Rms, double Peak);

public readonly record struct AudioLevelsSnapshot(AudioSourceLevel SystemAudio, AudioSourceLevel Microphone);
