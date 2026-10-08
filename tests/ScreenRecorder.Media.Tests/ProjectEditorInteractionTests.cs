// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Projects;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectEditorInteractionTests
{
    [Theory]
    [InlineData(1440,900,true,true)]
    [InlineData(1280,720,true,false)]
    [InlineData(1024,640,false,false)]
    public void ResponsiveLayoutKeepsPreviewAndTimelineUsable(double width, double height, bool left, bool right)
    {
        var layout = EditorLayout.ForSize(width, height);
        Assert.Equal(left, layout.LeftVisible);
        Assert.Equal(right, layout.RightVisible);
        Assert.Equal(240, layout.TimelineHeight);
        Assert.True(width - (left ? layout.LeftWidth : 0) - (right ? layout.RightWidth : 0) >= 500);
    }

    [Fact]
    public void DragCommitsOnlyLastProposalOnceAndEscapeCommitsNothing()
    {
        var id = Guid.NewGuid();
        var drag = new TimelineInteraction();
        drag.Begin(new ProjectClipEdit.Trim(id, 0, 1000));
        for (var i = 1; i <= 100; i++) drag.Update(new ProjectClipEdit.Trim(id, i, 1000));
        Assert.Equal(new ProjectClipEdit.Trim(id, 100, 1000), drag.Commit());
        Assert.Null(drag.Commit());
        drag.Begin(new ProjectClipEdit.Move(id, null));
        drag.Cancel();
        Assert.Null(drag.Commit());
    }

    [Fact]
    public void ClipSelectionAndPlayheadRemainIndependent()
    {
        var id = Guid.NewGuid();
        var selected = new EditorSelection(null, 12_000_000, null, null) with { ClipId = id };
        Assert.Equal(12_000_000, selected.PlayheadTicks);
        var playing = selected with { PlayheadTicks = 35_000_000 };
        Assert.Equal(id, playing.ClipId);
    }

    [Theory]
    [InlineData(5,100,TimelineHit.LeftTrim)]
    [InlineData(95,100,TimelineHit.RightTrim)]
    [InlineData(50,100,TimelineHit.Move)]
    public void EdgesHaveTenLogicalPixelTargets(double x, double width, TimelineHit expected)
        => Assert.Equal(expected, TimelineInteraction.HitTest(x, width));
}
