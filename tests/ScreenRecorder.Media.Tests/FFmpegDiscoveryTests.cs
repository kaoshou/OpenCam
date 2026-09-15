using ScreenRecorder.Media.FFmpeg;
using Xunit;

namespace ScreenRecorder.Media.Tests;

public class FFmpegDiscoveryTests
{
    [Fact]
    public void FFmpegDiscovery_ShouldFindFFmpegOnSystem()
    {
        var ffmpeg = FFmpegDiscovery.FindFFmpegExecutable();
        Assert.NotNull(ffmpeg);
        Assert.True(File.Exists(ffmpeg));
    }

    [Fact]
    public void FFmpegDiscovery_ShouldFindFFprobeOnSystem()
    {
        var ffprobe = FFmpegDiscovery.FindFFprobeExecutable();
        Assert.NotNull(ffprobe);
        Assert.True(File.Exists(ffprobe));
    }
}
