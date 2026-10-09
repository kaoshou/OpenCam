using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectEditSaveStateTests
{
    private static RecordingProject Project() => new() { ProjectId = Guid.NewGuid(), Name = "original" };

    [Fact]
    public void EditDoesNotAlterSavedBaseline()
    {
        var original = Project();
        var state = new ProjectEditSaveState(original);
        var history = new ProjectEditHistory(original);
        history.RenameProject("changed");
        Assert.True(state.IsDirty(history.Current));
        Assert.Equal("original", state.DiscardEdits(history.Current).Name);
        state.AcceptSaved(history.Current);
        Assert.False(state.IsDirty(history.Current));
    }

    [Fact]
    public void UndoToSavedContentIsClean()
    {
        var original = Project();
        var state = new ProjectEditSaveState(original);
        var history = new ProjectEditHistory(original);
        history.RenameProject("changed");
        history.Undo();
        Assert.True(history.Current.Revision > original.Revision);
        Assert.False(state.IsDirty(history.Current));
        history.Redo();
        Assert.True(state.IsDirty(history.Current));
    }

    [Fact]
    public void DiscardPreservesSourcesAndSessions()
    {
        var original = Project();
        var source = new ProjectSource { Id = Guid.NewGuid(), SessionId = "new", RelativePath = "sources/new.mkv",
            Sha256 = new string('a', 64), FileSize = 10, Width = 1920, Height = 1080,
            VideoCodec = "h264", Timing = new(new(1, 1000), 0, 1000) };
        var saved = original with { Sources = [source], Sessions = ["new"],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = source.Id, OutPts = 1000, Name = "take" }] };
        var state = new ProjectEditSaveState(saved);
        var working = saved with { Revision = 10, Name = "unsaved", Clips = [] };
        var discarded = state.DiscardEdits(working);
        Assert.Equal("original", discarded.Name);
        Assert.Equal(saved.Clips, discarded.Clips);
        Assert.Equal(working.Sources, discarded.Sources);
        Assert.Equal(working.Sessions, discarded.Sessions);
        Assert.True(discarded.Revision > working.Revision);
    }

    [Fact]
    public void LegacyProjectDefaultsToAutoExport()
    {
        var legacy = JsonSerializer.Deserialize<RecordingProject>("{\"Name\":\"old\"}")!;
        Assert.True(legacy.AutoExportOnStop);
    }

    [Fact]
    public void PolicyAndViewStateDoNotMakeEditsDirty()
    {
        var saved = Project();
        var state = new ProjectEditSaveState(saved);
        Assert.False(state.IsDirty(saved with { AutoExportOnStop = false, ViewState = new(500), Revision = 9 }));
        Assert.Throws<InvalidDataException>(() => state.DiscardEdits(saved with { ProjectId = Guid.NewGuid() }));
    }
}
