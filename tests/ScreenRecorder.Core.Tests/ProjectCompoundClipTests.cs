using System.Text.Json;
using ScreenRecorder.Core.Projects;
namespace ScreenRecorder.Core.Tests;

public class ProjectCompoundClipTests
{
    [Fact]
    public void InsertionAnchorInsideMergedClipUsesLogicalTicks()
    {
        var p = ProjectClipEditTests.Fixture();
        var history = new ProjectEditHistory(p);
        history.Apply(new ProjectClipEdit.Merge(p.Clips[0].Id, false));
        var anchor = ProjectInsertion.Anchor(history.Current, 25_000_000);
        Assert.Equal(25_000_000, anchor.SourcePts);
        var inserted = ProjectInsertion.Apply(history.Current, anchor,
            p.Sources[0] with { Id = Guid.NewGuid(), SessionId = "insert" }, Guid.NewGuid());
        Assert.Equal(25_000_000, ProjectTimeline.Build(inserted).Clips[0].EndTicks);
    }
    [Fact]
    public void MergeIsOneClipAndUndoRestoresSourcesAndIntervals()
    {
        var p = ProjectClipEditTests.Fixture();
        var history = new ProjectEditHistory(p);
        var edit = JsonSerializer.Deserialize<ProjectClipEdit>(
            $$"""{"kind":"merge","ClipId":"{{p.Clips[0].Id}}","WithPrevious":false}""")!;
        history.Apply(edit);
        Assert.Single(history.Current.Clips);
        Assert.Equal(50_000_000, ProjectTimeline.Build(history.Current).DurationTicks);
        Assert.Equal(8500, ProjectTimeline.Build(history.Current).Locate(25_000_000)!.SourcePts);
        Assert.Equal(p.Sources, history.Current.Sources);
        history.Undo();
        Assert.Equal(p.Clips, history.Current.Clips);
        history.Redo();
        history.Apply(new ProjectClipEdit.SplitAtTimeline(history.Current.Clips[0].Id, 30_000_000, Guid.NewGuid()));
        Assert.Equal(2, history.Current.Clips.Length);
        Assert.Equal(50_000_000, ProjectTimeline.Build(history.Current).DurationTicks);
        history.Apply(new ProjectClipEdit.RemoveRange(10_000_000, 40_000_000));
        Assert.Equal(20_000_000, ProjectTimeline.Build(history.Current).DurationTicks);
    }
}
