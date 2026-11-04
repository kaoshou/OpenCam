// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects.Editor;
namespace ScreenRecorder.Media.Tests;
public sealed class EditorTimelineMathTests
{
    [Theory]
    [InlineData(20,1,500000)]
    [InlineData(30,1,333334)]
    [InlineData(30000,1001,333667)]
    public void StepsUseAbsoluteRationalFrameBoundaries(long n,long d,long first)
    {
        var fps = new ProjectRational(n,d);
        Assert.Equal(first, EditorTimelineMath.StepFrame(0,1,100000000,fps));
        Assert.Equal(0, EditorTimelineMath.StepFrame(first,-1,100000000,fps));
        long ticks=0;
        for(var i=0;i<300;i++) ticks=EditorTimelineMath.StepFrame(ticks,1,200000000,fps);
        Assert.Equal(n==20?150000000:n==30?100000000:100100000,ticks);
        Assert.True(EditorTimelineMath.StepFrame(long.MaxValue,1,long.MaxValue,fps)<long.MaxValue);
        Assert.Equal(0,EditorTimelineMath.StepFrame(0,-1,1,fps));
    }
    [Fact]
    public void SnapUsesClosestValidTargetAndEarlierOnTie()
    {
        Assert.Equal(90,EditorTimelineMath.Snap(100,[110,90,-1,201],10,200));
        Assert.Equal(100,EditorTimelineMath.Snap(100,[110,90],9,200));
        Assert.Equal(110,EditorTimelineMath.Snap(106,[110,90],10,200));
    }
}
