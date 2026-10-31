// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Platform.Linux;
namespace ScreenRecorder.Media.Tests;
public sealed class LinuxRecordingLimitsTests
{
    [Fact]
    public void OfferedFrameRatesAreAcceptedByTheLinuxNativeRequestRange()
    {
        Assert.NotEmpty(LinuxRecordingLimits.FpsOptions);
        Assert.All(LinuxRecordingLimits.FpsOptions,fps=>Assert.InRange(fps,1,30));
    }
    [Theory]
    [InlineData(40,20)] [InlineData(0,20)] [InlineData(5,5)] [InlineData(10,10)] [InlineData(20,20)] [InlineData(30,30)]
    public void SavedUnsupportedFrameRateFallsBackToVisibleDefault(int saved,int expected)=>Assert.Equal(expected,LinuxRecordingLimits.NormalizeFps(saved));
}
