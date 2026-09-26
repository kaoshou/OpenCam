// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.macOS;
using System.Diagnostics;

namespace ScreenRecorder.Media.Tests;

public sealed class MacOsLevelLineParserTests
{
    [Fact]
    public void ParsesOnlyFiniteBoundedLevelLines()
    {
        Assert.True(MacOsLevelLineParser.TryParse("LEVEL rms=0.25 peak=0.75", out var rms, out var peak));
        Assert.Equal(0.25, rms);
        Assert.Equal(0.75, peak);

        foreach (var line in new[]
        {
            "READY sample-rate=48000 channels=1 format=s16le",
            "ERROR missing permission",
            "LEVEL rms=0.5",
            "LEVEL rms=NaN peak=0.5",
            "LEVEL rms=0.5 peak=Infinity",
            "LEVEL rms=-0.1 peak=0.5",
            "LEVEL rms=0.5 peak=1.1",
            "LEVEL rms=0.5 peak=0.6 extra"
        })
        {
            Assert.False(MacOsLevelLineParser.TryParse(line, out _, out _));
        }
    }

    [MacOsOnlyFact]
    public async Task HelperLevelLines_UpdateLatestAndClearAfterStop()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var root = Path.Combine(Path.GetTempPath(), "OpenCamLevelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var helper = Path.Combine(root, "fake-helper");
            File.WriteAllText(helper,
                "#!/bin/sh\n" +
                "echo 'READY sample-rate=48000 channels=1 format=s16le' >&2\n" +
                "echo 'LEVEL rms=0.2 peak=0.3' >&2\n" +
                "echo 'LEVEL rms=0.7 peak=0.8' >&2\n" +
                "while true; do sleep 1; done\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            await using var capture = new MacOsMicrophoneCapture(
                helper, () => Path.Combine(root, "capture"), UnixFifo.CreatePrivate);
            var errors = new List<string>();
            capture.AudioErrorOccurred += (_, error) => errors.Add(error);
            Assert.NotNull(await capture.StartCaptureAsync("0"));

            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (capture.ReadLatestLevel(DateTimeOffset.UtcNow)?.Rms != 0.7 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
            Assert.Equal(0.7, capture.ReadLatestLevel(DateTimeOffset.UtcNow)?.Rms);
            Assert.Empty(errors);

            await capture.StopCaptureAsync();
            Assert.Null(capture.ReadLatestLevel(DateTimeOffset.UtcNow));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [RealMacHelperFact]
    public async Task RealMicrophoneHelper_WhenExplicitlyConfigured_ProducesFreshLevel()
    {
        var helper = Environment.GetEnvironmentVariable("OPENCAM_REAL_MIC_HELPER");
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(helper)) return;

        var root = Path.Combine(Path.GetTempPath(), "OpenCamRealLevelTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? reader = null;
        try
        {
            await using var capture = new MacOsMicrophoneCapture(
                helper, () => Path.Combine(root, "capture"), UnixFifo.CreatePrivate);
            var info = await capture.StartCaptureAsync(null);
            Assert.NotNull(info);
            var startInfo = new ProcessStartInfo("/bin/dd")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("if=" + info.PipePath);
            startInfo.ArgumentList.Add("of=/dev/null");
            startInfo.ArgumentList.Add("bs=4096");
            reader = Process.Start(startInfo);
            Assert.NotNull(reader);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (capture.ReadLatestLevel(DateTimeOffset.UtcNow) is null && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
            Assert.NotNull(capture.ReadLatestLevel(DateTimeOffset.UtcNow));
            await capture.StopCaptureAsync();
        }
        finally
        {
            try { if (reader is { HasExited: false }) reader.Kill(); } catch { }
            reader?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
