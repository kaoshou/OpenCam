// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Recorder.Services;
using ScreenRecorder.Infrastructure.IPC;
using ScreenRecorder.UI.ViewModels;

namespace ScreenRecorder.Media.Tests;

public sealed class AudioLevelsLifecycleTests
{
    [RealRecorderFact]
    public async Task RealRecorder_WhenExplicitlyConfigured_ReturnsMeasuredMicrophoneState()
    {
        var pipe = Environment.GetEnvironmentVariable("OPENCAM_REAL_PIPE");
        if (string.IsNullOrWhiteSpace(pipe)) return;
        await using var client = new NamedPipeIpcClient(pipe);
        var response = await client.SendCommandAsync("GetAudioLevels", new { }, timeoutMs: 1500);
        Assert.True(response.Success, response.ErrorMessage);
        Assert.True(AudioLevelsResponseParser.TryParse(response.ErrorMessage, out var snapshot), response.ErrorMessage);
        var expectingMicrophone = Environment.GetEnvironmentVariable("OPENCAM_EXPECT_MIC") == "1";
        Assert.True(expectingMicrophone
                ? snapshot.Microphone.State is AudioMeterState.Silent or AudioMeterState.Live
                : snapshot.Microphone.State == AudioMeterState.Off,
            response.ErrorMessage);
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly AudioLevelSample Live = new(0.6, 0.8, Now);

    [Theory]
    [InlineData(RecordingState.Idle)]
    [InlineData(RecordingState.Stopping)]
    [InlineData(RecordingState.Completed)]
    [InlineData(RecordingState.Failed)]
    public void NonActiveSession_ReportsBothSourcesOff(RecordingState state)
    {
        var snapshot = RecordingAudioLevelResolver.Resolve(
            AudioSourceType.SystemAndMicrophone, state, Live, Live, Now);
        Assert.Equal(AudioMeterState.Off, snapshot.SystemAudio.State);
        Assert.Equal(AudioMeterState.Off, snapshot.Microphone.State);
    }

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
