// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectInsertionTests
{
    [Fact]
    public void InsertInsideClipIsOneUndoAndKeepsOriginalSources()
    {
        var project = Fixture();
        var history = new ProjectEditHistory(project);
        var anchor = ProjectInsertion.Anchor(project, TimeSpan.TicksPerSecond / 2);
        var added = Source("new");
        var operation = Guid.NewGuid();
        var committed = ProjectInsertion.Apply(project, anchor, added, operation);
        Assert.Equal(new[] { project.Clips[0].SourceId, added.Id, project.Clips[0].SourceId, project.Clips[1].SourceId }, committed.Clips.Select(c => c.SourceId));
        Assert.Equal(2500, committed.Clips[0].OutPts);
        Assert.Equal(2500, committed.Clips[2].InPts);
        Assert.Equal(3, committed.Sources.Length);
        history.AcceptRecordingInsertion(committed);
        history.Undo();
        Assert.Equal(project.Clips, history.Current.Clips);
        Assert.Contains(added, history.Current.Sources); // Undo never discards captured media.
        Assert.False(history.CanUndo);
        history.Redo();
        Assert.Equal(committed.Clips, history.Current.Clips);
    }

    [Fact]
    public void InsertionInsideGroupRemainsContiguousAndStaleAnchorDoesNotMutate()
    {
        var project = Fixture();
        var group = Guid.NewGuid();
        project = project with { Clips = [..project.Clips.Select(c => c with { GroupId = group })] };
        var anchor = ProjectInsertion.Anchor(project, TimeSpan.TicksPerSecond);
        var source = Source("new");
        var result = ProjectInsertion.Apply(project, anchor, source, Guid.NewGuid());
        Assert.Equal(3, result.Clips.Length);
        Assert.All(result.Clips, clip => Assert.Equal(group, clip.GroupId));
        Assert.Throws<InvalidOperationException>(() => ProjectInsertion.Apply(project with { Revision = project.Revision + 1 }, anchor, source, Guid.NewGuid()));
        Assert.Equal(2, project.Clips.Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20000000, 2)]
    public void BoundaryInsertionDoesNotCreateEmptySplit(long ticks, int index)
    {
        var project = Fixture();
        var source = Source("new");
        var result = ProjectInsertion.Apply(project, ProjectInsertion.Anchor(project, ticks), source, Guid.NewGuid());
        Assert.Equal(3, result.Clips.Length);
        Assert.Equal(source.Id, result.Clips[index].SourceId);
        Assert.All(result.Clips, c => Assert.True(c.OutPts > c.InPts));
    }

    private static ProjectSource Source(string session) => new() { Id = Guid.NewGuid(), SessionId = session,
        RelativePath = $"sources/{session}.mkv", FileSize = 1, Sha256 = new('a', 64), Width = 64, Height = 36,
        VideoCodec = "h264", Timing = new(new(1, 1000), 2000, 1000) };

    private static RecordingProject Fixture()
    {
        var a = Source("a"); var b = Source("b");
        return new() { ProjectId = Guid.NewGuid(), Name = "Insert", Sessions = ["a", "b"], Sources = [a, b],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = a.Id, Name = "A", InPts = 2000, OutPts = 3000 },
                new() { Id = Guid.NewGuid(), SourceId = b.Id, Name = "B", InPts = 2000, OutPts = 3000 }] };
    }
}
