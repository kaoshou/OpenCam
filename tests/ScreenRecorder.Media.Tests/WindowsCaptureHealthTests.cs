// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class WindowsCaptureHealthTests
{
    private sealed class Catalog : IDxgiOutputCatalog
    {
        public IReadOnlyList<DxgiOutputInfo> Outputs = [new(7, 2, "LEFT", new(-1920, 0, 1920, 1080), true, true, true)];
        public IReadOnlyList<DxgiOutputInfo> GetOutputs() => Outputs;
    }
    [Fact]
    public void NewlyAmbiguousAdapterIdentityStopsPinnedCapture()
    {
        var catalog = new Catalog();
        var selection = new CaptureSelection(CaptureBackend.DesktopDuplication, CaptureFallbackReason.None,
            "LEFT", 7, 2, new(-1800, 100, 640, 480));
        Assert.True(new WindowsCaptureHealthMonitor(catalog, () => true).Check(selection));
        catalog.Outputs = DxgiOutputCatalog.ValidateAdapterIdentity(catalog.Outputs, [new(7, false), new(8, false)]);
        Assert.False(new WindowsCaptureHealthMonitor(catalog, () => true).Check(selection));
    }

    [Fact]
    public void LockedDesktopOrChangedOutputIsUnhealthyEvenWithAdvancingFrames()
    {
        var catalog = new Catalog();
        var selection = new CaptureSelection(CaptureBackend.DesktopDuplication, CaptureFallbackReason.None,
            "LEFT", 7, 2, new(-1800, 100, 640, 480));
        Assert.True(new WindowsCaptureHealthMonitor(catalog, () => true).Check(selection));
        Assert.False(new WindowsCaptureHealthMonitor(catalog, () => false).Check(selection));
        catalog.Outputs = [catalog.Outputs[0] with { DeviceName = "REPLACEMENT" }];
        Assert.False(new WindowsCaptureHealthMonitor(catalog, () => true).Check(selection));
    }
}
