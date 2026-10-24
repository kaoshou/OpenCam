// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public sealed class NativePointerProtocolTests
{
    [Fact]
    public void FrameClockRetainsAcquisitionAndPresentationForRepeatedSurfaces()
    {
        var parser = new NativePointerProtocol(1920, 1080);
        var first = parser.Parse("FRAME_CLOCK v1 0 8000000 7999000 1")!.Value;
        var repeated = parser.Parse("FRAME_CLOCK v1 33333 8033333 7999000 1")!.Value;
        Assert.Equal(0, first.Frame!.Value.SourcePtsMicroseconds);
        Assert.Equal(8033333, repeated.Frame!.Value.PresentationMicroseconds);
        Assert.Equal(7999000, repeated.Frame!.Value.AcquisitionMicroseconds);
        Assert.Equal(1UL, repeated.Frame!.Value.Sequence);
    }

    [Fact]
    public void PointerMessagesKeepCoordinatesPrivateAndIndependentOfFrameOrdering()
    {
        var parser = new NativePointerProtocol(1920, 1080);
        parser.Parse("FRAME_CLOCK v1 0 8000000 7999000 1");
        var point = parser.Parse("POINTER v1 7999500 12 25 1 1")!.Value;
        Assert.Equal(new PointerSample(7999500, 12, 25, true, 1), point.Pointer);
        Assert.Null(parser.Parse("READY bgra-matroska-clock-v1"));
    }

    [Theory]
    [InlineData("POINTER v1 1 1920 0 1 0")]
    [InlineData("POINTER v1 1 0 1080 1 0")]
    [InlineData("POINTER v1 1 12 25 0 0")]
    [InlineData("POINTER v1 1 NaN 0 1 0")]
    [InlineData("POINTER v1 1 0 0 2 0")]
    [InlineData("POINTER v1 1 0 0 1 8")]
    [InlineData("POINTER v2 1 0 0 1 0")]
    [InlineData("POINTER v1 1 0 0 1 0 extra")]
    [InlineData("FRAME_CLOCK v1 0 100 101 1")]
    [InlineData("FRAME_CLOCK v1 -1 100 99 1")]
    [InlineData("FRAME_CLOCK v1 0 100 99 0")]
    public void MalformedMetadataFailsClosed(string line)
    {
        Assert.Throws<InvalidDataException>(() => new NativePointerProtocol(1920, 1080).Parse(line));
    }

    [Fact]
    public void NonMonotonicSamplesAndChangingClockOriginsAreRejected()
    {
        var parser = new NativePointerProtocol(1920, 1080);
        parser.Parse("POINTER v1 100 0 0 1 0");
        Assert.Throws<InvalidDataException>(() => parser.Parse("POINTER v1 99 0 0 1 0"));
        parser.Parse("FRAME_CLOCK v1 0 8000000 7999000 1");
        Assert.Throws<InvalidDataException>(() => parser.Parse("FRAME_CLOCK v1 33333 9033333 7999000 1"));
        Assert.Throws<InvalidDataException>(() => parser.Parse("FRAME_CLOCK v1 0 8000000 7999000 1"));
    }

    [Fact]
    public void MillisecondMuxRoundingIsAllowedButNotAWholeFrameShift()
    {
        var parser = new NativePointerProtocol(1920, 1080);
        parser.Parse("FRAME_CLOCK v1 0 8000000 7999000 1");
        Assert.NotNull(parser.Parse("FRAME_CLOCK v1 33000 8033333 8030000 2"));
        Assert.Throws<InvalidDataException>(() => parser.Parse("FRAME_CLOCK v1 66000 8100000 8090000 3"));
    }
}
