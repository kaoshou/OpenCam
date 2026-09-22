// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using System.Text.Json;

namespace ScreenRecorder.Core.Tests;

public class RecordingConfigurationTests
{
    [Theory]
    [InlineData("Ultra", 18, 10000)]
    [InlineData("Standard", 23, 6000)]
    [InlineData("Compact", 28, 3500)]
    [InlineData("unexpected", 23, 6000)]
    [InlineData(null, 23, 6000)]
    public void VideoQualityPreset_ResolvesRuntimeQuality(
        string? preset,
        int expectedQuality,
        int expectedBitrateKbps)
    {
        var config = new RecordingConfiguration
        {
            VideoQualityPreset = preset!
        };

        Assert.Equal(expectedQuality, config.VideoQualityValue);
        Assert.Equal(expectedBitrateKbps, config.VideoBitrateKbps);
    }

    [Fact]
    public void RuntimeDiskThresholds_DefaultToExistingSafeValues()
    {
        var config = new RecordingConfiguration();

        Assert.Equal(2L * 1024 * 1024 * 1024, config.DiskWarningThresholdBytes);
        Assert.Equal(500L * 1024 * 1024, config.DiskCriticalThresholdBytes);
        Assert.False(config.DeleteWorkingFileAfterSuccessfulRemux);
    }

    [Fact]
    public void NormalizeDiskGuardThresholds_InvalidPairRestoresSafeDefaults()
    {
        var config = new RecordingConfiguration
        {
            DiskWarningThresholdBytes = 400L * 1024 * 1024,
            DiskCriticalThresholdBytes = 500L * 1024 * 1024
        };

        config.NormalizeDiskGuardThresholds();

        Assert.Equal(RecordingConfiguration.DefaultDiskWarningThresholdBytes, config.DiskWarningThresholdBytes);
        Assert.Equal(RecordingConfiguration.DefaultDiskCriticalThresholdBytes, config.DiskCriticalThresholdBytes);
    }

    [Fact]
    public void LegacyIpcPayloadWithoutRuntimeSettingsUsesSafeDefaults()
    {
        var config = JsonSerializer.Deserialize<RecordingConfiguration>("{}");

        Assert.NotNull(config);
        Assert.Equal("Standard", config.VideoQualityPreset);
        Assert.Equal(RecordingConfiguration.DefaultDiskWarningThresholdBytes, config.DiskWarningThresholdBytes);
        Assert.Equal(RecordingConfiguration.DefaultDiskCriticalThresholdBytes, config.DiskCriticalThresholdBytes);
        Assert.False(config.DeleteWorkingFileAfterSuccessfulRemux);
    }

    [Fact]
    public void ApplyPausedSettings_ChangesOnlyAudioAndCursorOptions()
    {
        var target = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            MonitorIndex = 2,
            Region = new CaptureRegion(10, 20, 1280, 720),
            Fps = 60,
            EncoderType = HardwareEncoderType.NvidiaNvenc,
            OutputDirectory = "/original",
            AudioSource = AudioSourceType.SystemOnly,
            MicrophoneDeviceId = null,
            CursorEffect = CursorEffectMode.Default,
            MaintainSegmentAudioTrack = true
        };
        var updates = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.Monitor,
            MonitorIndex = 9,
            Region = new CaptureRegion(0, 0, 640, 480),
            Fps = 15,
            EncoderType = HardwareEncoderType.SoftwareCpu,
            OutputDirectory = "/changed",
            AudioSource = AudioSourceType.MicrophoneOnly,
            MicrophoneDeviceId = "built-in",
            CursorEffect = CursorEffectMode.HighlightHalo
        };

        target.ApplyPausedSettings(updates);

        Assert.Equal(AudioSourceType.MicrophoneOnly, target.AudioSource);
        Assert.Equal("built-in", target.MicrophoneDeviceId);
        Assert.Equal(CursorEffectMode.HighlightHalo, target.CursorEffect);
        Assert.Equal(CaptureSourceType.CustomRegion, target.CaptureSource);
        Assert.Equal(2, target.MonitorIndex);
        Assert.Equal(new CaptureRegion(10, 20, 1280, 720), target.Region);
        Assert.Equal(60, target.Fps);
        Assert.Equal(HardwareEncoderType.NvidiaNvenc, target.EncoderType);
        Assert.Equal("/original", target.OutputDirectory);
        Assert.True(target.MaintainSegmentAudioTrack);
    }
}
