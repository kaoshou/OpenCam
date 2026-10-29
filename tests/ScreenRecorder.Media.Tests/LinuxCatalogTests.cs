// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxCatalogTests
{
    [Fact]
    public void NativeDisplaysPreserveExactIdsAndNeverInventFallback()
    {
        var monitors = LinuxDesktopCatalog.ParseDisplays("OCLINUX {\"event\":\"display\",\"id\":\"xshm:3\",\"label\":\"HDMI-1 (1920x1080 @ 2560,0)\"}\n");
        var m = Assert.Single(monitors);
        Assert.Equal("xshm:3", m.DeviceName); Assert.Equal(2560, m.Bounds.X); Assert.Equal(1920, m.Bounds.Width);
        Assert.Empty(LinuxDesktopCatalog.ParseDisplays("ordinary log\n"));
        Assert.Empty(LinuxDesktopCatalog.ParseDisplays("OCLINUX {\"event\":\"display\",\"id\":\"xshm:-1\",\"label\":\"Select a display\"}\n"));
    }
    [Fact]
    public void PulseDefaultOutputUsesMonitorIdNotSinkOrInput()
    {
        const string sinks = "[{\"name\":\"speakers\",\"description\":\"Speakers\",\"monitor_source\":\"speakers.monitor\"}]";
        const string sources = "[{\"name\":\"mic\",\"description\":\"Microphone\",\"monitor_of_sink\":null},{\"name\":\"speakers.monitor\",\"description\":\"Monitor\",\"monitor_of_sink\":1}]";
        var audio = LinuxDesktopCatalog.ParseAudio(sources, sinks, "mic", "speakers");
        Assert.Equal("speakers.monitor", audio.ResolveSystem("default"));
        Assert.Equal("mic", audio.ResolveMicrophone("default_mic"));
        Assert.Single(audio.Microphones); Assert.Single(audio.Playback);
        Assert.Throws<IOException>(() => audio.ResolveSystem("speakers"));
        Assert.Throws<IOException>(() => audio.ResolveMicrophone("speakers.monitor"));
        Assert.Throws<IOException>(() => audio.ResolveMicrophone("removed"));
    }
}
