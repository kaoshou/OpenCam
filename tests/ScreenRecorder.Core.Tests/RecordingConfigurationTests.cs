using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public class RecordingConfigurationTests
{
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
            CursorEffect = CursorEffectMode.Default
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
    }
}
