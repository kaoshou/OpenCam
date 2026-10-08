// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Immutable;
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public class RecordingProjectTests
{
    // Detect lost source timing, clip properties, or schema identity during serialization.
    [Fact]
    public void RoundTripPreservesTimingAndEditableProperties()
    {
        var project = Example();
        var loaded = JsonSerializer.Deserialize<RecordingProject>(JsonSerializer.Serialize(project))!;
        ProjectValidation.Validate(loaded);
        Assert.Equal(project.ProjectId, loaded.ProjectId);
        Assert.Equal(90000, loaded.Sources[0].Timing.TimeBase.Denominator);
        Assert.Equal(4500, loaded.Sources[0].Timing.StartPts);
        Assert.Equal(904500, loaded.Clips[0].OutPts);
        Assert.Equal(0.75, loaded.Clips[0].Volume);
        Assert.Equal(1.2, loaded.Clips[0].Scale);
        Assert.Equal(5, loaded.Clips[0].PositionX);
        Assert.Equal("講解", loaded.Clips[0].Name);
    }

    [Theory]
    [InlineData("unknown-source")]
    [InlineData("empty-range")]
    [InlineData("outside-source")]
    [InlineData("nonfinite")]
    [InlineData("duplicate-source")]
    [InlineData("duplicate-clip")]
    [InlineData("future-schema")]
    [InlineData("invalid-timebase")]
    [InlineData("overflow-timing")]
    [InlineData("null-clip")]
    public void InvalidProjectsAreRejected(string reason)
    {
        var p = Example();
        p = reason switch
        {
            "unknown-source" => p with { Clips = [p.Clips[0] with { SourceId = Guid.NewGuid() }] },
            "empty-range" => p with { Clips = [p.Clips[0] with { OutPts = 4500 }] },
            "outside-source" => p with { Clips = [p.Clips[0] with { InPts = 0 }] },
            "nonfinite" => p with { Clips = [p.Clips[0] with { Volume = double.NaN }] },
            "duplicate-source" => p with { Sources = [p.Sources[0], p.Sources[0]] },
            "duplicate-clip" => p with { Clips = [p.Clips[0], p.Clips[0]] },
            "future-schema" => p with { SchemaVersion = 2 },
            "invalid-timebase" => p with { Sources = [p.Sources[0] with { Timing = new(new(1, 0), 4500, 900000) }] },
            "overflow-timing" => p with { Sources = [p.Sources[0] with { Timing = new(new(1, 90000), long.MaxValue, 1) }] },
            "null-clip" => p with { Clips = [null!] },
            _ => throw new ArgumentException(reason)
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p));
    }

    [Fact]
    public void ManifestLimitsRejectOversizedCollectionsNamesAndPaths()
    {
        var p = Example();
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with { Name = new string('x', 201) }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with {
            Sources = Enumerable.Range(0, 10001).Select(_ => p.Sources[0] with { Id = Guid.NewGuid() }).ToImmutableArray()
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with {
            Clips = Enumerable.Range(0, 10001).Select(_ => p.Clips[0] with { Id = Guid.NewGuid() }).ToImmutableArray()
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with {
            Sessions = Enumerable.Range(0, 1001).Select(i => "session-" + i).ToImmutableArray()
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p with {
            Sources = [p.Sources[0] with { RelativePath = new string('a', 1025) }]
        }));
    }

    [Fact]
    public void LargeFiniteTimingDoesNotOverflowDuringValidation()
    {
        var p = Example();
        p = p with {
            Sources = [p.Sources[0] with { Timing = new(new(int.MaxValue, 1), 0, long.MaxValue) }],
            Clips = [p.Clips[0] with { InPts = 0, OutPts = long.MaxValue }]
        };
        ProjectValidation.Validate(p);
    }

    // Detect false "saved" status when an earlier write completes after a newer edit.
    [Fact]
    public void EarlierSaveReceiptDoesNotClearNewerEdits()
    {
        var id = Guid.NewGuid();
        var state = new ProjectSaveState(id, 2);
        state.MarkDirty(3);
        state.BeginSave(3);
        state.MarkDirty(4);
        state.Complete(new(id, 3, DateTimeOffset.UtcNow));
        Assert.True(state.IsDirty);
        Assert.Equal(3, state.SavedRevision);
        Assert.Equal(4, state.DirtyRevision);
        state.BeginSave(4);
        state.Fail("disk full");
        Assert.True(state.IsDirty);
        Assert.Equal("disk full", state.Error);
        state.BeginSave(4);
        state.Complete(new(id, 4, DateTimeOffset.UtcNow));
        Assert.False(state.IsDirty);
        Assert.Null(state.Error);
    }

    [Fact]
    public void ForeignReceiptCannotMarkProjectSaved()
    {
        var state = new ProjectSaveState(Guid.NewGuid(), 0);
        state.MarkDirty(1);
        state.BeginSave(1);
        state.Complete(new(Guid.NewGuid(), 1, DateTimeOffset.UtcNow));
        Assert.True(state.IsDirty);
        Assert.Equal(0, state.SavedRevision);
    }

    // Detect destructive undo, revision reuse, and loss of undo history on save.
    [Fact]
    public void RenameUndoRedoRetainsSourcesAndUsesFreshRevisions()
    {
        var p = Example();
        var history = new ProjectEditHistory(p);
        history.RenameClip(p.Clips[0].Id, "修正版");
        Assert.Equal(1, history.Current.Revision);
        var saves = new ProjectSaveState(p.ProjectId, 0);
        saves.MarkDirty(1);
        saves.BeginSave(1);
        saves.Complete(new(p.ProjectId, 1, DateTimeOffset.UtcNow));
        history.Undo();
        Assert.Equal("講解", history.Current.Clips[0].Name);
        Assert.Equal(2, history.Current.Revision);
        Assert.Equal(p.Sources, history.Current.Sources);
        history.Redo();
        Assert.Equal("修正版", history.Current.Clips[0].Name);
        Assert.Equal(3, history.Current.Revision);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NewEditClearsRedoButReopeningDoesNotInventHistory()
    {
        var p = Example();
        var history = new ProjectEditHistory(p);
        history.RenameClip(p.Clips[0].Id, "新版");
        history.Undo();
        history.RenameClip(p.Clips[0].Id, "另一版");
        Assert.False(history.CanRedo);
        var reopened = new ProjectEditHistory(history.Current);
        Assert.False(reopened.CanUndo);
        Assert.False(reopened.CanRedo);
        Assert.Equal("另一版", reopened.Current.Clips[0].Name);
    }

    [Fact]
    public void InvalidOrNoOpRenameDoesNotCreateHistory()
    {
        var p = Example();
        var h = new ProjectEditHistory(p);
        h.RenameClip(p.Clips[0].Id, "講解");
        Assert.False(h.CanUndo);
        Assert.Throws<InvalidDataException>(() => h.RenameClip(p.Clips[0].Id, ""));
        Assert.Throws<InvalidDataException>(() => h.RenameClip(Guid.NewGuid(), "其他"));
        Assert.Equal(0, h.Current.Revision);
    }

    internal static RecordingProject Example()
    {
        var source = new ProjectSource
        {
            Id = Guid.NewGuid(), SessionId = "session-1",
            RelativePath = "sessions/Sessions/session-1/segment_000.mkv",
            FileSize = 1234, Sha256 = new string('a', 64),
            Timing = new(new(1, 90000), 4500, 900000),
            Width = 1920, Height = 1080, VideoCodec = "h264", AudioCodec = "aac"
        };
        return new RecordingProject
        {
            ProjectId = Guid.NewGuid(), Name = "教學", Canvas = new(1920, 1080, new(30, 1)),
            Sources = [source], Sessions = ["session-1"],
            Clips = [new ProjectClip { Id = Guid.NewGuid(), SourceId = source.Id, Name = "講解",
                InPts = 4500, OutPts = 904500, Volume = 0.75, Scale = 1.2, PositionX = 5 }]
        };
    }
}
