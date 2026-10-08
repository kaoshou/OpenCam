// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectRenderPlanTests
{
    [Fact]
    public void FractionalClips_QuantizeGlobalBoundaries_NotEachDuration()
    {
        var project = Make(300, 1, new(1001, 30000));
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(301, plan.FrameCount);
        Assert.Equal(480480, plan.AudioSampleCount);
        Assert.Equal(100100000, plan.DurationTicks);
        Assert.Equal(2, plan.Clips[0].EndFrame);
        for (var i = 1; i < plan.Clips.Length; i++)
        {
            Assert.Equal(plan.Clips[i - 1].EndFrame, plan.Clips[i].StartFrame);
            Assert.Equal(plan.Clips[i - 1].EndAudioSample, plan.Clips[i].StartAudioSample);
        }
        Assert.Equal(301, plan.Clips.Sum(c => c.EndFrame - c.StartFrame));
    }

    [Theory]
    [InlineData(2000, 2200, 9600)]
    [InlineData(-2000, -1800, 9600)]
    [InlineData(2000, 1800, -9600)]
    public void AudioOffset_UsesVideoTrimOrigin_NotAudioStart(long origin, long audioPts, long expected)
    {
        var project = Make(1, 1000, new(1, 1000), origin);
        var clip = ProjectRenderPlan.Create(project).Clips[0];
        Assert.Equal(expected, clip.AudioSampleOffset(audioPts, new(1, 1000)));
    }

    [Fact]
    public void Snapshot_PreservesOrderSourceIdentityAndProperties()
    {
        var project = Make(2, 1000, new(1, 1000));
        var edited = project.Clips[1] with { Muted = true, Volume = .4, Scale = 1.5 };
        project = project with { Clips = [edited, project.Clips[0]] };
        var plan = ProjectRenderPlan.Create(project);
        project = project with { Clips = [] };
        Assert.Equal(2, plan.Clips.Length);
        Assert.Equal(edited, plan.Clips[0].Clip);
        Assert.Equal(edited.SourceId, plan.Clips[0].Source.Id);
        Assert.Equal(60, plan.FrameCount);
    }

    [Fact]
    public void EmptyTimeline_IsAValidZeroLengthPlan()
    {
        var plan = ProjectRenderPlan.Create(Make(0, 1, new(1, 30)));
        Assert.Empty(plan.Clips);
        Assert.Equal(0, plan.FrameCount);
        Assert.Equal(0, plan.AudioSampleCount);
    }

    [Fact]
    public void FrameLookup_UsesExactGlobalTime_AndHalfOpenBoundaries()
    {
        var project = Make(2, 1000, new(1, 1000), 2000);
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(new ProjectPosition(project.Clips[0].Id, project.Sources[0].Id, 2966),
            plan.LocateVideoFrame(29));
        Assert.Equal(new ProjectPosition(project.Clips[1].Id, project.Sources[0].Id, 2000),
            plan.LocateVideoFrame(30));
        Assert.Null(plan.LocateVideoFrame(-1));
        Assert.Null(plan.LocateVideoFrame(60));
    }

    [Fact]
    public void FrameLookup_SkipsSubFrameClipsWithoutInventingFrames()
    {
        var project = Make(100, 1, new(1, 1000));
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(3, plan.FrameCount);
        Assert.Equal(project.Clips[0].Id, plan.LocateVideoFrame(0)!.ClipId);
        Assert.Equal(project.Clips[33].Id, plan.LocateVideoFrame(1)!.ClipId);
        Assert.Equal(project.Clips[66].Id, plan.LocateVideoFrame(2)!.ClipId);
    }

    [Fact]
    public void AudioPlacement_PreservesFractionalGlobalSamplePhase()
    {
        var plan = ProjectRenderPlan.Create(Make(3, 1, new(1, 100000)));
        // Each clip lasts 0.48 samples. The second starts at exact sample 0.48,
        // while its first owned output sample is index 1.
        Assert.Equal(1, plan.Clips[1].StartAudioSample);
        Assert.Equal(0, plan.Clips[1].AudioSampleOffset(1, new(1, 100000)));
        Assert.Equal(1, plan.Clips[2].AudioSampleOffset(1, new(1, 100000)));
    }

    [Fact]
    public void FractionalOutputRate_MatchesExactDurationWithoutTickRoundTrip()
    {
        var project = Make(300, 1, new(1001, 30000)) with
        { Canvas = new(1920, 1080, new(30000, 1001)) };
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(300, plan.FrameCount);
        Assert.Equal(299, plan.Clips[299].StartFrame);
    }

    [Fact]
    public void InvalidClip_IsRejectedBeforePlanning()
    {
        var project = Make(1, 1000, new(1, 1000));
        Assert.Throws<InvalidDataException>(() => ProjectRenderPlan.Create(project with
        { Clips = [project.Clips[0] with { OutPts = 1001 }] }));
    }

    [Fact]
    public void MixedTimeBases_AgreeWithCanonicalTimelineAfterReorderingAndTrimming()
    {
        var first = Make(100, 97, new(1001, 30000), -9000);
        var second = Make(100, 4321, new(1, 90000), 180000);
        var project = first with {
            Sources = [first.Sources[0], second.Sources[0]],
            Clips = first.Clips.Zip(second.Clips, (a, b) => new[] {
                b with { InPts = b.InPts + 23, OutPts = b.OutPts - 100 },
                a with { InPts = a.InPts + 3, OutPts = a.OutPts - 7 }
            }).SelectMany(pair => pair).ToImmutableArray()
        };
        var canonical = ProjectTimeline.Build(project);
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(canonical.DurationTicks, plan.DurationTicks);
        for (var i = 0; i < plan.Clips.Length; i++)
        {
            Assert.Equal(canonical.Clips[i].StartTicks, plan.Clips[i].StartTicks);
            Assert.Equal(canonical.Clips[i].EndTicks, plan.Clips[i].EndTicks);
        }
        for (long frame = 0; frame < plan.FrameCount; frame++)
        {
            var position = plan.LocateVideoFrame(frame)!;
            var clip = project.Clips.Single(c => c.Id == position.ClipId);
            Assert.InRange(position.SourcePts, clip.InPts, clip.OutPts - 1);
        }
    }

    [Fact]
    public void ExtremeNegativeSourceOrigin_DoesNotOverflowIntermediateMath()
    {
        var project = Make(1, 1000, new(1, 1000), long.MinValue);
        var plan = ProjectRenderPlan.Create(project);
        Assert.Equal(long.MinValue + 966, plan.LocateVideoFrame(29)!.SourcePts);
        Assert.Equal(4800, plan.Clips[0].AudioSampleOffset(long.MinValue + 100, new(1, 1000)));
    }

    [Fact]
    public void UnrepresentableOutputFrameCount_IsRejected()
    {
        var project = Make(1, 1_000_000_000, new(1, 1)) with {
            Canvas = new(1920, 1080, new(int.MaxValue, 1))
        };
        // Valid UI duration, but not a practical frame rate; increase duration to overflow frames.
        project = project with { Sources = [project.Sources[0] with {
            Timing = new(new(1, 1), 0, 10_000_000_000) }],
            Clips = [project.Clips[0] with { OutPts = 10_000_000_000 }] };
        Assert.Throws<InvalidDataException>(() => ProjectRenderPlan.Create(project));
    }

    private static RecordingProject Make(int count, long duration, ProjectRational timeBase, long origin = 0)
    {
        var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "session",
            RelativePath = "sources/source.mkv", FileSize = 1, Sha256 = new string('a', 64),
            Width = 1920, Height = 1080, VideoCodec = "h264", AudioCodec = "aac",
            Timing = new(timeBase, origin, duration) };
        return new RecordingProject { ProjectId = Guid.NewGuid(), Name = "Test",
            Sessions = ["session"], Sources = [source],
            Clips = Enumerable.Range(0, count).Select(i => new ProjectClip {
                Id = Guid.NewGuid(), SourceId = source.Id, Name = $"Clip {i}",
                InPts = origin, OutPts = origin + duration }).ToImmutableArray() };
    }
}
