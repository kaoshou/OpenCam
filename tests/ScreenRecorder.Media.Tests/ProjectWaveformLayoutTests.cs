// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Headless.XUnit;
using ScreenRecorder.UI.Projects.Editor;

namespace ScreenRecorder.Media.Tests;

public sealed class ProjectWaveformLayoutTests
{
    [AvaloniaTheory]
    [InlineData(132)]
    [InlineData(144)]
    [InlineData(176)]
    [InlineData(210)]
    public void AudioTrackRemainsVisibleBelowVideoWhenToolbarWraps(int height)
    {
        var timeline = new ProjectTimelineControl();
        timeline.Measure(new Size(1000, height));
        timeline.Arrange(new Rect(0, 0, 1000, height));
        Assert.True(timeline.VideoTrackBounds.Height >= 36);
        Assert.True(timeline.AudioTrackBounds.Height >= 24);
        Assert.True(timeline.AudioTrackBounds.Top >= timeline.VideoTrackBounds.Bottom + 4);
        Assert.True(timeline.AudioTrackBounds.Bottom <= timeline.Bounds.Height);
    }
}
