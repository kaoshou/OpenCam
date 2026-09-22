// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsFFmpegProviderTests
{
    [Fact]
    public void FullDisplay_SelectsNamedScreenAndScalesRetinaFrames()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.Monitor,
            MonitorIndex = 1,
            AudioSource = AudioSourceType.None,
            Fps = 30
        };

        var args = provider.BuildInputArguments(
            config, 4480, 0, 1920, 1080, false, false);

        Assert.Contains("-i \"Capture screen 1:none\"", args);
        Assert.Contains("scale=1920:1080", args);
        Assert.DoesNotContain("crop=", args);
        Assert.DoesNotContain("aresample", args);
    }

    [Fact]
    public void CustomRegion_UsesDisplayRelativeCrop()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            CaptureSource = CaptureSourceType.CustomRegion,
            MonitorIndex = 1,
            AudioSource = AudioSourceType.None
        };

        var args = provider.BuildInputArguments(
            config, 4580, 50, 640, 480, false, false);

        Assert.Contains("crop=640:480:100:50", args);
    }

    [Fact]
    public void Microphone_UsesSeparateQueuedInputAndTimestampCorrection()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            MonitorIndex = 0,
            AudioSource = AudioSourceType.MicrophoneOnly,
            MicrophoneDeviceId = "2"
        };

        var args = provider.BuildInputArguments(
            config, 0, 0, 1280, 720, false, true);

        Assert.Contains("-i \"Capture screen 0:none\"", args);
        Assert.Contains(
            "-thread_queue_size 1024 -f avfoundation -i \":2\"",
            args);
        Assert.Contains(
            "[1:a]aresample=48000:async=1:first_pts=0," +
            "aformat=sample_rates=48000:channel_layouts=stereo[mic]",
            args);
        Assert.Contains("-map 0:v -map \"[mic]\"", args);
    }

    [Fact]
    public void NativeMicrophone_UsesPcmPipeInsteadOfAvFoundationAudioInput()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            MonitorIndex = 0,
            AudioSource = AudioSourceType.MicrophoneOnly,
            MicrophoneDeviceId = "0"
        };
        const string microphonePipeArgs =
            "-thread_queue_size 1024 -f s16le -ar 48000 -ac 1 -i \"/tmp/microphone.pcm\" ";

        var args = provider.BuildInputArguments(
            config,
            0,
            0,
            1280,
            720,
            false,
            false,
            systemAudioPipeArg: null,
            microphoneAudioPipeArg: microphonePipeArgs);

        Assert.Contains(microphonePipeArgs.Trim(), args);
        Assert.DoesNotContain("-f avfoundation -i \":0\"", args);
        Assert.Contains(
            "[1:a]aresample=48000:async=1:first_pts=0," +
            "aformat=sample_rates=48000:channel_layouts=stereo[mic]",
            args);
        Assert.Contains("-map 0:v -map \"[mic]\"", args);
    }

    [Fact]
    public void SystemAudio_UsesPipeAndExplicitMapping()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            MonitorIndex = 0,
            AudioSource = AudioSourceType.SystemOnly
        };
        const string pipeArgs =
            "-thread_queue_size 1024 -f s16le -ar 48000 -ac 2 -i \"/tmp/system-audio.pcm\" ";

        var args = provider.BuildInputArguments(
            config, 0, 0, 1280, 720, false, false, pipeArgs);

        Assert.Contains(pipeArgs.Trim(), args);
        Assert.Contains(
            "[1:a]aresample=48000:async=1:first_pts=0," +
            "aformat=sample_rates=48000:channel_layouts=stereo[sys]",
            args);
        Assert.Contains("-map 0:v -map \"[sys]\"", args);
    }

    [Fact]
    public void SystemAndMicrophone_NormalizesAndMixesBothInputs()
    {
        var provider = CreateProvider();
        var config = new RecordingConfiguration
        {
            MonitorIndex = 0,
            AudioSource = AudioSourceType.SystemAndMicrophone,
            MicrophoneDeviceId = "0"
        };
        const string pipeArgs =
            "-thread_queue_size 1024 -f s16le -ar 48000 -ac 2 -i \"/tmp/system-audio.pcm\" ";

        var args = provider.BuildInputArguments(
            config, 0, 0, 1280, 720, false, true, pipeArgs);

        Assert.Contains(
            "[1:a]aresample=48000:async=1:first_pts=0[mic]",
            args);
        Assert.Contains(
            "[2:a]aresample=48000:async=1:first_pts=0[sys]",
            args);
        Assert.Contains(
            "[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0," +
            "aformat=sample_rates=48000:channel_layouts=stereo[aout]",
            args);
        Assert.Contains("-map 0:v -map \"[aout]\"", args);
    }

    [Fact]
    public void AudioOutput_Uses48kAac()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration
            {
                AudioSource = AudioSourceType.MicrophoneOnly,
                AudioBitrateKbps = 192
            },
            HardwareEncoderType.AppleVideoToolbox,
            "/tmp/output.mkv");

        Assert.Contains("-c:a aac -ar 48000 -ac 2 -b:a 192k", args);
    }

    [Fact]
    public void SegmentedSession_WithAudioDisabled_KeepsSilentStereoTrack()
    {
        var config = new RecordingConfiguration
        {
            AudioSource = AudioSourceType.None,
            MaintainSegmentAudioTrack = true
        };
        var provider = CreateProvider();

        var inputArgs = provider.BuildInputArguments(
            config, 0, 0, 1280, 720, false, false);
        var outputArgs = provider.BuildOutputArguments(
            config,
            HardwareEncoderType.AppleVideoToolbox,
            "/tmp/output.mkv");

        Assert.Contains(
            "anullsrc=channel_layout=stereo:sample_rate=48000",
            inputArgs);
        Assert.Contains("-map 0:v -map", inputArgs);
        Assert.Contains("-c:a aac -ar 48000 -ac 2", outputArgs);
    }

    [Theory]
    [InlineData(CaptureSourceType.Monitor, "scale=1280:720")]
    [InlineData(CaptureSourceType.CustomRegion, "crop=1280:720:0:0")]
    public void SegmentedSession_WithSilentTrack_PlacesAllInputsBeforeVideoFilter(
        CaptureSourceType captureSource,
        string expectedVideoFilter)
    {
        var config = new RecordingConfiguration
        {
            CaptureSource = captureSource,
            AudioSource = AudioSourceType.None,
            MaintainSegmentAudioTrack = true
        };

        var args = CreateProvider().BuildInputArguments(
            config, 0, 0, 1280, 720, false, false);

        var silentInputIndex = args.IndexOf(
            "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000",
            StringComparison.Ordinal);
        var videoFilterIndex = args.IndexOf(expectedVideoFilter, StringComparison.Ordinal);

        Assert.True(silentInputIndex >= 0, "Expected the silent audio input to be present.");
        Assert.True(videoFilterIndex > silentInputIndex,
            "FFmpeg output filters must appear after every input argument.");
    }

    [Fact]
    public void CursorMode_ControlsMacOsCursorCapture()
    {
        var provider = CreateProvider();
        var visible = provider.BuildInputArguments(
            new RecordingConfiguration
            {
                AudioSource = AudioSourceType.None,
                CursorEffect = CursorEffectMode.HighlightHalo
            },
            0, 0, 1280, 720, false, false);
        var hidden = provider.BuildInputArguments(
            new RecordingConfiguration
            {
                AudioSource = AudioSourceType.None,
                CursorEffect = CursorEffectMode.Hidden
            },
            0, 0, 1280, 720, false, false);

        Assert.Contains("-capture_cursor 1", visible);
        Assert.Contains("-capture_cursor 0", hidden);
    }

    [Fact]
    public void VideoToolboxOutput_UsesNv12PixelFormat()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoQualityPreset = "Standard" },
            HardwareEncoderType.AppleVideoToolbox,
            "/tmp/output.mkv");

        Assert.Contains("-c:v h264_videotoolbox", args);
        Assert.Contains("-pix_fmt nv12", args);
    }

    [Fact]
    public void VideoToolboxOutput_UsesSelectedQualityBitrate()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoQualityPreset = "Compact" },
            HardwareEncoderType.AppleVideoToolbox,
            "/tmp/output.mkv");

        Assert.Contains("-b:v 3500k", args);
    }

    [Fact]
    public void SoftwareOutput_UsesYuv420pPixelFormat()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoQualityPreset = "Standard" },
            HardwareEncoderType.SoftwareCpu,
            "/tmp/output.mkv");

        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
    }

    [Fact]
    public void SoftwareOutput_UsesSelectedCrfQuality()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoQualityPreset = "Ultra" },
            HardwareEncoderType.SoftwareCpu,
            "/tmp/output.mkv");

        Assert.Contains("-crf 18", args);
        Assert.DoesNotContain("-b:v", args);
    }

    [Fact]
    public void Output_FlushesShortMatroskaClustersForCrashRecovery()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration(),
            HardwareEncoderType.SoftwareCpu,
            "/tmp/output.mkv");

        Assert.Contains("-flush_packets 1", args);
        Assert.Contains("-cluster_time_limit 1000", args);
    }

    private static MacOsFFmpegProvider CreateProvider() =>
        new(new FakeDisplayService(
            new MonitorInfo(
                0, "Mac Display 1",
                new CaptureRegion(0, 0, 4480, 2520), true, 2.0),
            new MonitorInfo(
                1, "Mac Display 2",
                new CaptureRegion(4480, 0, 1920, 1080), false, 1.0)));

    private sealed class FakeDisplayService(
        params MonitorInfo[] monitors) : IDisplayService
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;

        public MonitorInfo? GetPrimaryMonitor() =>
            monitors.FirstOrDefault(monitor => monitor.IsPrimary);

        public CaptureRegion GetVirtualScreenBounds() => monitors[0].Bounds;
    }
}
