// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using System.Collections.Immutable;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectTimelineTests
{
    [Fact]
    public void HalfOpenBoundaryMapsToNextSourceAndEndIsNotAFrame()
    {
        var p = ProjectClipEditTests.Fixture();
        var t = ProjectTimeline.Build(p);
        Assert.Equal(50_000_000, t.DurationTicks);
        Assert.Equal(5000, t.Locate(0)!.SourcePts);
        Assert.Equal(p.Clips[1].Id, t.Locate(20_000_000)!.ClipId);
        Assert.Equal(8000, t.Locate(20_000_000)!.SourcePts);
        Assert.Null(t.Locate(50_000_000)); Assert.Null(t.Locate(-1));
    }

    [Fact]
    public void NegativeSourcePtsArePreserved()
    {
        var p = ProjectClipEditTests.Fixture();
        p = p with { Sources = [p.Sources[0] with { Timing = new(new(1,1000), -5000, 6000) }],
            Clips = [p.Clips[0] with { InPts = -5000, OutPts = -3000 }] };
        var t = ProjectTimeline.Build(p);
        Assert.Equal(-4500, t.Locate(5_000_000)!.SourcePts);
    }

    [Fact]
    public void FractionalTicksDoNotAccumulateOneRoundingErrorPerClip()
    {
        var p = ProjectClipEditTests.Fixture();
        p = p with { Sources = [p.Sources[0] with { Timing = new(new(1,3), 0, 3) }],
            Clips = Enumerable.Range(0, 3).Select(i => p.Clips[0] with {
                Id = Guid.NewGuid(), InPts = i, OutPts = i + 1 }).ToImmutableArray() };
        Assert.Equal(10_000_000, ProjectTimeline.Build(p).DurationTicks);
    }

    [Fact]
    public void UnrepresentableTimelineIsRejectedInsteadOfWrapping()
    {
        var p = ProjectClipEditTests.Fixture();
        p = p with { Sources = [p.Sources[0] with { Timing = new(new(int.MaxValue,1), 0, long.MaxValue) }],
            Clips = [p.Clips[0] with { InPts = 0, OutPts = long.MaxValue }] };
        Assert.Throws<InvalidDataException>(() => ProjectTimeline.Build(p));
    }

    [Theory]
    [InlineData(166666, 0)]
    [InlineData(166667, 1)]
    [InlineData(333333, 1)]
    [InlineData(333334, 2)]
    public void SplitDoesNotChangeFrameTimestampAtFractionalTickBoundaries(long ticks, long expectedPts)
    {
        var p = ProjectClipEditTests.Fixture();
        p = p with { Sources = [p.Sources[0] with { Timing = new(new(1,60), 0, 3) }],
            Clips = [p.Clips[0] with { InPts = 0, OutPts = 3 }] };
        Assert.Equal(expectedPts, ProjectTimeline.Build(p).Locate(ticks)!.SourcePts);
        var history = new ProjectEditHistory(p);
        history.Apply(new ProjectClipEdit.Split(p.Clips[0].Id, 1, Guid.NewGuid()));
        Assert.Equal(expectedPts, ProjectTimeline.Build(history.Current).Locate(ticks)!.SourcePts);
    }
}
