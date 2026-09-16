using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsFFmpegProviderTests
{
    [Fact]
    public void FullDisplay_SelectsNamedScreenWithoutCrop()
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
        Assert.DoesNotContain("crop=", args);
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
    public void Microphone_UsesConfiguredAudioDevice()
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

        Assert.Contains("-i \"Capture screen 0:2\"", args);
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
