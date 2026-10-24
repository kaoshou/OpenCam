// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsCaptureRequestTests
{
    private static readonly MonitorInfo Display = new(7, @"\\.\DISPLAY3", new(-1920, 0, 1920, 1080), false, 1.5);

    [Fact]
    public void PreservesNativeDisplayAndSelectedAudioIdentityInBoundedProtocol()
    {
        var config = new RecordingConfiguration { Fps = 60, AudioSource = AudioSourceType.SystemAndMicrophone,
            MicrophoneDeviceId = "@device_cm_{selected-moniker}", SystemAudioDeviceId = "selected-endpoint",
            CursorEffect = CursorEffectMode.Hidden, VideoQualityPreset = "Ultra" };
        var request = ObsCaptureRequest.Create(config, new(-1800, 100, 640, 480), Display,
            HardwareEncoderType.NvidiaNvenc, synthetic: false);
        using var json = JsonDocument.Parse(request.Serialize());
        var value = json.RootElement;
        Assert.Equal(1, value.GetProperty("protocol").GetInt32());
        Assert.Equal(@"\\.\DISPLAY3", value.GetProperty("monitor").GetString());
        Assert.Equal(120, value.GetProperty("cropX").GetInt32());
        Assert.Equal(100, value.GetProperty("cropY").GetInt32());
        Assert.Equal("@device_cm_{selected-moniker}", value.GetProperty("microphone").GetString());
        Assert.Equal("selected-endpoint", value.GetProperty("systemDevice").GetString());
        Assert.Equal("both", value.GetProperty("audio").GetString());
        Assert.Equal("nvenc", value.GetProperty("encoder").GetString());
        Assert.Equal(60, value.GetProperty("fps").GetInt32());
        Assert.Equal(18, value.GetProperty("quality").GetInt32());
        Assert.False(value.GetProperty("cursor").GetBoolean());
        Assert.False(value.TryGetProperty("outputPath", out _));
    }

    [Theory]
    [InlineData(0, 480)]
    [InlineData(641, 480)]
    [InlineData(640, -2)]
    public void RejectsGeometryInsteadOfSilentlyChangingRequestedResolution(int width, int height) =>
        Assert.Throws<ArgumentException>(() => ObsCaptureRequest.Create(new(), new(-1800, 0, width, height), Display,
            HardwareEncoderType.SoftwareCpu, false));

    [Fact]
    public void RejectsCrossDisplayRegionAndOversizedDeviceIdentity()
    {
        Assert.Throws<ArgumentException>(() => ObsCaptureRequest.Create(new(), new(-100, 0, 640, 480), Display,
            HardwareEncoderType.SoftwareCpu, false));
        var config = new RecordingConfiguration { MicrophoneDeviceId = new string('x', 70000) };
        Assert.Throws<ArgumentException>(() => ObsCaptureRequest.Create(config, new(-1800, 0, 640, 480), Display,
            HardwareEncoderType.SoftwareCpu, false).Serialize());
    }

    [Fact]
    public void DirectPresentationPointerOmitsTheSystemCursor()
    {
        var request = ObsCaptureRequest.Create(new() { CursorEffect = CursorEffectMode.PresentationStick },
            new(-1800, 0, 640, 480), Display, HardwareEncoderType.SoftwareCpu, false);
        Assert.False(request.Cursor);
    }

    [Theory]
    [InlineData(AudioSourceType.None, false, false, "none")]
    [InlineData(AudioSourceType.None, false, true, "silence")]
    [InlineData(AudioSourceType.SystemOnly, true, false, "silence")]
    [InlineData(AudioSourceType.SystemOnly, false, false, "system")]
    [InlineData(AudioSourceType.MicrophoneOnly, false, false, "microphone")]
    public void MapsAudioPolicyWithoutInventingAnInput(AudioSourceType source, bool recovery, bool maintain, string expected)
    {
        var request = ObsCaptureRequest.Create(new() { AudioSource = source, IsRecoverySilenceMode = recovery,
            MaintainSegmentAudioTrack = maintain }, new(-1800, 0, 640, 480), Display, HardwareEncoderType.SoftwareCpu, false);
        Assert.Equal(expected, request.Audio);
    }
}
