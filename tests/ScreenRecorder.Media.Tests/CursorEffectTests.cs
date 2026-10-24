// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Media.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class CursorEffectTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void PresentationStickCanBeSelectedAndPassedToRecording()
    {
        var main = new ScreenRecorder.UI.ViewModels.MainViewModel(forScreenshot: true);
        try
        {
            var stick = Assert.Single(main.AvailableCursorEffects, o => o.Mode == CursorEffectMode.PresentationStick);
            main.SelectedCursorEffect = stick;
            Assert.Equal(CursorEffectMode.PresentationStick, main.BuildProjectConfiguration().CursorEffect);
        }
        finally { main.Cleanup(); }
    }

    [Theory]
    [InlineData(CursorEffectMode.Hidden)]
    [InlineData(CursorEffectMode.PresentationStick)]
    public void BuildFFmpegArguments_WhenCursorReplacedOrHidden_ShouldIncludeDrawMouseZero(CursorEffectMode mode)
    {
        var config = new RecordingConfiguration
        {
            Fps = 30,
            CursorEffect = mode,
            AudioSource = AudioSourceType.None
        };

        var args = new ScreenRecorder.Platform.Windows.WindowsFFmpegProvider().BuildInputArguments(
            config, 0, 0, 1920, 1080, useSynthetic: false, hasDirectShowMic: false);

        Assert.Contains("-draw_mouse 0", args);
        Assert.DoesNotContain("-draw_mouse 1", args);
    }

    [Theory]
    [InlineData(TeachingPointerStyle.WarmIvory, TeachingPointerSize.Standard)]
    [InlineData(TeachingPointerStyle.TealSilver, TeachingPointerSize.Small)]
    [InlineData(TeachingPointerStyle.RoundShort, TeachingPointerSize.Large)]
    [InlineData(TeachingPointerStyle.HighContrast, TeachingPointerSize.Large)]
    public void LiveWindowsStickUsesSelectedSharedArtworkAndPremultipliedBgra(TeachingPointerStyle style, TeachingPointerSize size)
    {
        var options=new TeachingPointerOptions(style,size);
        var artwork=TeachingPointerArtwork.Get(options);
        var rgba=artwork.Pixels.ToArray();
        var bgra = Enumerable.Repeat((byte)123, rgba.Length).ToArray();
        typeof(ScreenRecorder.Platform.Windows.Cursor.WindowsCursorHighlightService)
            .GetMethod("RenderStick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [bgra, options]);
        var artifact = Environment.GetEnvironmentVariable("OPENCAM_POINTER_RENDER_ARTIFACT");
        if (!string.IsNullOrEmpty(artifact)) File.WriteAllBytes(artifact, bgra);
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var alpha=rgba[i+3];
            Assert.Equal((byte)((rgba[i+2]*alpha+127)/255),bgra[i]);
            Assert.Equal((byte)((rgba[i+1]*alpha+127)/255),bgra[i+1]);
            Assert.Equal((byte)((rgba[i]*alpha+127)/255),bgra[i+2]);
            Assert.Equal(alpha,bgra[i+3]);
            Assert.True(bgra[i] <= bgra[i + 3] && bgra[i + 1] <= bgra[i + 3] && bgra[i + 2] <= bgra[i + 3]);
        }
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
