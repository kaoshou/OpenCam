// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectClipEditTests
{
    internal static RecordingProject Fixture()
    {
        var p = RecordingProjectTests.Example();
        var s = p.Sources[0] with { Timing = new(new(1, 1000), 5000, 6000) };
        return p with { Sources = [s], Clips = [
            new() { Id = Guid.NewGuid(), SourceId = s.Id, Name = "A", InPts = 5000, OutPts = 7000 },
            new() { Id = Guid.NewGuid(), SourceId = s.Id, Name = "B", InPts = 8000, OutPts = 11000 }
        ] };
    }

    [Fact]
    public void TrimCanRestoreHiddenSourceAndUndoExactly()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.Trim(p.Clips[0].Id, 5500, 6500));
        Assert.Equal(5500, h.Current.Clips[0].InPts);
        Assert.Equal(p.Sources, h.Current.Sources);
        h.Undo(); Assert.Equal(p.Clips, h.Current.Clips);
        h.Redo(); Assert.Equal(6500, h.Current.Clips[0].OutPts);
        h.Apply(new ProjectClipEdit.Trim(p.Clips[0].Id, 5000, 7000));
        Assert.Equal(p.Clips.ToArray(), h.Current.Clips.ToArray());
    }

    [Fact]
    public void SplitRejectsEndpointsAndRetainsDistinctIds()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        Assert.Throws<InvalidDataException>(() => h.Apply(new ProjectClipEdit.Split(p.Clips[0].Id, 5000, Guid.NewGuid())));
        Assert.Throws<InvalidDataException>(() => h.Apply(new ProjectClipEdit.Split(p.Clips[0].Id, 7000, Guid.NewGuid())));
        Assert.False(h.CanUndo);
        var right = Guid.NewGuid();
        h.Apply(new ProjectClipEdit.Split(p.Clips[0].Id, 6000, right));
        Assert.Equal(3, h.Current.Clips.Length);
        Assert.Equal(6000, h.Current.Clips[0].OutPts);
        Assert.Equal(6000, h.Current.Clips[1].InPts);
        Assert.Equal(right, h.Current.Clips[1].Id);
        h.Undo(); Assert.Equal(p.Clips, h.Current.Clips);
    }

    [Fact]
    public void CrossClipRangeDeletionIsOneUndoAndNeverDeletesSources()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.RemoveRange(10_000_000, 40_000_000));
        Assert.Equal(2, h.Current.Clips.Length);
        Assert.Equal(6000, h.Current.Clips[0].OutPts);
        Assert.Equal(10000, h.Current.Clips[1].InPts);
        Assert.Equal(p.Sources, h.Current.Sources);
        Assert.Equal(1, h.Current.Revision);
        h.Undo(); Assert.Equal(p.Clips, h.Current.Clips);
        Assert.False(h.CanUndo);
        h.Redo(); Assert.Equal(10000, h.Current.Clips[1].InPts);
    }

    [Fact]
    public void InteriorRangeKeepsBothSidesAndStableRedoIds()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.RemoveRange(5_000_000, 15_000_000));
        var edited = h.Current.Clips;
        Assert.Equal(3, edited.Length);
        Assert.Equal(5500, edited[0].OutPts);
        Assert.Equal(6500, edited[1].InPts);
        Assert.NotEqual(edited[0].Id, edited[1].Id);
        h.Undo(); h.Redo(); Assert.Equal(edited, h.Current.Clips);
    }

    [Fact]
    public void GroupMovesAsUnitAndCannotBeSplitByInsertion()
    {
        var p = Fixture();
        p = p with { Clips = p.Clips.Add(p.Clips[0] with { Id = Guid.NewGuid(), Name = "C" }) };
        var h = new ProjectEditHistory(p);
        var group = Guid.NewGuid();
        h.Apply(new ProjectClipEdit.Group(p.Clips[0].Id, p.Clips[1].Id, group));
        Assert.Throws<InvalidDataException>(() => h.Apply(new ProjectClipEdit.Move(p.Clips[2].Id, p.Clips[1].Id)));
        h.Apply(new ProjectClipEdit.Move(p.Clips[0].Id, null));
        Assert.Equal(new[] { "C", "A", "B" }, h.Current.Clips.Select(c => c.Name));
        h.Apply(new ProjectClipEdit.Ungroup(group));
        Assert.All(h.Current.Clips, c => Assert.Null(c.GroupId));
        h.Undo(); Assert.Equal(group, h.Current.Clips[1].GroupId);
    }

    [Fact]
    public void DeleteAllLeavesSourcesAndAppendSurvivesUndo()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.RemoveRange(0, 50_000_000));
        Assert.Empty(h.Current.Clips); Assert.Equal(p.Sources, h.Current.Sources);
        var appended = p.Clips[0] with { Id = Guid.NewGuid(), Name = "new recording" };
        h.AcceptRecordingAppend(h.Current with { Revision = 2, Clips = [appended] });
        h.Undo();
        Assert.Equal(3, h.Current.Clips.Length);
        Assert.Equal(appended, h.Current.Clips[2]);
    }

    [Fact]
    public void NoOpDoesNotConsumeRevisionAndInvalidEditIsAtomic()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.Trim(p.Clips[0].Id, 5000, 7000));
        h.Apply(new ProjectClipEdit.Move(p.Clips[0].Id, p.Clips[1].Id));
        Assert.Equal(0, h.Current.Revision); Assert.False(h.CanUndo);
        Assert.Throws<InvalidDataException>(() => h.Apply(new ProjectClipEdit.Trim(p.Clips[0].Id, 0, 1000)));
        Assert.Equal(p, h.Current);
        Assert.Throws<InvalidDataException>(() => h.Apply(new ProjectClipEdit.Split(p.Clips[0].Id, 6000, p.Clips[1].Id)));
        Assert.Equal(p, h.Current);
    }

    [Fact]
    public void SubPtsRangeDoesNotSplitOrConsumeUndo()
    {
        var p = Fixture();
        var h = new ProjectEditHistory(p);
        h.Apply(new ProjectClipEdit.RemoveRange(5_000_000, 5_000_001));
        Assert.Equal(p, h.Current);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void NonAdjacentGroupMembersAreRejectedAtProjectBoundary()
    {
        var p = Fixture();
        var id = Guid.NewGuid();
        p = p with { Clips = [p.Clips[0] with { GroupId = id }, p.Clips[1],
            p.Clips[0] with { Id = Guid.NewGuid(), GroupId = id }] };
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p));
    }
}
