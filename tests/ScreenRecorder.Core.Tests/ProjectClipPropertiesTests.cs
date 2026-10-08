// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using ScreenRecorder.Core.Projects;

namespace ScreenRecorder.Core.Tests;

public sealed class ProjectClipPropertiesTests
{
    [Fact]
    public void PropertiesAreOneUndoAndRoundTripWithoutChangingSourceOrTrim()
    {
        var p = ProjectClipEditTests.Fixture();
        var h = new ProjectEditHistory(p);
        ProjectClipEdit edit = new ProjectClipEdit.Properties(p.Clips[0].Id, "Introduction",
            .65, true, 5_000_000, 2_500_000, 1.2, 4, 12, -10);
        var wire = JsonSerializer.Deserialize<ProjectClipEdit>(JsonSerializer.Serialize(edit))!;
        h.Apply(wire);
        var changed = h.Current;
        var clip = changed.Clips[0];
        Assert.Equal("Introduction", clip.Name);
        Assert.Equal(.65, clip.Volume);
        Assert.True(clip.Muted);
        Assert.Equal(5_000_000, clip.FadeInTicks);
        Assert.Equal(2_500_000, clip.FadeOutTicks);
        Assert.Equal(1.2, clip.Scale);
        Assert.Equal(4, clip.Crop);
        Assert.Equal(12, clip.PositionX);
        Assert.Equal(-10, clip.PositionY);
        Assert.Equal(p.Clips[0].InPts, clip.InPts);
        Assert.Equal(p.Clips[0].OutPts, clip.OutPts);
        Assert.Equal(p.Sources, changed.Sources);
        Assert.Equal(p.Clips[1], changed.Clips[1]);
        h.Apply(wire);
        Assert.Equal(changed.Revision, h.Current.Revision);
        h.Undo();
        Assert.Equal(p.Clips, h.Current.Clips);
        Assert.False(h.CanUndo);
        h.Redo();
        Assert.Equal(changed.Clips, h.Current.Clips);
    }

    [Theory]
    [InlineData("volume")]
    [InlineData("fade")]
    [InlineData("nan")]
    [InlineData("scale")]
    [InlineData("name")]
    [InlineData("missing")]
    public void InvalidPropertiesNeverPartiallyApplyOrConsumeUndo(string invalid)
    {
        var p = ProjectClipEditTests.Fixture();
        var h = new ProjectEditHistory(p);
        var edit = new ProjectClipEdit.Properties(p.Clips[0].Id, "Changed",
            .5, true, 0, 0, 1, 0, 0, 0);
        edit = invalid switch {
            "volume" => edit with { Volume = 3 },
            "fade" => edit with { FadeInTicks = 30_000_000 },
            "nan" => edit with { PositionX = double.NaN },
            "scale" => edit with { Scale = .5 },
            "name" => edit with { Name = "" },
            _ => edit with { ClipId = Guid.NewGuid() }
        };
        Assert.Throws<InvalidDataException>(() => h.Apply(edit));
        Assert.Equal(p, h.Current);
        Assert.False(h.CanUndo);
    }
}
