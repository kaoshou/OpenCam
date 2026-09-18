using ScreenRecorder.Media.FFmpeg;
using Xunit;

namespace ScreenRecorder.Media.Tests;

[CollectionDefinition("FFmpeg discovery environment", DisableParallelization = true)]
public sealed class FFmpegDiscoveryEnvironmentCollection
{
}

[Collection("FFmpeg discovery environment")]
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

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("ffprobe")]
    public void FFmpegDiscovery_ShouldPreferBundledExecutableOverSystemPath(string executableName)
    {
        var executableFileName = OperatingSystem.IsWindows()
            ? $"{executableName}.exe"
            : executableName;
        var bundledPath = Path.Combine(AppContext.BaseDirectory, executableFileName);
        var pathDirectory = Path.Combine(
            Path.GetTempPath(),
            "OpenCamFFmpegDiscovery_" + Guid.NewGuid().ToString("N"));
        var pathExecutable = Path.Combine(pathDirectory, executableFileName);
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        Directory.CreateDirectory(pathDirectory);
        File.WriteAllText(bundledPath, "bundled fixture");
        File.WriteAllText(pathExecutable, "path fixture");

        try
        {
            Environment.SetEnvironmentVariable("PATH", pathDirectory);

            var discovered = executableName == "ffmpeg"
                ? FFmpegDiscovery.FindFFmpegExecutable()
                : FFmpegDiscovery.FindFFprobeExecutable();

            Assert.Equal(bundledPath, discovered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            File.Delete(bundledPath);
            Directory.Delete(pathDirectory, recursive: true);
        }
    }
}
