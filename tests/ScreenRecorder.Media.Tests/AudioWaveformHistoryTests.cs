// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.ViewModels;
using System.Text.Json;

namespace ScreenRecorder.Media.Tests;

public sealed class AudioWaveformHistoryTests
{
    [Fact]
    public void ValidRecorderSnapshot_ParsesForUi()
    {
        var original = new AudioLevelsSnapshot(
            new AudioSourceLevel(AudioMeterState.Off, 0, 0),
            new AudioSourceLevel(AudioMeterState.Silent, 0, 0.1));
        Assert.True(AudioLevelsResponseParser.TryParse(JsonSerializer.Serialize(original), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"SystemAudio\":{\"State\":1,\"Rms\":0.5,\"Peak\":0.7}}")]
    [InlineData("{\"SystemAudio\":{\"State\":99,\"Rms\":0.5,\"Peak\":0.7},\"Microphone\":{\"State\":0,\"Rms\":0,\"Peak\":0}}")]
    public void MalformedIpcPayload_IsRejected(string payload)
    {
        Assert.False(AudioLevelsResponseParser.TryParse(payload, out _));
    }

    [Fact]
    public void SourcesRemainIndependentAndCapacityIsBounded()
    {
        var system = new AudioWaveformHistory(24);
        var microphone = new AudioWaveformHistory(24);
        system.Push(new AudioSourceLevel(AudioMeterState.Live, 0.6, 0.8));
        Assert.Equal(0.6, system.Values[^1]);
        Assert.All(microphone.Values, value => Assert.Equal(0, value));

        for (var i = 0; i < 30; i++)
        {
            microphone.Push(new AudioSourceLevel(AudioMeterState.Live, i / 30.0, 1));
        }
        Assert.Equal(24, microphone.Values.Count);
        Assert.Equal(0.6, system.Values[^1]);
    }

    [Theory]
    [InlineData(AudioMeterState.Off)]
    [InlineData(AudioMeterState.Paused)]
    [InlineData(AudioMeterState.Unavailable)]
    public void NonLiveStatesClearOldMovement(AudioMeterState state)
    {
        var history = new AudioWaveformHistory(24);
        history.Push(new AudioSourceLevel(AudioMeterState.Live, 0.9, 1));
        history.Push(new AudioSourceLevel(state, 0, 0));
        Assert.All(history.Values, value => Assert.Equal(0, value));
    }

    [Fact]
    public void SilentAndClearNeverFabricateNonzeroBars()
    {
        var history = new AudioWaveformHistory(24);
        history.Push(new AudioSourceLevel(AudioMeterState.Silent, 0, 0));
        Assert.All(history.Values, value => Assert.Equal(0, value));
        history.Push(new AudioSourceLevel(AudioMeterState.Live, 0.7, 0.8));
        history.Clear();
        Assert.All(history.Values, value => Assert.Equal(0, value));
    }
}
