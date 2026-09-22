// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Recorder.Services;

namespace ScreenRecorder.Media.Tests;

public sealed class AudioLevelsLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly AudioLevelSample Live = new(0.6, 0.8, Now);

    [Theory]
    [InlineData(AudioSourceType.None, AudioMeterState.Off, AudioMeterState.Off)]
    [InlineData(AudioSourceType.SystemOnly, AudioMeterState.Live, AudioMeterState.Off)]
    [InlineData(AudioSourceType.MicrophoneOnly, AudioMeterState.Off, AudioMeterState.Live)]
    [InlineData(AudioSourceType.SystemAndMicrophone, AudioMeterState.Live, AudioMeterState.Live)]
    public void ResolvesIndependentEnabledSources(AudioSourceType source, AudioMeterState system, AudioMeterState microphone)
    {
        var snapshot = RecordingAudioLevelResolver.Resolve(source, RecordingState.Recording, Live, Live, Now);
        Assert.Equal(system, snapshot.SystemAudio.State);
        Assert.Equal(microphone, snapshot.Microphone.State);
    }

    [Fact]
    public void PauseSettingsAndStop_DoNotCarryPreviousSample()
    {
        var paused = RecordingAudioLevelResolver.Resolve(AudioSourceType.SystemAndMicrophone,
            RecordingState.Paused, Live, Live, Now);
        Assert.Equal(AudioMeterState.Paused, paused.SystemAudio.State);
        Assert.Equal(AudioMeterState.Paused, paused.Microphone.State);

        var resumed = RecordingAudioLevelResolver.Resolve(AudioSourceType.MicrophoneOnly,
            RecordingState.Recording, null, Live, Now);
        Assert.Equal(AudioMeterState.Off, resumed.SystemAudio.State);
        Assert.Equal(AudioMeterState.Live, resumed.Microphone.State);

        var stopped = RecordingAudioLevelResolver.Resolve(AudioSourceType.None,
            RecordingState.Completed, null, null, Now);
        Assert.Equal(AudioMeterState.Off, stopped.SystemAudio.State);
        Assert.Equal(AudioMeterState.Off, stopped.Microphone.State);
    }

    [Fact]
    public void JsonRoundTripPreservesIndependentStates()
    {
        var snapshot = RecordingAudioLevelResolver.Resolve(AudioSourceType.SystemAndMicrophone,
            RecordingState.Recording, Live, null, Now);
        var restored = JsonSerializer.Deserialize<AudioLevelsSnapshot>(JsonSerializer.Serialize(snapshot));
        Assert.Equal(snapshot, restored);
        Assert.Equal(AudioMeterState.Unavailable, restored.Microphone.State);
    }
}
