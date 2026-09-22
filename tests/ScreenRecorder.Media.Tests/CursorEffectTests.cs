// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class CursorEffectTests
{
    [Fact]
    public void BuildFFmpegArguments_WhenCursorHidden_ShouldIncludeDrawMouseZero()
    {
        var config = new RecordingConfiguration
        {
            Fps = 30,
            CursorEffect = CursorEffectMode.Hidden,
            AudioSource = AudioSourceType.None
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildInputArguments(
            config, 0, 0, 1920, 1080, useSynthetic: false, hasDirectShowMic: false);

        Assert.Contains("-draw_mouse 0", args);
        Assert.DoesNotContain("-draw_mouse 1", args);
    }

    [Theory]
    [InlineData(CursorEffectMode.Default)]
    [InlineData(CursorEffectMode.HighlightHalo)]
    [InlineData(CursorEffectMode.HaloWithClickRipple)]
    public void BuildFFmpegArguments_WhenCursorVisible_ShouldIncludeDrawMouseOne(CursorEffectMode mode)
    {
        var config = new RecordingConfiguration
        {
            Fps = 30,
            CursorEffect = mode,
            AudioSource = AudioSourceType.None
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildInputArguments(
            config, 0, 0, 1920, 1080, useSynthetic: false, hasDirectShowMic: false);

        Assert.Contains("-draw_mouse 1", args);
        Assert.DoesNotContain("-draw_mouse 0", args);
    }
}
