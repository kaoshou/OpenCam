// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsMicrophoneCaptureTests : IDisposable
{
    private readonly string _fixtureDirectory;
    private readonly string _helperPath;
    private readonly string _captureDirectory;

    public MacOsMicrophoneCaptureTests()
    {
        _fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            "OpenCamMicrophoneTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDirectory);
        _helperPath = Path.Combine(_fixtureDirectory, "fake-helper");
        _captureDirectory = Path.Combine(_fixtureDirectory, "capture");

        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                _helperPath,
                "#!/bin/sh\n" +
                "printf '%s\\n' \"$@\" > \"$0.args\"\n" +
                "echo 'READY sample-rate=48000 channels=1 format=s16le' >&2\n" +
                "while true; do sleep 1; done\n");
            File.SetUnixFileMode(
                _helperPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    [MacOsOnlyFact]
    public async Task StartAndStop_ManagesNativeHelperAndAnonymousPcm()
    {
        await using var capture = new MacOsMicrophoneCapture(
            _helperPath);

        var info = await capture.StartCaptureAsync("0");

        Assert.NotNull(info);
        Assert.Equal(48000, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Contains("-f s16le -ar 48000 -ac 1", info.FfmpegInputArgs);
        Assert.Empty(info.PipePath);
        Assert.NotNull(info.PcmStream);
        Assert.False(Directory.Exists(_captureDirectory));
        Assert.True(capture.IsCapturing);

        var helperArguments = await File.ReadAllTextAsync(_helperPath + ".args");
        Assert.Equal("--stdout\n", helperArguments);

        await capture.StopCaptureAsync();

        Assert.False(capture.IsCapturing);
        Assert.False(Directory.Exists(_captureDirectory));
    }

    [MacOsOnlyFact]
    public async Task StartCapture_WaitsForDelayedNativeHelperReadiness()
    {
        File.WriteAllText(
            _helperPath,
            "#!/bin/sh\n" +
            "sleep 6\n" +
            "echo 'READY sample-rate=48000 channels=1 format=s16le' >&2\n" +
            "while true; do sleep 1; done\n");

        await using var capture = new MacOsMicrophoneCapture(
            _helperPath);

        var info = await capture.StartCaptureAsync("0");

        Assert.NotNull(info);
        Assert.True(capture.IsCapturing);
        await capture.StopCaptureAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDirectory))
        {
            Directory.Delete(_fixtureDirectory, recursive: true);
        }
    }
}
