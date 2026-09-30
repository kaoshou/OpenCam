// SPDX-License-Identifier: AGPL-3.0-or-later
using ScreenRecorder.Core.Enums;
using ScreenRecorder.Core.Interfaces;
using ScreenRecorder.Core.Models;
using ScreenRecorder.Platform.Windows;
using ScreenRecorder.Platform.Windows.Capture;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class WindowsCaptureArgumentsTests
{
    private static readonly CaptureRegion Screen = new(-1920, 0, 1920, 1080);
    private sealed class Catalog : IDxgiOutputCatalog
    {
        public IReadOnlyList<DxgiOutputInfo> GetOutputs() => [new(7, 2, "LEFT", Screen, true, true, true)];
    }
    private static WindowsCapturePlanProvider Provider() => new(new WindowsFFmpegProvider(), new Catalog(),
        () => [new MonitorInfo(0, "LEFT", Screen, true, 1.25)], _ => Task.FromResult(true));
    private static RecordingConfiguration Config => new() { WindowsCaptureMode = WindowsCaptureMode.ModernExperimental,
        CaptureSource = CaptureSourceType.CustomRegion, Fps = 30 };

    [Theory]
    [InlineData(CursorEffectMode.Default, 1)]
    [InlineData(CursorEffectMode.Hidden, 0)]
    public async Task UsesOutputRelativeCoordinatesAndDownloadsHardwareFrames(CursorEffectMode cursor, int draw)
    {
        var config = Config; config.CursorEffect = cursor;
        var plan = await Provider().PrepareAsync(config, new(-1800, 100, 640, 480), null, default);
        Assert.Equal($"-f lavfi -i \"ddagrab=output_idx=2:framerate=30:draw_mouse={draw}:video_size=640x480:offset_x=120:offset_y=100:output_fmt=bgra:dup_frames=1,hwdownload,format=bgra\" ", plan.VideoInputArguments);
    }

    [Theory]
    [InlineData(AudioSourceType.None)]
    [InlineData(AudioSourceType.SystemOnly)]
    [InlineData(AudioSourceType.MicrophoneOnly)]
    [InlineData(AudioSourceType.SystemAndMicrophone)]
    public async Task AudioArgumentsRemainUnchanged(AudioSourceType audio)
    {
        var config = Config; config.AudioSource = audio; config.MicrophoneDeviceId = "test mic";
        var provider = Provider();
        var plan = await provider.PrepareAsync(config, Screen, null, default);
        var dda = provider.BuildInputArguments(plan, config, true, "-f s16le -i pipe:7 ", null);
        var gdi = new WindowsFFmpegProvider().BuildInputArguments(config, -1920, 0, 1920, 1080, false, true, "-f s16le -i pipe:7 ", null);
        Assert.Equal(gdi[(gdi.IndexOf("-i desktop ", StringComparison.Ordinal) + 11)..], dda[plan.VideoInputArguments.Length..]);
    }

    [Fact]
    public async Task PinnedOutputCannotSilentlyChangeTarget()
    {
        var provider = Provider(); var config = Config;
        var plan = await provider.PrepareAsync(config, Screen, null, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.PrepareAsync(config, Screen,
            plan.Selection with { DeviceName = "OTHER" }, default));
    }

    [Fact]
    public async Task InvalidFrameRateIsRejectedBeforeArgumentBuilding()
    {
        var config = Config; config.Fps = -1;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Provider().PrepareAsync(config, Screen, null, default));
    }
}
