// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
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

    [MacOsOnlyTheory]
    [InlineData(CursorEffectMode.HaloWithClickRipple, "ripple")]
    [InlineData(CursorEffectMode.PresentationStick, "stick")]
    public async Task StartSelectedModeAndStop_ManagesNativeHelper(CursorEffectMode mode, string argument)
    {
        using var service = new MacOsCursorHighlightService(_helperPath);

        service.Start(mode);

        await WaitForFileAsync(_helperPath + ".args");
        Assert.True(service.IsRunning);
        Assert.Equal(
            $"--mode\n{argument}\n" + (mode == CursorEffectMode.PresentationStick ? "--stick-style\nWarmIvory\n--stick-size\nStandard\n" : ""),
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

    [MacOsOnlyTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PresentationReadinessRequiresExactAcknowledgementAndOwnsShutdown(bool ready)
    {
        File.WriteAllText(_helperPath, "#!/bin/sh\nprintf '" + (ready ? "READY stick-v2 WarmIvory Standard" : "WRONG") +
            "\\n'\nwhile IFS= read -r line; do [ \"$line\" = stop ] && exit 0; done\n");
        using var service = new MacOsCursorHighlightService(_helperPath);
        var overlay = Assert.IsAssignableFrom<ScreenRecorder.Core.Interfaces.IPresentationPointerOverlay>(service);
        Assert.Equal(ready, await overlay.StartPresentationAsync(TeachingPointerOptions.Default));
        Assert.Equal(ready, service.IsRunning);
        service.Stop();
        Assert.False(service.IsRunning);
        Assert.Equal(ready, await overlay.StartPresentationAsync(TeachingPointerOptions.Default));
        service.Stop();
    }

    [MacOsOnlyFact]
    public async Task CancelledPresentationHandshakeReapsOnlyItsOwnedHelper()
    {
        using var service = new MacOsCursorHighlightService(_helperPath);
        var overlay = Assert.IsAssignableFrom<ScreenRecorder.Core.Interfaces.IPresentationPointerOverlay>(service);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => overlay.StartPresentationAsync(TeachingPointerOptions.Default, cancel.Token));
        Assert.False(service.IsRunning);
    }

    [MacOsOnlyFact]
    public async Task UnexpectedOverlayExitIsReportedWithoutThrowingIntoRecording()
    {
        File.WriteAllText(_helperPath, "#!/bin/sh\nprintf 'READY stick-v2 WarmIvory Standard\\n'\n" +
            "while [ ! -f \"$0.exit\" ]; do sleep 0.05; done\nexit 7\n");
        using var service = new MacOsCursorHighlightService(_helperPath);
        var overlay = Assert.IsAssignableFrom<ScreenRecorder.Core.Interfaces.IPresentationPointerOverlay>(service);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overlay.OverlayFailed += (_, _) => failed.TrySetResult();
        Assert.True(await overlay.StartPresentationAsync(TeachingPointerOptions.Default));
        File.WriteAllText(_helperPath + ".exit", "exit");
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(service.IsRunning);
        service.Stop();
    }

    [MacOsOnlyTheory]
    [InlineData(TeachingPointerStyle.TealSilver, TeachingPointerSize.Small)]
    [InlineData(TeachingPointerStyle.RoundShort, TeachingPointerSize.Standard)]
    [InlineData(TeachingPointerStyle.HighContrast, TeachingPointerSize.Large)]
    public async Task SelectedArtworkMustBeAcknowledgedByTheOwnedHelper(TeachingPointerStyle style, TeachingPointerSize size)
    {
        File.WriteAllText(_helperPath, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$0.args\"\n" +
            "printf 'READY stick-v2 %s %s\\n' \"$4\" \"$6\"\n" +
            "while IFS= read -r line; do [ \"$line\" = stop ] && exit 0; done\n");
        using var service = new MacOsCursorHighlightService(_helperPath);
        Assert.True(await service.StartPresentationAsync(new(style,size)));
        Assert.Equal($"--mode\nstick\n--stick-style\n{style}\n--stick-size\n{size}\n",
            await File.ReadAllTextAsync(_helperPath+".args"));
        service.Stop();
        Assert.False(service.IsRunning);
    }

    [MacOsOnlyFact]
    public async Task OldHelperAcknowledgementCannotDisableNativeCursorForNewArtwork()
    {
        File.WriteAllText(_helperPath,"#!/bin/sh\nprintf 'READY stick-v1\\n'\nwhile IFS= read -r line; do [ \"$line\" = stop ] && exit 0; done\n");
        using var service=new MacOsCursorHighlightService(_helperPath);
        Assert.False(await service.StartPresentationAsync(new(TeachingPointerStyle.HighContrast,TeachingPointerSize.Large)));
        Assert.False(service.IsRunning);
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
