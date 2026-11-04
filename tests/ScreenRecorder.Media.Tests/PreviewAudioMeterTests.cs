// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using ScreenRecorder.Media.Projects;
namespace ScreenRecorder.Media.Tests;
public sealed class PreviewAudioMeterTests
{
    [Fact]
    public void RealPcmProducesFinitePeakAndRmsAndRejectsPartialStereo()
    {
        var meter=new PreviewAudioMeter();
        meter.Observe(MemoryMarshal.AsBytes<float>(new float[]{.5f,-.5f,.5f,-.5f}));
        var level=meter.SnapshotAndReset();
        Assert.NotNull(level); Assert.Equal(.5,level!.Value.Peak); Assert.Equal(.5,level.Value.Rms);
        Assert.False(level.Value.Overload); Assert.Null(meter.SnapshotAndReset());
        meter.Observe(MemoryMarshal.AsBytes<float>(new float[]{float.NaN,float.PositiveInfinity,2,-2}));
        level=meter.SnapshotAndReset();
        Assert.True(level!.Value.Overload); Assert.InRange(level.Value.Peak,0,1); Assert.InRange(level.Value.Rms,0,1);
        Assert.Throws<ArgumentException>(()=>meter.Observe(new byte[3]));
    }
}
