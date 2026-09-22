// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Media.Capture;

namespace ScreenRecorder.Media.Tests;

public class AudioDeviceProbeTests
{
    [Fact]
    public void WindowsProbe_PreservesDirectShowArguments()
    {
        Assert.Equal(
            "-list_devices true -f dshow -i dummy",
            FFmpegScreenRecorderEngine.BuildAudioDeviceListArguments(isWindows: true));
    }

    [Fact]
    public void MacProbe_UsesAvFoundationArguments()
    {
        Assert.Equal(
            "-hide_banner -list_devices true -f avfoundation -i \"\"",
            FFmpegScreenRecorderEngine.BuildAudioDeviceListArguments(isWindows: false));
    }

    [Fact]
    public void MacProbe_OnlyMatchesIdsInAudioSection()
    {
        const string output = """
            AVFoundation video devices:
            [0] FaceTime Camera
            [1] Capture screen 0
            AVFoundation audio devices:
            [0] Built-in Microphone
            """;

        Assert.True(FFmpegScreenRecorderEngine.IsAudioDeviceListed(
            output, "0", isWindows: false));
        Assert.False(FFmpegScreenRecorderEngine.IsAudioDeviceListed(
            output, "1", isWindows: false));
    }
}
