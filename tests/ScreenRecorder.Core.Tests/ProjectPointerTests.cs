// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectPointerTests
{
    private static RecordingProject Project()
    {
        var id = Guid.NewGuid();
        return new() { SchemaVersion = 5, ProjectId = Guid.NewGuid(), Name = "pointer", Sessions = ["session"],
            Sources = [new() { Id = id, SessionId = "session", RelativePath = "sources/test.mkv",
                FileSize = 100, Sha256 = new string('a', 64), Width = 1920, Height = 1080, VideoCodec = "h264",
                Timing = new(new(1,1000), 0, 1000),
                Pointer = new($"pointers/{id:N}.ocpt", 485, new string('b',64), 0, true) }],
            Clips = [new() { Id = Guid.NewGuid(), SourceId = id, InPts = 0, OutPts = 1000, Name = "clip" }] };
    }

    [Fact]
    public void PointerNeedsNewSchemaAndSafeBoundReference()
    {
        var p = Project(); ProjectValidation.Validate(p);
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with { SchemaVersion = 4 }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with { Sources = [p.Sources[0] with {
            Pointer = p.Sources[0].Pointer! with { RelativePath = "../external.ocpt" } }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with { PointerSettings = new(ProjectPointerStyle.Stick, 201) }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with { PointerSettings = new((ProjectPointerStyle)99, 100) }));
    }

    [Fact]
    public void StyleChangesParticipateInUndoSaveAndExportIdentity()
    {
        var p = Project(); var history = new ProjectEditHistory(p); var saved = new ProjectEditSaveState(p);
        var original = ProjectExportFingerprint.Compute(p, ProjectExportFingerprint.Profile);
        history.SetPointerSettings(new(ProjectPointerStyle.Dot, 150));
        Assert.True(saved.IsDirty(history.Current));
        Assert.NotEqual(original, ProjectExportFingerprint.Compute(history.Current, ProjectExportFingerprint.Profile));
        history.Undo(); Assert.False(saved.IsDirty(history.Current));
        Assert.Equal(original, ProjectExportFingerprint.Compute(history.Current, ProjectExportFingerprint.Profile));
        history.Redo(); Assert.Equal(ProjectPointerStyle.Dot, history.Current.PointerSettings.Style);
        Assert.Equal(p.PointerSettings, saved.DiscardEdits(history.Current).PointerSettings);
        Assert.Throws<InvalidDataException>(() => history.AcceptSavedMetadata(history.Current with { PointerSettings = p.PointerSettings }));
    }

    [Fact]
    public void AddingSubtitlesNeverDowngradesPointerProject()
    {
        var p = Project(); var history = new ProjectEditHistory(p); var id = Guid.NewGuid();
        history.SetSubtitles(new([new(id, "English", "en", $"subtitles/{id:N}.vtt", 100,
            new string('a', 64), new string('b', 64))], id));
        Assert.Equal(5, history.Current.SchemaVersion);
        history.Undo(); Assert.Equal(5, history.Current.SchemaVersion);
    }

    [Fact]
    public void InsertingPointerSourceUpgradesLegacyProjectWithoutLosingSettings()
    {
        var source = Project().Sources[0];
        var legacy = new RecordingProject { SchemaVersion = 4, ProjectId = Guid.NewGuid(), Name = "old" };
        var result = ProjectInsertion.Apply(legacy, ProjectInsertion.Anchor(legacy, 0), source, Guid.NewGuid());
        Assert.Equal(5, result.SchemaVersion);
        Assert.Equal(source.Pointer, result.Sources.Single().Pointer);
        Assert.Equal(legacy.PointerSettings, result.PointerSettings);
        Assert.True(ProjectPointers.HasVisiblePointer(result));
    }

    [Fact]
    public void LegacyProjectsRemainValidAndHaveNoEditablePointerSources()
    {
        foreach (var version in new[] {1,3,4})
        {
            var p = new RecordingProject { SchemaVersion = version, ProjectId = Guid.NewGuid(), Name = "old" };
            ProjectValidation.Validate(p); Assert.False(ProjectPointers.HasVisiblePointer(p));
        }
        var current = Project(); Assert.True(ProjectPointers.HasVisiblePointer(current));
        Assert.False(ProjectPointers.HasVisiblePointer(current with { PointerSettings = new(ProjectPointerStyle.Hidden, 100) }));
    }
}
