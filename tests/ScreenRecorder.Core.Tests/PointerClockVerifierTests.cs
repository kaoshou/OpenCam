// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Models;

namespace ScreenRecorder.Core.Tests;

public sealed class PointerClockVerifierTests
{
    [Fact]
    public void CompleteVideoPtsSequenceDeterminesMuxOffsetNotProgressArrival()
    {
        var v = new PointerClockVerifier();
        v.Add(0, 21_000); v.Add(33_333, 54_000); v.Add(500_000, 521_000);
        Assert.Equal(21_000, v.Finish(3, 3));
    }
    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    [InlineData(0, 0)]
    public void IncompleteOrUnequalVideoSequencesDoNotGetAnAnchor(long native, long output)
    {
        var v = new PointerClockVerifier();
        Assert.Throws<InvalidDataException>(() => v.Finish(native, output));
    }
    [Fact]
    public void DriftOrRepeatedOutputPtsCannotBeSealed()
    {
        var v = new PointerClockVerifier(); v.Add(0, 0);
        Assert.Throws<InvalidDataException>(() => v.Add(33_333, 66_667));
        Assert.Throws<InvalidDataException>(() => v.Finish(1, 1));
        var duplicate = new PointerClockVerifier(); duplicate.Add(0, 0);
        Assert.Throws<InvalidDataException>(() => duplicate.Add(33_333, 0));
    }
}
