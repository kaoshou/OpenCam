// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class WindowsCapturePlannerTests
{
    private static readonly CaptureRegion Left = new(-1920, 0, 1920, 1080);
    private static readonly MonitorInfo[] Monitors =
    [
        new(0, "RIGHT", new(1920, 0, 1920, 1080), false, 1),
        new(1, "MAIN", new(0, 0, 1920, 1080), true, 1.5),
        new(2, "LEFT", Left, false, 1.25)
    ];
    private static readonly DxgiOutputInfo[] Outputs =
    [
        new(7, 0, "LEFT", Left, true, true, true),
        new(7, 1, "RIGHT", Monitors[0].Bounds, true, true, true),
        new(7, 2, "MAIN", Monitors[1].Bounds, true, true, true)
    ];
    private static RecordingConfiguration Config => new() { WindowsCaptureMode = WindowsCaptureMode.ModernExperimental, MonitorIndex = 2 };

    [Fact]
    public void MapsDeviceIdentityNotUiIndex()
    {
        var result = WindowsCapturePlanner.Select(Config, Left, Monitors, Outputs, true);
        Assert.Equal(CaptureBackend.DesktopDuplication, result.Backend);
        Assert.Equal(0, result.OutputIndex);
        Assert.Equal("LEFT", result.DeviceName);
        Assert.Equal(7, result.AdapterLuid);
        Assert.Equal(Left, result.Bounds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleHardwareAdaptersCannotAttestFfmpegDefault(bool otherAdapterHasOutput)
    {
        // FFmpeg may receive a different per-executable GPU profile, even with a headless GPU.
        DxgiOutputInfo[] outputs = otherAdapterHasOutput
            ? [.. Outputs, new(8, 0, "OTHER", new(3840, 0, 1920, 1080), true, true, false)]
            : Outputs;
        var eligible = DxgiOutputCatalog.ValidateAdapterIdentity(outputs, [new(7, false), new(8, false)]);
        Assert.Equal(CaptureFallbackReason.UnsupportedTopology,
            WindowsCapturePlanner.Select(Config, Left, Monitors, eligible, true).FallbackReason);
        Assert.All(eligible, o => Assert.False(o.IsDefaultAdapter));
    }

    [Fact]
    public void UniqueHardwareAdapterMustMatchButSoftwareAdapterDoesNotCreateAmbiguity()
    {
        var eligible = DxgiOutputCatalog.ValidateAdapterIdentity(Outputs, [new(7, false), new(8, true)]);
        Assert.Equal(CaptureBackend.DesktopDuplication,
            WindowsCapturePlanner.Select(Config, Left, Monitors, eligible, true).Backend);
        foreach (var adapters in new DxgiAdapterInfo[][] { [], [new(8, false)], [new(7, true)] })
            Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(Config, Left, Monitors,
                DxgiOutputCatalog.ValidateAdapterIdentity(Outputs, adapters), true).Backend);
    }

    [Fact]
    public void PhysicalNegativeRegionIsNotScaledAgain()
    {
        var config = Config; config.CaptureSource = CaptureSourceType.CustomRegion;
        var region = new CaptureRegion(-1800, 100, 640, 480);
        var result = WindowsCapturePlanner.Select(config, region, Monitors, Outputs, true);
        Assert.Equal(CaptureBackend.DesktopDuplication, result.Backend);
        Assert.Equal(region, result.Bounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SupportsEveryMappedOutput(int monitor)
    {
        var config = Config; config.MonitorIndex = monitor;
        var result = WindowsCapturePlanner.Select(config, Monitors[monitor].Bounds, Monitors, Outputs, true);
        Assert.Equal(Monitors[monitor].DeviceName, result.DeviceName);
        Assert.Equal(CaptureBackend.DesktopDuplication, result.Backend);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void UnsupportedOutputFallsBack(bool attached, bool identity, bool defaultAdapter)
    {
        var output = Outputs[0] with { Attached = attached, IdentityRotation = identity, IsDefaultAdapter = defaultAdapter };
        Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(Config, Left, Monitors, [output], true).Backend);
    }

    [Fact]
    public void AmbiguousMirrorDoesNotPickFirst()
    {
        Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(Config, Left,
            [.. Monitors, new(3, "MIRROR", Left, false, 1)], Outputs, true).Backend);
    }

    [Fact]
    public void CrossOutputAndOverflowCannotSelectAnOutput()
    {
        var config = Config; config.CaptureSource = CaptureSourceType.CustomRegion;
        foreach (var region in new[] { new CaptureRegion(-100, 0, 200, 200), new CaptureRegion(int.MaxValue - 2, 0, 100, 100) })
            Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(config, region, Monitors, Outputs, true).Backend);
    }

    [Fact]
    public void UnknownMonitorOrDuplicateOutputDoesNotSelectPrimary()
    {
        var config = Config; config.MonitorIndex = 99;
        Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(config, Left, Monitors, Outputs, true).Backend);
        Assert.Equal(CaptureBackend.Gdi, WindowsCapturePlanner.Select(Config, Left, Monitors, [.. Outputs, Outputs[0]], true).Backend);
    }

    [Fact]
    public void MissingFilterAndOldPreferenceUseGdi()
    {
        Assert.Equal(CaptureFallbackReason.FilterUnavailable, WindowsCapturePlanner.Select(Config, Left, Monitors, Outputs, false).FallbackReason);
        Assert.Equal(CaptureFallbackReason.None, WindowsCapturePlanner.Select(new(), Left, [], [], false).FallbackReason);
    }

    [Fact]
    public void NativeCatalogDoesNotLoadWindowsDllOnOtherPlatforms()
    {
        if (!OperatingSystem.IsWindows()) Assert.Empty(new DxgiOutputCatalog().GetOutputs());
    }

    [WindowsOnlyFact]
    public void NativeCatalogHasUniqueAttachedOutputIdentities()
    {
        var outputs = new DxgiOutputCatalog().GetOutputs();
        Assert.NotEmpty(outputs);
        Assert.All(outputs, o => { Assert.NotEmpty(o.DeviceName); Assert.True(o.Bounds.IsValid); });
        Assert.Equal(outputs.Count, outputs.Select(o => (o.AdapterLuid, o.OutputIndex)).Distinct().Count());
    }
}
