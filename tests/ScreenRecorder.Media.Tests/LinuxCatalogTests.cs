// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxCatalogTests
{
    [Fact]
    public void EnumerationRefusesTamperedExecutableBeforeLaunchingIt()
    {
        if (OperatingSystem.IsWindows()) return; // executable shell fixture is Unix-only
        var root = Path.Combine(Path.GetTempPath(), "opencam-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        try {
            var marker = Path.Combine(root, "executed");
            var helper = Path.Combine(root, "bin", "OpenCam.ObsCapture");
            File.WriteAllText(helper, "#!/bin/sh\ntouch '" + marker + "'\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(root, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new {
                enabled = true, protocol = 1, obsVersion = "33.0.0", platform = "linux-x64",
                sourceRevision = ScreenRecorder.Media.Capture.ObsRuntime.PinnedSourceRevision,
                files = new Dictionary<string,string> { ["bin/OpenCam.ObsCapture"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(helper))) },
                capabilities = new { backends = new[] { "x11" }, audio = new[] { "none" }, encoders = new[] { "cpu" } }
            }));
            LinuxDesktopCatalog.EnumerateRuntime(root, "x11"); Assert.True(File.Exists(marker)); File.Delete(marker);
            File.AppendAllText(helper, "# modified\n");
            Assert.Empty(LinuxDesktopCatalog.EnumerateRuntime(root, "x11")); Assert.False(File.Exists(marker));
        } finally { Directory.Delete(root, true); }
    }
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
    [Fact]
    public void Pulse16JsonUsesMonitorSourceNameAndEmptyStringForNonMonitor()
    {
        // PulseAudio v16.1 src/utils/pactl.c source_info_callback uses
        // monitor_source (not the C struct's monitor_of_sink field).
        const string sources = "[{\"name\":\"mic\",\"description\":\"Microphone\",\"monitor_source\":\"\"},{\"name\":\"speakers.monitor\",\"description\":\"Monitor\",\"monitor_source\":\"speakers\"}]";
        var audio = LinuxDesktopCatalog.ParseAudio(sources, "[]", "mic", "speakers");
        Assert.Equal("mic", Assert.Single(audio.Microphones).Id);
    }
}
