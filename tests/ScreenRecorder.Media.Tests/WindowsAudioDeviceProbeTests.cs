// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using ScreenRecorder.Platform.Windows.Audio;

namespace ScreenRecorder.Media.Tests;

public sealed class WindowsAudioDeviceProbeTests
{
    [UnixOnlyFact]
    public void HungFfmpegDeviceProbe_ReturnsDefaultWithoutBlockingRecording()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var script = Path.Combine(Path.GetTempPath(), "opencam-device-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(script, "#!/bin/sh\nsleep 10\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var service = new WindowsAudioDeviceService(script, probeTimeoutMs: 300);
            var watch = Stopwatch.StartNew();

            var devices = service.GetRecordingDevices();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Probe blocked for {watch.Elapsed}");
            Assert.Equal("default_mic", Assert.Single(devices).Id);
        }
        finally
        {
            File.Delete(script);
        }
    }
}
