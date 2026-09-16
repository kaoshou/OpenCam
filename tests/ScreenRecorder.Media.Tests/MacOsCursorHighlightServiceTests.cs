using ScreenRecorder.Core.Enums;
using ScreenRecorder.Platform.macOS;

namespace ScreenRecorder.Media.Tests;

public class MacOsCursorHighlightServiceTests : IDisposable
{
    private readonly string _fixtureDirectory;
    private readonly string _helperPath;

    public MacOsCursorHighlightServiceTests()
    {
        _fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            "OpenCamCursorTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDirectory);
        _helperPath = Path.Combine(_fixtureDirectory, "fake-helper");

        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                _helperPath,
                "#!/bin/sh\n" +
                "printf '%s\\n' \"$@\" > \"$0.args\"\n" +
                "while true; do sleep 1; done\n");
            File.SetUnixFileMode(
                _helperPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    [MacOsOnlyFact]
    public async Task StartRippleAndStop_ManagesNativeHelper()
    {
        using var service = new MacOsCursorHighlightService(_helperPath);

        service.Start(CursorEffectMode.HaloWithClickRipple);

        await WaitForFileAsync(_helperPath + ".args");
        Assert.True(service.IsRunning);
        Assert.Equal(
            "--mode\nripple\n",
            await File.ReadAllTextAsync(_helperPath + ".args"));

        service.Stop();

        Assert.False(service.IsRunning);
    }

    [MacOsOnlyFact]
    public void DefaultAndHidden_DoNotLaunchOverlay()
    {
        using var service = new MacOsCursorHighlightService(_helperPath);

        service.Start(CursorEffectMode.Default);
        service.Start(CursorEffectMode.Hidden);

        Assert.False(service.IsRunning);
        Assert.False(File.Exists(_helperPath + ".args"));
    }

    [MacOsOnlyFact]
    public void DamagedHelper_DoesNotAbortRecording()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var damagedHelper = Path.Combine(_fixtureDirectory, "damaged-helper");
        File.WriteAllText(damagedHelper, "not an executable image");
        File.SetUnixFileMode(
            damagedHelper,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        using var service = new MacOsCursorHighlightService(damagedHelper);

        var exception = Record.Exception(
            () => service.Start(CursorEffectMode.HighlightHalo));

        Assert.Null(exception);
        Assert.False(service.IsRunning);
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!File.Exists(path))
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDirectory))
        {
            Directory.Delete(_fixtureDirectory, recursive: true);
        }
    }
}
