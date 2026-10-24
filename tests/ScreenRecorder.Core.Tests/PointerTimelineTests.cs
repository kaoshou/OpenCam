// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public sealed class PointerTimelineTests
{
    [Fact]
    public void NativeFrameClockMapsSourcePtsWithoutProgressReceiptTime()
    {
        var anchor = new CaptureClockAnchor(100_000, 8_000_000);
        Assert.Equal(8_033_333, anchor.ToHostMicroseconds(133_333));
        Assert.Equal(7_900_000, anchor.ToHostMicroseconds(0));
    }

    [Fact]
    public void InterpolationUsesAcquisitionTimeAndKeepsButtonsAtTheirEventTime()
    {
        var samples = new[] { new PointerSample(1_000_000, 10, 20, true, 0), new PointerSample(1_050_000, 30, 40, true, 1) };
        var at = PointerTimeline.Resolve(samples, 1_025_000);
        Assert.NotNull(at); Assert.Equal(20, at.Value.X); Assert.Equal(30, at.Value.Y); Assert.Equal(0, at.Value.Buttons);
        Assert.Equal(1, PointerTimeline.Resolve(samples, 1_050_000)!.Value.Buttons);
    }

    [Theory]
    [InlineData(999_999)]
    [InlineData(1_050_001)]
    public void NeverExtrapolatesOutsideRecordedSamples(long time)
    {
        var samples = new[] { new PointerSample(1_000_000, 10, 20, true, 0), new PointerSample(1_050_000, 30, 40, true, 0) };
        Assert.Null(PointerTimeline.Resolve(samples, time));
    }

    [Theory]
    [InlineData(1_100_001, true)]
    [InlineData(1_050_000, false)]
    public void GapsAndVisibilityTransitionsAreNotSmoothed(long next, bool visible)
    {
        var samples = new[] { new PointerSample(1_000_000, 10, 20, true, 0), new PointerSample(next, 0, 0, visible, 0) };
        Assert.Null(PointerTimeline.Resolve(samples, 1_025_000));
    }

    [Fact]
    public void RegionProjectionHandlesNegativeOriginsAndDoesNotRetainOutsideCoordinates()
    {
        var bounds = new CaptureRegion(-1920, -200, 1920, 1080);
        Assert.Equal(new PointerSample(50, 100, 100, true, 1), PointerSample.FromDesktop(50, -1820, -100, bounds, true, 1));
        Assert.Equal(new PointerSample(50, 0, 0, false, 0), PointerSample.FromDesktop(50, 10, 10, bounds, true, 1));
    }

    [Fact]
    public void InvalidCoordinatesAndNonMonotonicSamplesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => PointerTimeline.Validate(new[] { new PointerSample(1, double.NaN, 0, true, 0) }));
        Assert.Throws<ArgumentException>(() => PointerTimeline.Validate(new[] { new PointerSample(2, 0, 0, true, 0), new PointerSample(1, 0, 0, true, 0) }));
        Assert.Throws<ArgumentException>(() => PointerTimeline.Validate(new[] { new PointerSample(1, 100, 20, false, 0) }));
        Assert.Throws<OverflowException>(() => new CaptureClockAnchor(0, long.MaxValue).ToHostMicroseconds(1));
    }
}
