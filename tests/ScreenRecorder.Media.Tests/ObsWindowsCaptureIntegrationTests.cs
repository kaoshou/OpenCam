// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using ScreenRecorder.Platform.Windows.Capture;
using ScreenRecorder.Platform.Windows.Display;

namespace ScreenRecorder.Media.Tests;

public sealed class ObsWindowsCaptureIntegrationTests
{
    private const string StableId = @"\\?\DISPLAY#GSM0001#4&abcd&0&UID42#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private static readonly MonitorInfo Display = new(8, @"\\.\DISPLAY3", new(-1920, 0, 1920, 1080), false, 1.5);
    private static readonly CaptureRegion Region = new(-1800, 100, 640, 480);

    [Fact]
    public void WindowsIdentityResolvesCurrentInterfaceWithoutUsingUiEnumerationIndex()
    {
        var service = new WindowsDisplayService(() => [Display with { Index = 1 }], name =>
            name == @"\\.\DISPLAY3" ? [StableId] : []);
        Assert.Equal(StableId, service.GetRecordingDisplayIdentity(Display));
    }

    [Fact]
    public void WindowsIdentityRejectsChangedGeometryMissingAndAmbiguousTargets()
    {
        Assert.Null(new WindowsDisplayService(() => [Display with { Bounds = new(-1920, 0, 1920, 1200) }], _ => [StableId])
            .GetRecordingDisplayIdentity(Display));
        Assert.Null(new WindowsDisplayService(() => [Display], _ => []).GetRecordingDisplayIdentity(Display));
        Assert.Null(new WindowsDisplayService(() => [Display], _ => [StableId, StableId + "replacement"])
            .GetRecordingDisplayIdentity(Display));
        Assert.Null(new WindowsDisplayService(() => [Display, Display with { Index = 9 }], _ => [StableId])
            .GetRecordingDisplayIdentity(Display));
    }

    [Fact]
    public void LibobsHealthChecksStableInterfaceAndBoundsInsteadOfGdiLabel()
    {
        var selection = new CaptureSelection(CaptureBackend.WindowsGraphicsCapture, CaptureFallbackReason.None,
            StableId, null, null, Region) { RecordingCore = "libobs-v1", OutputBounds = Display.Bounds };
        var native = new GraphicsCaptureMonitor(1, StableId, Display.Bounds);
        Assert.True(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => true, _ => native).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => false, _ => native).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => true,
            _ => native with { DeviceName = "replacement" }).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => true,
            _ => native with { Bounds = new(-1920, 0, 1920, 1200) }).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => true, _ => null).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(new UnusedCatalog(), () => true, _ => native)
            .Check(selection with { Bounds = new(-100, 0, 640, 480) }));
    }

    [Theory]
    [InlineData("win-x64", WindowsCaptureMode.ModernExperimental, false)]
    [InlineData("win-x64", WindowsCaptureMode.CompatibleGdi, false)]
    [InlineData("win-x64", (WindowsCaptureMode)2, true)]
    public void ExplicitManualCaptureChoicesRetainExistingEngine(string platform, WindowsCaptureMode mode, bool eligible)
    {
        Assert.Equal(eligible, ObsRecordingEngineRouter.SupportsCaptureMode(new() { WindowsCaptureMode = mode }, platform));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MacExplicitStandardAndNativePreferencesArePreserved(bool nativeCapture, bool eligible)
    {
        Assert.Equal(eligible, ObsRecordingEngineRouter.SupportsCaptureMode(new() { NativeCaptureEnabled = nativeCapture }, "osx-arm64"));
    }

    private sealed class UnusedCatalog : IDxgiOutputCatalog
    {
        public IReadOnlyList<DxgiOutputInfo> GetOutputs() => throw new InvalidOperationException("Stable libobs identities do not use legacy DXGI indices.");
    }
}
