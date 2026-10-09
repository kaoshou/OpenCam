// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.Media.Projects;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectStillSelectionTests
{
    internal static RecordingProject Fixture()
    {
        var id = Guid.NewGuid();
        return new() { ProjectId = Guid.NewGuid(), Name = "Boundary", Sessions = ["s"],
            Canvas = new(64, 36, new(30, 1)), Sources = [new() { Id = id, SessionId = "s",
                RelativePath = "sources/a.mkv", FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36,
                VideoCodec = "h264", Timing = new(new(1, 1000), 0, 1000) }],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = id, Name = "A", InPts = 0, OutPts = 10 },
                new() { Id = Guid.NewGuid(), SourceId = id, Name = "B", InPts = 10, OutPts = 20 }] };
    }

    [Fact]
    public void SubFrameClip_StillUsesOutputOwner_ThumbnailUsesItsOwnSource()
    {
        var project = Fixture();
        var still = ProjectStillSelection.Create(project, 100000);
        Assert.Equal(project.Clips[0].Id, still.Plan.Clips[still.Index].Clip.Id);
        Assert.Equal(0, still.Frame);
        var card = ProjectStillSelection.Create(project, 100000, project.Clips[1].Id);
        Assert.Equal(project.Clips[1].Id, card.Plan.Clips[card.Index].Clip.Id);
        Assert.Equal(1, card.Plan.FrameCount);
        Assert.Equal(512 * 288 * 4, ProjectMediaJob.PreviewStill(card.Plan, card.Index, card.Frame).ExpectedOutputBytes);
    }

    [Theory]
    [InlineData(1919, 1079)]
    [InlineData(5000, 3000)]
    public void PreviewDoesNotInheritH264CanvasRestrictions(int width, int height)
    {
        var project = Fixture() with { Canvas = new(width, height, new(30, 1)) };
        Assert.Equal(512 * 288 * 4, ProjectMediaJob.PreviewStill(ProjectRenderPlan.Create(project), 0, 0).ExpectedOutputBytes);
    }
}
