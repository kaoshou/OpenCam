// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;
using ScreenRecorder.UI.Views;

namespace ScreenRecorder.Media.Tests;

public sealed class DisplayScreenMatcherTests
{
    [Fact]
    public void ThreeDisplays_ReversedUiOrder_KeepCaptureNumbersOnPhysicalScreens()
    {
        var capture = new[]
        {
            Capture(0, 0, 0, 1920, 1080, true, "DISPLAY-A"),
            Capture(1, 1920, 0, 1920, 1080, false, "DISPLAY-B"),
            Capture(2, -1600, -200, 1600, 900, false, "DISPLAY-C")
        };
        var ui = new[]
        {
            Ui(-1600, -200, 1600, 900, false, "DISPLAY-C"),
            Ui(1920, 0, 1920, 1080, false, "DISPLAY-B"),
            Ui(0, 0, 1920, 1080, true, "DISPLAY-A")
        };

        var result = DisplayScreenMatcher.Match(capture, ui);

        Assert.Equal(new[] { (0, 2), (1, 1), (2, 0) }, result.Matches);
        Assert.Empty(result.UnresolvedCaptureIndices);
    }

    [Fact]
    public void FourDisplays_WithoutIdentity_UseUniqueGeometryNotListOrder()
    {
        var capture = new[]
        {
            Capture(0, 0, 0, 1440, 900, true),
            Capture(1, -1920, 0, 1920, 1080),
            Capture(2, 1440, -240, 2560, 1440),
            Capture(3, 4000, 0, 1280, 1024)
        };
        var ui = new[]
        {
            Ui(4000, 0, 1280, 1024),
            Ui(1440, -240, 2560, 1440),
            Ui(-1920, 0, 1920, 1080),
            Ui(0, 0, 1440, 900, true)
        };

        var result = DisplayScreenMatcher.Match(capture, ui);

        Assert.Equal(new[] { (0, 3), (1, 2), (2, 1), (3, 0) }, result.Matches);
        Assert.Empty(result.UnresolvedCaptureIndices);
    }

    [Fact]
    public void MixedScaling_MatchesEquivalentLogicalGeometry()
    {
        var capture = new[]
        {
            Capture(0, 0, 0, 2880, 1800, true, scale: 2),
            Capture(1, 2880, 0, 1920, 1080, scale: 1)
        };
        var ui = new[]
        {
            Ui(2880, 0, 1920, 1080, scale: 1),
            Ui(0, 0, 2880, 1800, true, scale: 2)
        };

        var result = DisplayScreenMatcher.Match(capture, ui);

        Assert.Equal(new[] { (0, 1), (1, 0) }, result.Matches);
    }

    [Fact]
    public void DuplicateGeometryWithoutIdentity_RemainsUnresolved()
    {
        var capture = new[]
        {
            Capture(0, 0, 0, 1920, 1080),
            Capture(1, 0, 0, 1920, 1080),
            Capture(2, 0, 0, 1920, 1080)
        };
        var ui = new[]
        {
            Ui(0, 0, 1920, 1080),
            Ui(0, 0, 1920, 1080),
            Ui(0, 0, 1920, 1080)
        };

        var result = DisplayScreenMatcher.Match(capture, ui);

        Assert.Empty(result.Matches);
        Assert.Equal(new[] { 0, 1, 2 }, result.UnresolvedCaptureIndices);
    }

    [Fact]
    public void DisconnectedScreen_DoesNotInventAMatch()
    {
        var capture = new[]
        {
            Capture(0, 0, 0, 1920, 1080, true),
            Capture(1, 1920, 0, 1920, 1080)
        };
        var ui = new[] { Ui(0, 0, 1920, 1080, true) };

        var result = DisplayScreenMatcher.Match(capture, ui);

        Assert.Equal(new[] { (0, 0) }, result.Matches);
        Assert.Equal(new[] { 1 }, result.UnresolvedCaptureIndices);
    }

    [Fact]
    public void ConflictingIdentity_DoesNotFallbackToSimilarGeometry()
    {
        var result = DisplayScreenMatcher.Match(
            new[] { Capture(0, 0, 0, 1920, 1080, true, "DISPLAY-A") },
            new[] { Ui(0, 0, 1920, 1080, true, "DISPLAY-B") });

        Assert.Empty(result.Matches);
        Assert.Equal(new[] { 0 }, result.UnresolvedCaptureIndices);
    }

    private static CaptureDisplayDescriptor Capture(int index, int x, int y, int width, int height,
        bool primary = false, string? identity = null, double scale = 1) =>
        new(index, new CaptureRegion(x, y, width, height), scale, primary, identity);

    private static UiScreenDescriptor Ui(int x, int y, int width, int height,
        bool primary = false, string? identity = null, double scale = 1) =>
        new(new CaptureRegion(x, y, width, height), scale, primary, identity);
}
