// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Media.Encoders;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class EncoderDetectorTests
{
    [Fact]
    public async Task DetectAvailableEncoders_ShouldReturnAutoAndCpuAtMinimum()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var capabilities = await detector.DetectAvailableEncodersAsync();

        Assert.NotNull(capabilities);
        Assert.True(capabilities.Count >= 2);

        var autoCap = capabilities.FirstOrDefault(c => c.Type == HardwareEncoderType.Auto);
        Assert.NotNull(autoCap);
        Assert.True(autoCap.IsAvailable);

        var cpuCap = capabilities.FirstOrDefault(c => c.Type == HardwareEncoderType.SoftwareCpu);
        Assert.NotNull(cpuCap);
        Assert.True(cpuCap.IsAvailable);
    }

    [Fact]
    public async Task ResolveOptimalEncoder_WhenPreferredIsSoftwareCpu_ShouldReturnSoftwareCpu()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var resolved = await detector.ResolveOptimalEncoderAsync(HardwareEncoderType.SoftwareCpu);

        Assert.Equal(HardwareEncoderType.SoftwareCpu, resolved);
    }

    [Fact]
    public async Task ResolveOptimalEncoder_WhenAuto_ShouldReturnValidEncoder()
    {
        var detector = new FFmpegEncoderDetector(new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider());
        var resolved = await detector.ResolveOptimalEncoderAsync(HardwareEncoderType.Auto);

        Assert.True(Enum.IsDefined(typeof(HardwareEncoderType), resolved));
        Assert.NotEqual(HardwareEncoderType.Auto, resolved);
    }

    [Theory]
    [InlineData(HardwareEncoderType.SoftwareCpu, "libx264")]
    [InlineData(HardwareEncoderType.NvidiaNvenc, "h264_nvenc")]
    [InlineData(HardwareEncoderType.IntelQsv, "h264_qsv")]
    [InlineData(HardwareEncoderType.AmdAmf, "h264_amf")]
    public void GetVideoCodecArgs_ShouldContainExpectedEncoder(HardwareEncoderType type, string expectedCodecName)
    {
        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildOutputArguments(new ScreenRecorder.Core.Models.RecordingConfiguration(), type, "test.mkv");
        Assert.Contains(expectedCodecName, args);
    }

    [Fact]
    public void BuildFFmpegArguments_WithHardwareEncoder_ShouldEmbedEncoderArguments()
    {
        var config = new RecordingConfiguration
        {
            Fps = 30,
            EncoderType = HardwareEncoderType.NvidiaNvenc
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildOutputArguments(
            config, 
            HardwareEncoderType.NvidiaNvenc,
            "test.mkv");

        Assert.Contains("h264_nvenc", args);
    }

    [Theory]
    [InlineData(HardwareEncoderType.SoftwareCpu, "-crf 28")]
    [InlineData(HardwareEncoderType.NvidiaNvenc, "-cq 28")]
    [InlineData(HardwareEncoderType.IntelQsv, "-global_quality 28")]
    [InlineData(HardwareEncoderType.AmdAmf, "-qp_p 28")]
    public void WindowsOutput_UsesSelectedQualityPreset(
        HardwareEncoderType encoderType,
        string expectedArgument)
    {
        var config = new RecordingConfiguration
        {
            VideoQualityPreset = "Compact"
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider()
            .BuildOutputArguments(config, encoderType, "test.mkv");

        Assert.Contains(expectedArgument, args);
    }

    [Fact]
    public void WindowsOutput_FlushesShortMatroskaClustersForCrashRecovery()
    {
        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider()
            .BuildOutputArguments(
                new RecordingConfiguration(),
                HardwareEncoderType.SoftwareCpu,
                "test.mkv");

        Assert.Contains("-flush_packets 1", args);
        Assert.Contains("-cluster_time_limit 1000", args);
    }
}
