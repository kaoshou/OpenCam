using ScreenRecorder.Core.Models;
using ScreenRecorder.Core.Preflight;

namespace ScreenRecorder.Core.Tests;

public class RecordingPreflightTests
{
    [Fact]
    public void DisabledAndMissingSamplesCannotAppearLiveOrSilent()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(PreflightSourceState.Disabled, PreflightReadiness.Resolve(false, new(.5, .8, now), now).State);
        Assert.Equal(PreflightSourceState.Waiting, PreflightReadiness.Resolve(true, null, now).State);
    }

    [Theory]
    [InlineData(0, PreflightSourceState.Silent)]
    [InlineData(.1, PreflightSourceState.Live)]
    public void FreshSamplesDistinguishActualSilenceFromSound(double rms, PreflightSourceState expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(expected, PreflightReadiness.Resolve(true, new(rms, rms, now), now).State);
    }

    [Theory]
    [InlineData(-1001, .5)]
    [InlineData(1, .5)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void StaleFutureAndInvalidSamplesAreUnavailable(double milliseconds, double level)
    {
        var now = DateTimeOffset.UtcNow;
        var result = PreflightReadiness.Resolve(true, new(level, level, now.AddMilliseconds(milliseconds)), now);
        Assert.Equal(PreflightSourceState.Unavailable, result.State);
        Assert.Equal(0, result.Rms);
    }
}
