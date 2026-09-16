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
            "[1:a]aresample=48000:async=1:first_pts=0[mic]",
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
            "[1:a]aresample=48000:async=1:first_pts=0[sys]",
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
            "[sys][mic]amix=inputs=2:duration=longest:dropout_transition=0[aout]",
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

        Assert.Contains("-c:a aac -ar 48000 -b:a 192k", args);
    }

    [Fact]
    public void VideoToolboxOutput_UsesNv12PixelFormat()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoBitrateKbps = 6000 },
            HardwareEncoderType.AppleVideoToolbox,
            "/tmp/output.mkv");

        Assert.Contains("-c:v h264_videotoolbox", args);
        Assert.Contains("-pix_fmt nv12", args);
    }

    [Fact]
    public void SoftwareOutput_UsesYuv420pPixelFormat()
    {
        var args = CreateProvider().BuildOutputArguments(
            new RecordingConfiguration { VideoBitrateKbps = 6000 },
            HardwareEncoderType.SoftwareCpu,
            "/tmp/output.mkv");

        Assert.Contains("-c:v libx264", args);
        Assert.Contains("-pix_fmt yuv420p", args);
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
