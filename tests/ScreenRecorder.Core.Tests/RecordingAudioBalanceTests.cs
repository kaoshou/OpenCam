using System.Text.Json;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public class RecordingAudioBalanceTests
{
    [Theory]
    [InlineData("{}", 100, 100)]
    [InlineData("{\"SystemAudioVolumePercent\":25,\"MicrophoneVolumePercent\":0}", 25, 0)]
    [InlineData("{\"SystemAudioVolumePercent\":-1,\"MicrophoneVolumePercent\":101}", 0, 100)]
    public void ConfigurationAndPreferencesNormalizeAndRoundTrip(string json, int system, int mic)
    {
        var configuration = JsonSerializer.Deserialize<RecordingConfiguration>(json)!;
        var settings = JsonSerializer.Deserialize<UserSettings>(json)!;
        AssertVolumes(JsonSerializer.SerializeToElement(configuration), system, mic);
        AssertVolumes(JsonSerializer.SerializeToElement(settings), system, mic);
        AssertVolumes(JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<RecordingConfiguration>(JsonSerializer.Serialize(configuration))), system, mic);
    }

    [Fact]
    public void ResumeUsesUpdatedVolumesWithoutChangingCaptureGeometry()
    {
        var configuration = new RecordingConfiguration { Fps = 20, MonitorIndex = 2 };
        var update = JsonSerializer.Deserialize<RecordingConfiguration>("{\"SystemAudioVolumePercent\":30,\"MicrophoneVolumePercent\":70}")!;
        configuration.ApplyPausedSettings(update);
        AssertVolumes(JsonSerializer.SerializeToElement(configuration), 30, 70);
        Assert.Equal(20, configuration.Fps);
        Assert.Equal(2, configuration.MonitorIndex);
    }

    private static void AssertVolumes(JsonElement value, int system, int mic)
    {
        Assert.True(value.TryGetProperty("SystemAudioVolumePercent", out var s), "System audio gain must be preserved in configuration snapshots");
        Assert.True(value.TryGetProperty("MicrophoneVolumePercent", out var m), "Microphone gain must be preserved in configuration snapshots");
        Assert.Equal(system, s.GetInt32()); Assert.Equal(mic, m.GetInt32());
    }
}
