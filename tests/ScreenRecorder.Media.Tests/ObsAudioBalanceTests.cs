using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public class ObsAudioBalanceTests
{
    [Theory]
    [InlineData(100, 100, 1)]
    [InlineData(25, 100, 2)]
    [InlineData(0, 0, 2)]
    public void NonDefaultGainRequiresProtocolThatOldHelpersReject(int system, int mic, int protocol)
    {
        var config = new RecordingConfiguration { SystemAudioVolumePercent = system, MicrophoneVolumePercent = mic };
        var request = ObsCaptureRequest.Create(config, new(0, 0, 640, 480),
            new MonitorInfo(0, "display", new(0, 0, 1920, 1080), true, 1), HardwareEncoderType.SoftwareCpu, false);
        var json = JsonSerializer.Deserialize<JsonElement>(request.Serialize());
        Assert.Equal(protocol, json.GetProperty("protocol").GetInt32());
        Assert.True(json.TryGetProperty("systemAudioVolumePercent", out var s));
        Assert.True(json.TryGetProperty("microphoneVolumePercent", out var m));
        Assert.Equal(system, s.GetInt32()); Assert.Equal(mic, m.GetInt32());
        Assert.Equal("both", request.Audio);
    }
}
